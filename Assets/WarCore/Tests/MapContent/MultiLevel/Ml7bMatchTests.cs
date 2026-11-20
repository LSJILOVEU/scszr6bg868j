using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;
using static WarCore.Tests.RespawnWorld;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-7b acceptance 4 (match data), 5 (deployment), 6 (respawn and reinforcement), 7 (world build), 2b (world and deployment
    // sides) and invariant 2 (snapshot).
    public sealed class Ml7bMatchTests
    {
        // 80 x 40: plate 1 flat on cells x 40..43, z 30..33, top 3 m; cutout (42, 31); a raised wall (bottom 3 m) on (40, 30).
        private static byte[] Arena(bool plates = true) => Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null,
            plates ? new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M) } : new MapPlate[0], plates ? new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) } : null,
            plates ? new[] { Ml4Maps.Wall(1, 40, 30, 1, 1, 2 * M, 3 * M) } : null));

        private static SimVec3 Up(long xMm, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), SimFix.FromRaw(3 * M), SimFix.Ratio(zMm, 1000));
        private static SimVec3 Flat(long xMm, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), default, SimFix.Ratio(zMm, 1000));

        private static List<UnitSpawn> Reserves() => Units(U(20, 30, 10, reserve: true), U(21, 32, 10, reserve: true));

        private static M6World World(MatchPack pack, bool player = false, bool plates = true, long[] kills = null)
        {
            var w = new M6World(Recipe(Reserves(), pack, player).WithPlatesPreview(), Arena(plates));
            if (kills != null) w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, kills), SimSystemPhase.Queries);
            return w;
        }

        private sealed class GroundOnly : IUnitDeployer
        {
            internal int Calls;
            public ulong PolicyKey => 0;
            public bool TryDeploy(EntityId unit, SimVec3 at) { Calls++; return true; }
        }

        // 4: ranges and the spawn rule.
        [Test]
        public void AnchorLayersAreChecked()
        {
            Assert.DoesNotThrow(() => Assert.That(new SpawnAnchor(1, default, 4096).Layer, Is.EqualTo(4096)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnAnchor(1, default, 4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnAnchor(1, default, -1));
            Assert.DoesNotThrow(() => Assert.That(new ControlPointDef(1, default, SimFix.FromInt(1), 0, true, default, 4096).SpawnLayer, Is.EqualTo(4096)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ControlPointDef(1, default, SimFix.FromInt(1), 0, true, default, 4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ControlPointDef(1, default, SimFix.FromInt(1), 0, true, default, -1));
            Assert.That(Assert.Throws<ArgumentException>(() => new ControlPointDef(1, default, SimFix.FromInt(1), 0, false, default, 1)).Message,
                Does.StartWith("A spawn layer needs a spawn."));
            Assert.That(new ControlPointDef(1, default, SimFix.FromInt(1)).SpawnLayer, Is.EqualTo(0));
        }

        // 1 and 4: all layers 0 keeps the locked digest (C1cContentTests.Guard90); one layer anywhere changes it.
        [Test]
        public void OnlyLayersChangeTheDigest()
        {
            MatchPack Pack(int pointLayer, int baseLayer)
            {
                ControlPointDef[] points = RespawnDigestLockTests.Points().Select(p => new ControlPointDef(p.Id, p.Center, p.Radius, p.InitialOwner,
                    p.HasSpawn || pointLayer != 0, p.Spawn, p.Id == RespawnDigestLockTests.Points()[0].Id ? pointLayer : 0)).ToArray();
                SpawnAnchor[] bases = RespawnDigestLockTests.Bases().Select((b, i) => new SpawnAnchor(b.Team, b.Point, i == 0 ? baseLayer : 0)).ToArray();
                return new MatchPack(points, bases, 2, 1, false, 300, InterruptMode.Reset, 1, 90, 30, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2), 5, 90);
            }
            Assert.That(RespawnDigestLockTests.Points()[0].HasSpawn, Is.True, "the first locked point has a spawn");
            Assert.That(Pack(0, 0).PolicyDigest, Is.EqualTo(C1cContentTests.Guard90));
            var digests = new[] { Pack(0, 0).PolicyDigest, Pack(1, 0).PolicyDigest, Pack(0, 1).PolicyDigest, Pack(2, 0).PolicyDigest, Pack(0, 2).PolicyDigest };
            Assert.That(digests.Distinct().Count(), Is.EqualTo(5));
        }

        // 5: the three-argument deployment, directly.
        [Test]
        public void DeploymentOnAPlate()
        {
            MatchPack pack = Pack(Points(), Bases(new SpawnAnchor(1, P(5, 38))), 300);
            M6World w = World(pack);
            var deployer = Field<IUnitDeployer>(w.Reinforce, "deployer");
            EntityId ally = w.Unit(Ally);
            Assert.That(deployer.TryDeploy(ally, Flat(42500, 31500), 1), Is.False, "the cutout");
            Assert.That(deployer.TryDeploy(ally, Flat(40500, 30500), 1), Is.False, "under the raised wall");
            Assert.That(deployer.TryDeploy(ally, Flat(30500, 10500), 1), Is.False, "off the plate");
            Assert.That((w.Read<VerticalState>(Ally).Layer, w.Read<Position>(Ally).Value), Is.EqualTo(((ushort)0, P(20, 38))), "nothing written");
            Assert.That(deployer.TryDeploy(ally, Flat(41500, 32500), 1), Is.True);
            VerticalState v = w.Read<VerticalState>(Ally);
            Assert.That((v.Layer, v.TargetLayer, w.Read<Position>(Ally).Value), Is.EqualTo(((ushort)1, (ushort)0, Up(41500, 32500))));
            Assert.That(deployer.TryDeploy(ally, P(30, 10), 0), Is.True, "layer 0 is the ground deployment");
            Assert.That((w.Read<VerticalState>(Ally).Layer, w.Read<Position>(Ally).Value), Is.EqualTo(((ushort)0, P(30, 10))));

            var flat = World(pack, plates: false);
            Assert.Throws<NotSupportedException>(() => Field<IUnitDeployer>(flat.Reinforce, "deployer").TryDeploy(flat.Unit(Ally), Flat(41500, 32500), 1));
            IUnitDeployer ground = new GroundOnly();
            Assert.DoesNotThrow(() => Assert.That(ground.TryDeploy(default, default, 0), Is.True));
            Assert.That(((GroundOnly)ground).Calls, Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() => ground.TryDeploy(default, default, 1));
        }

        // 6: the player dies at tick 5; 30 ticks later the reserve deploys at the team's only anchor, a base on the plate, and
        // control switches to it. Read at the end of that tick (the switched-to unit gets no order).
        [Test]
        public void ThePlayerRespawnsOnThePlate()
        {
            MatchPack pack = Pack(Points(), new[] { new SpawnAnchor(1, Up(41500, 32500), 1), new SpawnAnchor(2, P(78, 20)) }, 30);
            M6World w = World(pack, player: true, kills: new long[] { 5 });
            int t = 0;
            while (Controlled(w) != 20 && t < 100) { w.Step(); t++; }
            Assert.That(Controlled(w), Is.EqualTo(20UL));
            Assert.That((w.Read<VerticalState>(20).Layer, w.Read<Position>(20).Value), Is.EqualTo(((ushort)1, Up(40500, 31500))),
                "slot 0 of the 2 x 2 grid: one metre back on each axis");
        }

        // 6: point 1 (owned by team 1) has its spawn on the plate. Attempt 0 deploys at team 1's ground base, attempt 1 at the
        // spawn: its first slot (1, -1) lands on the cutout and is skipped; the next, (-1, 1), stands on the plate.
        [Test]
        public void ReinforcementsLandOnThePlate()
        {
            var points = Points(new ControlPointDef(1, P(41, 32), SimFix.FromInt(2), 1, true, Up(41500, 32500), 1));
            MatchPack pack = Pack(points, Bases(new SpawnAnchor(1, P(5, 30))), 0, interval: 10);
            M6World w = World(pack);
            var records = new List<(DeployRecord, int)>();
            for (int t = 0; t < 25; t++)
            {
                w.Step();
                foreach (DeployRecord r in w.Reinforce.Deployed.ToArray())
                    records.Add((r, w.Sim.Entities.Read(w.Sim.Entities.Resolve<VerticalState>(), w.Sim.Entities.Resolve(new EntityLink(r.Unit))).Layer));
            }
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That((records[0].Item1.Point, records[0].Item2), Is.EqualTo((P(4, 29), 0)), "the ground base, slot 0");
            Assert.That((records[1].Item1.Point, records[1].Item2), Is.EqualTo((Up(40500, 33500), 1)), "the plate spawn, slot 2");
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(2L));
        }

        // 7: the world build refuses plate anchors it cannot place.
        [Test]
        public void TheWorldBuildChecksPlateAnchors()
        {
            string Error(MatchPack pack, bool plates) =>
                Assert.Throws<ArgumentException>(() => SimSession.Create(Recipe(Reserves(), pack, false).WithPlatesPreview(), Arena(plates))).Message;
            MatchPack OnBase(SimVec3 at, int layer) => Pack(Points(), Bases(new SpawnAnchor(1, at, layer)), 0);
            Assert.That(Error(OnBase(Up(41500, 32500), 1), false), Does.StartWith("Match base 0 names layer 1 on a map without floor plates."));
            Assert.That(Error(OnBase(Up(41500, 32500), 2), true), Does.StartWith("Match base 0 names layer 2 of a map with 1 plates."));
            Assert.That(Error(OnBase(Up(42500, 31500), 1), true), Does.StartWith("Match base 0 is not on a solid cell of layer 1."));
            MatchPack spawn = Pack(Points(new ControlPointDef(1, P(41, 32), SimFix.FromInt(2), 1, true, Up(30500, 10500), 1)), Bases(new SpawnAnchor(1, P(5, 30))), 0);
            Assert.That(Error(spawn, true), Does.StartWith("Match spawn of point 1 is not on a solid cell of layer 1."));
            Assert.That(SimSession.Create(Recipe(Reserves(), OnBase(Up(40500, 30500), 1), false).WithPlatesPreview(), Arena()), Is.Not.Null,
                "a solid cell under a wall builds: walkability is decided at each deployment");
        }

        // 2b, world build and deployment: on cutout cell (42, 31) the minimum x edge (42 m) is the cutout, 1 mm less is cell (41, 31).
        [Test]
        public void TheCellEdgeDecidesBuildAndDeployment()
        {
            MatchPack OnBase(long xMm) => Pack(Points(), Bases(new SpawnAnchor(1, Up(xMm, 31500), 1)), 0);
            Assert.Throws<ArgumentException>(() => SimSession.Create(Recipe(Reserves(), OnBase(42000), false).WithPlatesPreview(), Arena()));
            M6World w = new M6World(Recipe(Reserves(), OnBase(41999), false).WithPlatesPreview(), Arena());
            var deployer = Field<IUnitDeployer>(w.Reinforce, "deployer");
            Assert.That(deployer.TryDeploy(w.Unit(Ally), Flat(42000, 31500), 1), Is.False);
            Assert.That(deployer.TryDeploy(w.Unit(Ally), Flat(41999, 31500), 1), Is.True);
        }

        // 7: description -> bake -> recipe -> world; the units on plates stand on their layer.
        [Test]
        public void AMapRecipeCarriesTheLayers()
        {
            MapBakeResult result = MapBake.Run(Ml7bMaps.Bytes(Ml7bDescTests.OnPlates()));
            WorldRecipe recipe = MapRecipe.From(result, RwrPack.Load("rwr-m6"));
            Assert.That((recipe.Units[0].Layer, recipe.Units[1].Layer), Is.EqualTo((1, 0)));
            Assert.That((recipe.Match.Bases[0].Layer, recipe.Match.Points[0].SpawnLayer), Is.EqualTo((2, 2)));
            SimSession s = SimSession.Create(recipe.WithPlatesPreview(), result.WcMapBytes);
            Assert.That((Vertical(s, 1).Layer, M3TestWorld.Position(s, 1).Y.Raw), Is.EqualTo(((ushort)1, (25 * M / 10) * 3 / 4)), "unit 1 on the stair at x 12.5 m");
            Assert.That((Vertical(s, 2).Layer, M3TestWorld.Position(s, 2).Y.Raw), Is.EqualTo(((ushort)0, 0L)));
        }

        // Invariant 2: a match with plate anchors resumes from tick 150 to the same hashes up to 300. Team 1's base is on the
        // plate and point 1's spawn on the ground: reserve 20 deploys at the base at tick 100, reserve 21 at the spawn at 200.
        [Test]
        public void APlateMatchResumesToTheSameHashes()
        {
            var points = Points(new ControlPointDef(1, P(41, 32), SimFix.FromInt(2), 1, true, P(5, 30)));
            MatchPack pack = Pack(points, Bases(new SpawnAnchor(1, Up(41500, 32500), 1)), 0, interval: 100);
            M6World a = World(pack);
            a.Step(150);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            var expected = new List<ulong>();
            for (int i = 0; i < 150; i++) { a.Step(); expected.Add(a.Sim.StateHashRetained()); }
            Assert.That(a.Reinforce.Successes(1), Is.EqualTo(2L));
            M6World b = World(pack);
            b.Sim.RestoreRetainedSnapshot(snapshot);
            var resumed = new List<ulong>();
            for (int i = 0; i < 150; i++) { b.Step(); resumed.Add(b.Sim.StateHashRetained()); }
            Assert.That(resumed, Is.EqualTo(expected));
            Assert.That((b.Read<VerticalState>(20).Layer, b.Read<Position>(20).Value), Is.EqualTo(((ushort)1, Up(40500, 31500))), "at the plate base");
            Assert.That((b.Read<VerticalState>(21).Layer, b.Read<Position>(21).Value.Y.Raw), Is.EqualTo(((ushort)0, 0L)), "at the ground spawn");
        }
    }
}
