using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-9b acceptance 3 (match data, world build), 4 (capture), 5 (respawn), 8 (deployment), 6b (the AI keeps off plate slots)
    // and invariant 2 (snapshot).
    public sealed class Ml9bMatchTests
    {
        // 80 x 40: plate 1 (layer 1) flat on cells x 40..43, z 30..33, top 3 m (bottom 2.5 m: the ground under it is walkable),
        // cutout (42, 31); plate 2 (layer 2), a stair along x on cells x 36..39, z 31..32, rising 0 -> 3 m to the floor's west edge.
        internal static byte[] Arena(bool plates = true) => Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null,
            plates ? new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M), Ml4Maps.Slope(2, 36, 31, 4, 2, "x", 0, 3 * M) } : new MapPlate[0],
            plates ? new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) } : null));

        internal static SimVec3 Up(long xMm, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), SimFix.FromRaw(3 * M), SimFix.Ratio(zMm, 1000));
        internal static SimVec3 Flat(long xMm, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), default, SimFix.Ratio(zMm, 1000));
        internal static UnitSpawn L(ulong key, long xMm, long zMm, int layer, int team = 1, bool reserve = false) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 1, team, -1, reserve, layer: layer);

        // The point upstairs (centre on the floor, layer 1) and a point on the ground at the same plane position, radius 3 m.
        private static ControlPointDef Upstairs(int owner = 0, bool spawn = false) =>
            new ControlPointDef(1, Up(41500, 32500), SimFix.FromInt(3), owner, spawn, spawn ? Up(41500, 32500) : default, spawn ? 1 : 0, 1);
        private static ControlPointDef Downstairs() => new ControlPointDef(2, Flat(41500, 32500), SimFix.FromInt(3));

        private static M6World World(IEnumerable<UnitSpawn> units, MatchPack pack, bool plates = true, bool player = false, long[] kills = null)
        {
            WorldRecipe recipe = Recipe(units, pack, player);
            var w = new M6World(plates ? recipe.WithPlatesPreview() : recipe, Arena(plates));
            if (kills != null) w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, kills), SimSystemPhase.Queries);
            return w;
        }

        private sealed class Layers : IUnitLayers
        {
            private readonly int layer;
            internal Layers(int layer) { this.layer = layer; }
            public int LayerOf(EntityId unit) => layer;
        }

        // 3: the centre's layer range; the old constructor keeps layer 0.
        [Test]
        public void CentreLayersAreChecked()
        {
            Assert.DoesNotThrow(() => Assert.That(new ControlPointDef(1, default, SimFix.FromInt(1), 0, false, default, 0, 4096).Layer, Is.EqualTo(4096)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ControlPointDef(1, default, SimFix.FromInt(1), 0, false, default, 0, 4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ControlPointDef(1, default, SimFix.FromInt(1), 0, false, default, 0, -1));
            Assert.That(new ControlPointDef(1, default, SimFix.FromInt(1)).Layer, Is.EqualTo(0));
        }

        // 1 and 3: all centres on layer 0 keep the locked digest (C1cContentTests.Guard90) and, with a spawn layer, the ML-7b digest
        // (taken from the code before this change); any centre layer changes it, on any point.
        [Test]
        public void OnlyCentreLayersChangeTheDigest()
        {
            ControlPointDef[] locked = RespawnDigestLockTests.Points();
            MatchPack Pack(int index, int layer, int spawnLayer = 0)
            {
                ControlPointDef[] points = locked.Select((p, i) => new ControlPointDef(p.Id, p.Center, p.Radius, p.InitialOwner, p.HasSpawn || (i == 0 && spawnLayer != 0),
                    p.Spawn, i == 0 ? spawnLayer : 0, i == index ? layer : 0)).ToArray();
                return new MatchPack(points, RespawnDigestLockTests.Bases(), 2, 1, false, 300, InterruptMode.Reset, 1, 90, 30, 9000, SimFix.FromInt(35),
                    4, 4, SimFix.Ratio(3, 2), 5, 90);
            }
            Assert.That(locked.Length, Is.GreaterThan(1));
            Assert.That(Pack(0, 0).PolicyDigest, Is.EqualTo(C1cContentTests.Guard90));
            Assert.That(Pack(0, 0).HasCenterLayers(), Is.False);
            Assert.That(Pack(0, 0, spawnLayer: 1).PolicyDigest, Is.EqualTo(SpawnLayerDigest), "the ML-7b layer section alone");
            int last = locked.Length - 1;
            var digests = new[] { Pack(0, 0).PolicyDigest, Pack(0, 1).PolicyDigest, Pack(0, 2).PolicyDigest, Pack(last, 1).PolicyDigest, Pack(last, 2).PolicyDigest,
                Pack(0, 0, spawnLayer: 1).PolicyDigest, Pack(0, 1, spawnLayer: 1).PolicyDigest };
            Assert.That(digests.Distinct().Count(), Is.EqualTo(digests.Length));
            Assert.That(Pack(last, 1).HasCenterLayers(), Is.True);
        }

        // The digest of the locked points with point 0's spawn on layer 1, computed by the code before ML-9b.
        private const ulong SpawnLayerDigest = 0xAAF7063B1742319CUL;

        // 3: the world build refuses a centre it cannot place, as the other plate anchors.
        [Test]
        public void TheWorldBuildChecksTheCentre()
        {
            List<UnitSpawn> units = Units();
            string Error(ControlPointDef point, bool plates) =>
                Assert.Throws<ArgumentException>(() => SimSession.Create(Recipe(units, Pack(Points(point), Bases(new SpawnAnchor(1, P(5, 30))), 0), false)
                    .WithPlatesPreview(), Arena(plates))).Message;
            ControlPointDef At(SimVec3 at, int layer) => new ControlPointDef(1, at, SimFix.FromInt(2), 0, false, default, 0, layer);
            Assert.That(Error(At(Up(41500, 32500), 1), false), Does.StartWith("Match centre of point 1 names layer 1 on a map without floor plates."));
            Assert.That(Error(At(Up(41500, 32500), 3), true), Does.StartWith("Match centre of point 1 names layer 3 of a map with 2 plates."));
            Assert.That(Error(At(Up(42500, 31500), 1), true), Does.StartWith("Match centre of point 1 is not on a solid cell of layer 1."));
            Assert.That(SimSession.Create(Recipe(units, Pack(Points(At(Up(41500, 32500), 1)), Bases(new SpawnAnchor(1, P(5, 30))), 0), false).WithPlatesPreview(),
                Arena()), Is.Not.Null);
        }

        // 4: one unit of team 1 inside both circles on the plane (the point upstairs and the point on the ground, same centre),
        // standing on the floor, under it, or on the stair. Only the point on its own layer counts it; without plates both do.
        [TestCase("up", true, 10, 0)]
        [TestCase("under", true, 0, 10)]
        [TestCase("stair", true, 0, 0)]
        [TestCase("up", false, 10, 10)]
        [TestCase("under", false, 10, 10)]
        [TestCase("stair", false, 10, 10)]
        public void OnlyTheSameLayerCaptures(string where, bool plates, int upstairs, int downstairs)
        {
            (long x, long z, int layer) = where == "up" ? (41500L, 33500L, 1) : where == "under" ? (41500L, 33500L, 0) : (39500L, 31500L, 2);
            ControlPointDef up = plates ? Upstairs() : new ControlPointDef(1, Flat(41500, 32500), SimFix.FromInt(3));
            M6World w = World(Units(L(30, x, z, plates ? layer : 0)), Pack(Points(up, Downstairs()), Bases(), 0), plates);
            w.Step(10);
            Assert.That((w.Capture.Points[0].Progress, w.Capture.Points[1].Progress), Is.EqualTo((upstairs, downstairs)));
            Assert.That((w.Capture.Points[0].Capturing, w.Capture.Points[1].Capturing), Is.EqualTo((upstairs > 0 ? 1 : 0, downstairs > 0 ? 1 : 0)));
        }

        // 4: the rule itself. Without layers the plane alone decides, whatever the point's layer; with layers, the layer as well.
        [Test]
        public void TheLayerRuleNeedsLayers()
        {
            ControlPointDef up = Upstairs();
            Assert.That(CaptureSystem.Inside(null, default, Flat(41500, 33500), up), Is.True);
            Assert.That(CaptureSystem.Inside(new Layers(0), default, Flat(41500, 33500), up), Is.False);
            Assert.That(CaptureSystem.Inside(new Layers(1), default, Up(41500, 33500), up), Is.True);
            Assert.That(CaptureSystem.Inside(new Layers(1), default, Up(41500, 35600), up), Is.False, "outside on the plane");
            Assert.That(CaptureSystem.Inside(Up(41500, 33500), 1, up) && !CaptureSystem.Inside(Up(41500, 33500), 2, up), Is.True);
        }

        // 5: point 1 (owned by team 1, spawn and centre upstairs) and team 1's base on the ground. The player dies at tick 5; 30
        // ticks later the reserve deploys. An enemy under the floor does not contest the point; one upstairs does.
        [TestCase(0, 1, 40500, 31500)]
        [TestCase(1, 0, 4000, 29000)]
        public void AnEnemyUnderTheFloorDoesNotContest(int enemyLayer, int layer, long xMm, long zMm)
        {
            MatchPack pack = Pack(Points(Upstairs(owner: 1, spawn: true)), Bases(new SpawnAnchor(1, P(5, 30))), 30);
            M6World w = World(Units(L(20, 30000, 10000, 0, reserve: true), L(30, 41500, 33500, enemyLayer, team: 2)), pack, player: true, kills: new long[] { 5 });
            int t = 0;
            while (Controlled(w) != 20 && t < 100) { w.Step(); t++; }
            Assert.That(Controlled(w), Is.EqualTo(20UL));
            SimVec3 at = layer == 1 ? Up(xMm, zMm) : Flat(xMm, zMm);
            Assert.That((w.Read<VerticalState>(20).Layer, w.Read<Position>(20).Value), Is.EqualTo(((ushort)layer, at)));
        }

        // 8: on a map with plates a deployment keeps its distance only from units on its own layer.
        [Test]
        public void DeploymentKeepsItsDistanceOnItsLayer()
        {
            MatchPack pack = Pack(Points(), Bases(), 0);
            M6World under = World(Units(L(30, 41500, 32500, 0)), pack);
            var deployer = Field<IUnitDeployer>(under.Reinforce, "deployer");
            Assert.That(deployer.TryDeploy(under.Unit(Ally), Flat(41500, 32500), 0), Is.False, "beside a unit on the ground");
            Assert.That(deployer.TryDeploy(under.Unit(Ally), Flat(41500, 32500), 1), Is.True, "above it, on the floor");
            Assert.That(under.Read<VerticalState>(Ally).Layer, Is.EqualTo((ushort)1));

            M6World up = World(Units(L(30, 41500, 32500, 1)), pack);
            deployer = Field<IUnitDeployer>(up.Reinforce, "deployer");
            Assert.That(deployer.TryDeploy(up.Unit(Ally), Flat(41500, 32500), 1), Is.False, "beside a unit on the floor");
            Assert.That(deployer.TryDeploy(up.Unit(Ally), Flat(41500, 32500), 0), Is.True, "below it, on the ground");
        }

        // 6b: the AI thinks every 10 ticks and goes for the only point, a neutral one of radius 1 m (today's AI walks to the slots
        // of a point it goes for, not of one it owns). Team 1's four soldiers start at the floor's corners, outside the circle;
        // team 2 waits far away on the ground. On the ground (the control: the point and the soldiers under the floor) they walk
        // to the slots; upstairs the AI of today has no slots (ML-11 gives it plate slots), so none moves and none leaves the floor.
        [TestCase(0)]
        [TestCase(1)]
        public void TheAiDoesNotLeadSoldiersOffTheFloor(int layer)
        {
            UnitSpawn Walker(ulong key, long xMm, long zMm, int team, int on) => new UnitSpawn(key, 0, team, xMm, zMm, 3000, team, -1, false, layer: on);
            var units = new List<UnitSpawn>
            {
                Walker(1, 40500, 30500, 1, layer), Walker(2, 43500, 30500, 1, layer), Walker(3, 40500, 33500, 1, layer), Walker(4, 43500, 33500, 1, layer),
                Walker(10, 70000, 10000, 2, 0), Walker(11, 72000, 10000, 2, 0),
            };
            var point = new ControlPointDef(1, layer == 1 ? Up(41500, 32500) : Flat(41500, 32500), SimFix.FromInt(1), 0, false, default, 0, layer);
            MatchPack pack = M6World.Pack(new[] { point }, interval: 1000000, think: 10, bases: Bases(new SpawnAnchor(1, P(5, 30))));
            M6World w = Ml7bMaps.NoThrow(() => new M6World(M6World.Endless(Recipe(units, pack, false)).WithPlatesPreview(), Arena()));
            var start = new SimVec3[5];
            for (ulong key = 1; key <= 4; key++) start[key] = w.Read<Position>(key).Value;
            long moved = 0;
            for (int t = 0; t < 300; t++)
            {
                w.Step();
                for (ulong key = 1; key <= 4; key++)
                {
                    SimVec3 at = w.Read<Position>(key).Value;
                    Assert.That((w.Read<VerticalState>(key).Layer, at.Y.Raw), Is.EqualTo(((ushort)layer, layer * 3 * M)), "tick " + t + ", unit " + key);
                    moved = Math.Max(moved, Math.Abs(at.X.Raw - start[key].X.Raw) + Math.Abs(at.Z.Raw - start[key].Z.Raw));
                }
            }
            if (layer == 0) Assert.That(moved, Is.GreaterThan(M), "on the ground the AI walks them to the slots");
            else Assert.That(moved, Is.EqualTo(0L), "upstairs nobody is sent anywhere");
        }

        // Invariant 2: team 1 takes the point upstairs at tick 200 while an enemy stands under the floor; a snapshot at 150 resumes
        // to the same hashes up to 300.
        [Test]
        public void APlatePointMatchResumesToTheSameHashes()
        {
            MatchPack pack = Pack(Points(Upstairs()), Bases(), 0, captureTicks: 200);
            List<UnitSpawn> units = Units(L(30, 41500, 33500, 1), L(31, 40500, 33500, 1), L(32, 41500, 32500, 0, team: 2));
            M6World a = World(units, pack);
            a.Step(150);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            var expected = new List<ulong>();
            for (int i = 0; i < 150; i++) { a.Step(); expected.Add(a.Sim.StateHashRetained()); }
            Assert.That(a.Capture.Points[0].Owner, Is.EqualTo(1));
            M6World b = World(units, pack);
            b.Sim.RestoreRetainedSnapshot(snapshot);
            var resumed = new List<ulong>();
            for (int i = 0; i < 150; i++) { b.Step(); resumed.Add(b.Sim.StateHashRetained()); }
            Assert.That(resumed, Is.EqualTo(expected));
            Assert.That(b.Capture.Points[0].Owner, Is.EqualTo(1));
        }
    }
}
