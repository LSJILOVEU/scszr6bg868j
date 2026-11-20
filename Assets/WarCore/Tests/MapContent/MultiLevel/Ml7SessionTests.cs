using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-7 acceptance 1-3 and 5-9: the vertical state through the bridge. Floored plate tops (K11): layer 1 is 3 m (196608 raw)
    // everywhere; layer 2 at x = 2.5 m is 0.5 m + 1.5 m * 0.5 = 1.25 m (81920 raw).
    public sealed class Ml7SessionTests
    {
        private const long Top1 = 3 * M, Slope25 = 81920;
        private const string Refused = "Maps with floor plates are not supported by the runtime yet.";

        private static readonly UnitSpawn[] Three = { Unit(1, 1, 500, 500), Unit(2, 2, 6500, 1500, 1), Unit(3, 3, 2500, 3500, 2) };

        private static string Snapshot(SimSession s) => Encoding.ASCII.GetString(M3TestWorld.Sim(s).SaveRetainedSnapshot());

        // 1: a map without plates registers nothing new, with or without the preview.
        [Test]
        public void WithoutPlatesNothingIsNew()
        {
            var units = new[] { Unit(1, 1, 500, 500) };
            SimSession plain = World(NoPlates, units, preview: false), opened = World(NoPlates, units);
            Assert.That(Snapshot(plain), Does.Not.Contain(VerticalState.ComponentId));
            Assert.Throws<InvalidOperationException>(() => M3TestWorld.Sim(plain).Entities.Resolve<VerticalState>());
            Assert.That(M3TestWorld.Sim(plain).StableContent.Digest, Is.EqualTo(new StableContentManifest(1).Digest), "no plates.nav key");
            Assert.That(M3TestWorld.Sim(opened).SaveRetainedSnapshot(), Is.EqualTo(M3TestWorld.Sim(plain).SaveRetainedSnapshot()));
        }

        // 2: the preview switch, the mover, the content key.
        [Test]
        public void ThePreviewOpensPlateMaps()
        {
            var units = new[] { Unit(1, 1, 500, 500) };
            Assert.That(Assert.Throws<NotSupportedException>(() => World(Plates, units, preview: false)).Message, Is.EqualTo(Refused));
            WorldRecipe forged = Recipe(units, preview: false).With(new ForgedPreview());
            Assert.That(Assert.Throws<NotSupportedException>(() => SimSession.Create(forged, Plates)).Message, Is.EqualTo(Refused), "an option with the same id");
            Assert.That(Assert.Throws<NotSupportedException>(() => SimSession.Create(forged.WithPlatesPreview(), Plates)).Message, Is.EqualTo(Refused),
                "the forged id came first, so the builder's own option is not added");
            Assert.That(Assert.Throws<NotSupportedException>(() => SimSession.Create(Recipe(units, mover: MoverKind.Interim), Plates)).Message,
                Is.EqualTo("Plate maps need a path or crowd mover."));

            SimSession s = World(Plates, units);
            WcMap map = WcMapReader.Read(Plates);
            ulong nav = WcMapLayeredNav.Build(map, null, map.ContentId).Digest;
            // ML-8: a plate world runs on the terrain branch, so its tables (default rules without a pack) add their own key.
            ulong tables = TerrainDerivation.Derive(map, null, map.ContentId).Digest;
            Assert.That(M3TestWorld.Sim(s).StableContent.Digest, Is.EqualTo(new StableContentManifest(1, new StableContentKey("plates.nav", nav),
                new StableContentKey("terrain.tables", tables)).Digest));
            Assert.That(Snapshot(s), Does.Contain(VerticalState.ComponentId));
            Assert.DoesNotThrow(() => SimSession.Create(Recipe(units, mover: MoverKind.InterimPath), Plates));
            Assert.DoesNotThrow(() => M3TestWorld.Sim(s).Step(new InputFrame(default, default, aimLayer: 1)), "the input carries layers");
        }

        // 3 and 7: every unit has a vertical state; a unit on a plate stands on its floored top.
        [Test]
        public void UnitsStandOnTheirLayers()
        {
            SimSession s = null;
            Assert.DoesNotThrow(() => s = World(Plates, Three));
            Assert.That((Vertical(s, 1).Layer, Vertical(s, 2).Layer, Vertical(s, 3).Layer), Is.EqualTo(((ushort)0, (ushort)1, (ushort)2)));
            Assert.That((Vertical(s, 1).TargetLayer, Vertical(s, 2).TargetLayer, Vertical(s, 3).TargetLayer), Is.EqualTo(((ushort)0, (ushort)0, (ushort)0)));
            Assert.That((M3TestWorld.Position(s, 1).Y.Raw, M3TestWorld.Position(s, 2).Y.Raw, M3TestWorld.Position(s, 3).Y.Raw), Is.EqualTo((0L, Top1, Slope25)));
            Assert.That(M3TestWorld.Component<MoveIntent>(s, 2).Target, Is.EqualTo(M3TestWorld.Position(s, 2)), "the intent's target is the plate point");
            for (int i = 0; i < 30; i++) s.StepOnce();
            Assert.That((M3TestWorld.Position(s, 2).Y.Raw, M3TestWorld.Position(s, 3).Y.Raw), Is.EqualTo((Top1, Slope25)), "idle units stay on their plates");
        }

        [Test]
        public void TheSerializerReadsLayersAndAir()
        {
            var serializer = new VerticalStateSerializer();
            Assert.That((serializer.TypeId, serializer.Version), Is.EqualTo((2010, 1)));
            Assert.That(UnitSpawn.MaxLayer, Is.EqualTo(PlateField.MaxPlates), "the bridge's layer limit is the Sim's");
            VerticalState Read(int layer, int target)
            {
                var writer = new SimStateWriter();
                writer.Int32(layer); writer.Int32(target);
                return serializer.Read(new SimStateReader(writer.ToArray()));
            }
            var w = new SimStateWriter();
            serializer.Write(w, new VerticalState(5, 7));
            VerticalState back = serializer.Read(new SimStateReader(w.ToArray()));
            Assert.That((back.Layer, back.TargetLayer), Is.EqualTo(((ushort)5, (ushort)7)));
            VerticalState high = default, air = default;
            Assert.DoesNotThrow(() => high = Read(4096, 0));
            Assert.DoesNotThrow(() => air = Read(0, 65535));
            Assert.That((high.Layer, air.TargetLayer), Is.EqualTo(((ushort)4096, VerticalState.Air)));
            Assert.Throws<SimSnapshotException>(() => Read(4097, 0));
            Assert.Throws<SimSnapshotException>(() => Read(0, 65534));
            Assert.Throws<SimSnapshotException>(() => Read(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalState(VerticalState.Air, 0), "no entry point takes air");
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalState(0, 4097));
        }

        [Test]
        public void SnapshotsOfWorldsWithAndWithoutPlatesRefuseEachOther()
        {
            var units = new[] { Unit(1, 1, 500, 500) };
            byte[] withPlates = M3TestWorld.Sim(World(HighOnly, units)).SaveRetainedSnapshot();
            byte[] without = M3TestWorld.Sim(World(NoPlates, units)).SaveRetainedSnapshot();
            Assert.Catch<SimSnapshotException>(() => M3TestWorld.Sim(World(NoPlates, units)).RestoreRetainedSnapshot(withPlates));
            Assert.Catch<SimSnapshotException>(() => M3TestWorld.Sim(World(HighOnly, units)).RestoreRetainedSnapshot(without));
        }

        // 5: commands carry a layer; the target's height is the plate's floored top.
        [Test]
        public void MoveCommandsCarryTheirLayer()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.MoveTo(1, 1, 0, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.MoveTo(1, 1, 0, 0, 4097));
            Assert.That(BridgeCommand.MoveTo(1, 1, 0, 0, 4096).Layer, Is.EqualTo(4096));
            Assert.That((BridgeCommand.MoveTo(1, 1, 0, 0).Layer, BridgeCommand.Hold(1, 1).Layer, BridgeCommand.Follow(1, 1, 2).Layer), Is.EqualTo((0, 0, 0)));

            SimSession s = World(Plates, Three, new[] { BridgeCommand.MoveTo(1, 9, 6500, 1500, 1), BridgeCommand.MoveTo(2, 9, 2500, 3500, 2), BridgeCommand.MoveTo(3, 9, 4500, 500) });
            s.StepOnce();
            SquadCommand c = Input(s).Squad.Value;
            Assert.That((c.TargetPoint.X.Raw, c.TargetPoint.Y.Raw, c.TargetPoint.Z.Raw, c.TargetLayer), Is.EqualTo((6500 * M / 1000, Top1, 1500 * M / 1000, 1)));
            s.StepOnce();
            c = Input(s).Squad.Value;
            Assert.That((c.TargetPoint.Y.Raw, c.TargetLayer), Is.EqualTo((Slope25, 2)));
            s.StepOnce();
            c = Input(s).Squad.Value;
            Assert.That((c.TargetPoint.Y.Raw, c.TargetLayer), Is.EqualTo((0L, 0)), "a ground command as before");
        }

        [Test]
        public void CommandsOffTheirLayerAreRefused()
        {
            SimSession s = World(Plates, Three), flat = World(NoPlates, new[] { Unit(1, 1, 500, 500) });
            string Message(SimSession w, BridgeCommand c) => Assert.Throws<ArgumentException>(() => w.Enqueue(c)).Message;
            StringAssert.StartsWith("Command target names layer 1 on a map without floor plates.", Message(flat, BridgeCommand.MoveTo(1, 1, 6500, 1500, 1)));
            StringAssert.StartsWith("Command target names layer 3 of a map with 2 plates.", Message(s, BridgeCommand.MoveTo(1, 1, 6500, 1500, 3)));
            StringAssert.StartsWith("Command target is not on layer 1.", Message(s, BridgeCommand.MoveTo(1, 1, 8500, 2500, 1)));
            StringAssert.StartsWith("Command target is not on layer 1.", Message(s, BridgeCommand.MoveTo(1, 1, 5999, 1500, 1)));
            Assert.DoesNotThrow(() => s.Enqueue(BridgeCommand.MoveTo(1, 1, 6000, 1000, 1)), "the minimum edges belong to the cell");
            Assert.DoesNotThrow(() => s.Enqueue(BridgeCommand.MoveTo(2, 1, 9500, 3500, 1)), "a solid cell a wall blocks is still on the layer");
            Assert.DoesNotThrow(() => flat.Enqueue(BridgeCommand.MoveTo(1, 1, 1500, 500)), "a ground command needs no plates");
        }

        // 6: an order onto a plate stops the unit and records the layer; a ground order clears it and the unit walks.
        [TestCase(MoverKind.InterimCrowd)]
        [TestCase(MoverKind.InterimPath)]
        public void AnOrderOntoAPlateStopsTheUnit(MoverKind mover)
        {
            var script = new[] { BridgeCommand.MoveTo(1, 1, 5500, 500), BridgeCommand.MoveTo(10, 1, 6500, 1500, 1), BridgeCommand.MoveTo(21, 1, 500, 4500) };
            SimSession s = SimSession.Create(Recipe(new[] { Unit(1, 1, 500, 500) }, script, mover), Plates);
            for (int i = 0; i < 9; i++) s.StepOnce();
            MoveIntent walking = M3TestWorld.Component<MoveIntent>(s, 1);
            Assert.That(walking.Active, Is.True, "walking before the plate order");
            s.StepOnce();
            SimVec3 stopped = M3TestWorld.Position(s, 1);
            MoveIntent intent = M3TestWorld.Component<MoveIntent>(s, 1);
            Assert.That((intent.Active, intent.Target, intent.SpeedPerTick, intent.Squad), Is.EqualTo((false, walking.Target, walking.SpeedPerTick, walking.Squad)), "as Stop leaves it");
            Assert.That(Vertical(s, 1).TargetLayer, Is.EqualTo((ushort)1));
            for (int i = 0; i < 10; i++) s.StepOnce();
            Assert.That(M3TestWorld.Position(s, 1), Is.EqualTo(stopped), "it does not walk onto the plate");
            s.StepOnce();
            Assert.That((Vertical(s, 1).Layer, Vertical(s, 1).TargetLayer), Is.EqualTo(((ushort)0, (ushort)0)));
            for (int i = 0; i < 10; i++) s.StepOnce();
            Assert.That(M3TestWorld.Position(s, 1), Is.Not.EqualTo(stopped), "a ground order walks again");
        }

        [Test]
        public void ATwoArgumentOrderIsOnTheGround()
        {
            SimSession s = World(Plates, Three);
            InterimLocomotion locomotion = M3TestWorld.Locomotion(s);
            EntityId unit = M3TestWorld.Unit(s, 1);
            locomotion.Order(unit, M3TestWorld.Point(6500, 1500), 1);
            Assert.That(Vertical(s, 1).TargetLayer, Is.EqualTo((ushort)1));
            locomotion.Order(unit, M3TestWorld.Point(4500, 500));
            Assert.That(Vertical(s, 1).TargetLayer, Is.EqualTo((ushort)0), "an AI or follow order clears the layer");
            ILocomotion contract = locomotion;
            contract.Order(unit, M3TestWorld.Point(6500, 1500), 1);
            Assert.That(Vertical(s, 1).TargetLayer, Is.EqualTo((ushort)1), "through the interface");
            Assert.Throws<ArgumentOutOfRangeException>(() => locomotion.Order(unit, M3TestWorld.Point(6500, 1500), 4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => locomotion.Order(unit, M3TestWorld.Point(6500, 1500), -1));
            SimSession flat = World(NoPlates, new[] { Unit(1, 1, 500, 500) });
            Assert.Throws<NotSupportedException>(() => M3TestWorld.Locomotion(flat).Order(M3TestWorld.Unit(flat, 1), M3TestWorld.Point(500, 500), 1));
            Assert.DoesNotThrow(() => M3TestWorld.Locomotion(flat).Order(M3TestWorld.Unit(flat, 1), M3TestWorld.Point(1500, 500), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => M3TestWorld.Locomotion(flat).Order(M3TestWorld.Unit(flat, 1), M3TestWorld.Point(500, 500), 4097),
                "the range comes before the world");
        }

        // A stable frame (Sim.StepStable) carries the command's layer through its binding to the order.
        [Test]
        public void AStableFrameCarriesTheLayer()
        {
            SimSession s = World(Plates, Three);
            SimVec3 point = new SimVec3(M3TestWorld.Point(6500, 1500).X, SimFix.FromRaw(Top1), M3TestWorld.Point(6500, 1500).Z);
            var frame = new InputFrame(default, default, squad: new SquadCommand(SquadCommandKind.MoveTo, 1, point, default, 1));
            M3TestWorld.Sim(s).StepStable(new StableInputFrame(frame));
            Assert.That(Vertical(s, 1).TargetLayer, Is.EqualTo((ushort)1));
        }

        // V-f 2: with ground orders only, a world with a high plate moves its units exactly as the same map without plates.
        [Test]
        public void GroundOrdersMoveAsWithoutPlates()
        {
            var units = new[] { Unit(1, 1, 500, 500), Unit(2, 2, 11500, 5500), Unit(3, 3, 3500, 1500) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 11500, 2500), BridgeCommand.MoveTo(2, 2, 500, 1500), BridgeCommand.MoveTo(40, 1, 7500, 5500) };
            SimSession a = World(HighOnly, units, script), b = World(NoPlates, units, script, preview: false);
            for (int tick = 1; tick <= 300; tick++)
            {
                a.StepOnce(); b.StepOnce();
                for (ulong key = 1; key <= 3; key++)
                    Assert.That(M3TestWorld.Position(a, key), Is.EqualTo(M3TestWorld.Position(b, key)), "unit " + key + " at tick " + tick);
            }
            Assert.That(M3TestWorld.Position(a, 1), Is.Not.EqualTo(M3TestWorld.Point(500, 500)), "the units did move");
        }

        // 7: spawn rules.
        [Test]
        public void SpawnsOffTheirLayerAreRefused()
        {
            string Message(byte[] map, UnitSpawn spawn) =>
                Assert.Throws<ArgumentException>(() => World(map, new[] { Unit(1, 1, 500, 500), spawn })).Message;
            StringAssert.StartsWith("Unit spawn 2 is not on a walkable cell of layer 1.", Message(Plates, Unit(2, 2, 8500, 2500, 1)), "cutout");
            StringAssert.StartsWith("Unit spawn 2 is not on a walkable cell of layer 1.", Message(Plates, Unit(2, 2, 9500, 3500, 1)), "wall");
            StringAssert.StartsWith("Unit spawn 2 is not on a walkable cell of layer 1.", Message(Plates, Unit(2, 2, 5500, 1500, 1)), "off the plate");
            StringAssert.StartsWith("Unit spawn 2 names layer 3 of a map with 2 plates.", Message(Plates, Unit(2, 2, 6500, 1500, 3)));
            StringAssert.StartsWith("Reserve unit 2 names layer 1; reserves deploy on the ground.", Message(Plates, Unit(2, 2, 6500, 1500, 1, reserve: true)));
            StringAssert.StartsWith("Unit spawn 2 names layer 1 on a map without floor plates.", Message(NoPlates, Unit(2, 2, 6500, 1500, 1)));
            StringAssert.StartsWith("Unit spawn 2 names layer 1 on a map without floor plates.",
                Assert.Throws<ArgumentException>(() => SimSession.Create(Recipe(new[] { Unit(2, 2, 6500, 1500, 1) }, mover: MoverKind.Interim))).Message, "no map");
            Assert.Throws<ArgumentOutOfRangeException>(() => Unit(2, 2, 0, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Unit(2, 2, 0, 0, 4097));
        }

        // 7: on a terrain map the ground under a low plate is blocked (ML-4 rule 1), yet a unit can stand on the plate there.
        [Test]
        public void APlateUnitSkipsTheGroundFootprint()
        {
            MapDesc d = Ml4Maps.Make(0, 12, 6, Ml4Maps.Ground(12, 6, 0), new[] { Ml4Maps.Flat(1, 2, 1, 3, 2, M) });
            byte[] map = Bytes(d);
            WorldRecipe Terrain(UnitSpawn spawn) => Recipe(new[] { Unit(1, 1, 10500, 4500), spawn }).WithTerrainRules(Wp23.Pack());
            SimSession s = null;
            Assert.DoesNotThrow(() => s = SimSession.Create(Terrain(Unit(2, 2, 3500, 1500, 1)), map));
            Assert.That((M3TestWorld.Position(s, 2).Y.Raw, Vertical(s, 2).Layer), Is.EqualTo((M, (ushort)1)));
            StringAssert.StartsWith("Unit spawn 2 is not on standable ground in a terrain world.",
                Assert.Throws<ArgumentException>(() => SimSession.Create(Terrain(Unit(2, 2, 3500, 1500)), map)).Message);
        }

        // 3 and 8: reserves have a vertical state; deployment is on the ground and resets it.
        [Test]
        public void DeploymentIsOnTheGround()
        {
            var units = RespawnWorld.Units(RespawnWorld.U(20, 30, 10, reserve: true));
            MatchPack pack = RespawnWorld.Pack(RespawnWorld.Points(RespawnWorld.Spawn(1, 24, 20)), RespawnWorld.Bases(new SpawnAnchor(1, RespawnWorld.P(5, 38))), 300);
            var w = new M6World(RespawnWorld.Recipe(units, pack, player: false).WithPlatesPreview(), Arena());
            Assert.That(w.Has<VerticalState>(20), Is.True, "a reserve has one too");
            Assert.That(w.Read<VerticalState>(20).Layer, Is.EqualTo((ushort)0));
            var deployer = Field<IUnitDeployer>(w.Reinforce, "deployer");
            w.Set(RespawnWorld.Ally, new VerticalState(1, 1));
            Assert.That(deployer.TryDeploy(w.Unit(RespawnWorld.Ally), RespawnWorld.P(30, 10)), Is.True);
            VerticalState v = w.Read<VerticalState>(RespawnWorld.Ally);
            Assert.That((v.Layer, v.TargetLayer, w.Read<Position>(RespawnWorld.Ally).Value.Y.Raw), Is.EqualTo(((ushort)0, (ushort)0, 0L)));
        }

        // V-f 3: a world with units on plates resumes from a snapshot to the same hashes.
        [Test]
        public void APlateWorldResumesToTheSameHashes()
        {
            var script = new[] { BridgeCommand.MoveTo(1, 1, 11500, 500) };
            SimSession a = World(Plates, Three, script);
            for (int i = 0; i < 150; i++) a.StepOnce();
            byte[] snapshot = M3TestWorld.Sim(a).SaveRetainedSnapshot();
            var expected = new List<ulong>();
            for (int i = 0; i < 150; i++) { M3TestWorld.Sim(a).Step(default); expected.Add(M3TestWorld.Sim(a).StateHashRetained()); }

            SimSession b = World(Plates, Three, script);
            Simulation sim = M3TestWorld.Sim(b);
            sim.RestoreRetainedSnapshot(snapshot);
            Assert.That((Vertical(b, 2).Layer, Vertical(b, 3).Layer, M3TestWorld.Position(b, 2).Y.Raw), Is.EqualTo(((ushort)1, (ushort)2, Top1)));
            var resumed = new List<ulong>();
            for (int i = 0; i < 150; i++) { sim.Step(default); resumed.Add(sim.StateHashRetained()); }
            Assert.That(resumed, Is.EqualTo(expected));
            Assert.That(M3TestWorld.Position(a, 2).Y.Raw, Is.EqualTo(Top1));
        }

        // 5: the aim carries a layer (player worlds only).
        [Test]
        public void TheAimCarriesItsLayer()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInput(0, 0, aimLayer: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInput(0, 0, aimLayer: 4097));
            var old = new PlayerInput(10, -20, 3000, 4000, true, false, true, 2, 1500);
            Assert.That(old.GetHashCode(), Is.EqualTo(OldHash(old)), "layer 0 keeps the old hash code");
            Assert.That(new PlayerInput(10, -20, 3000, 4000, true, false, true, 2, 1500, 1), Is.Not.EqualTo(old));

            M6World w = PlayerArena(true);
            SimSession s = w.Session;
            string Message(PlayerInput input) => Assert.Throws<ArgumentException>(() => s.SetPlayerInput(s.Tick + 1, input)).Message;
            StringAssert.StartsWith("Aim point names layer 2 of a map with 1 plates.", Message(new PlayerInput(0, 0, 40500, 30500, aimLayer: 2)));
            StringAssert.StartsWith("Aim point is not on layer 1.", Message(new PlayerInput(0, 0, 42500, 31500, aimLayer: 1)));
            SimSession flat = PlayerArena(false).Session;
            StringAssert.StartsWith("Aim point names layer 1 on a map without floor plates.",
                Assert.Throws<ArgumentException>(() => flat.SetPlayerInput(1, new PlayerInput(0, 0, 40500, 30500, aimLayer: 1))).Message);

            (long y, int layer) Aim(PlayerInput input)
            {
                s.SetPlayerInput(s.Tick + 1, input);
                s.StepOnce();
                InputFrame frame = Input(s);
                return (frame.Aim.Y.Raw, frame.AimLayer);
            }
            Assert.That(Aim(new PlayerInput(0, 0, 40500, 30500, aimLayer: 1)), Is.EqualTo((Top1, 1)), "only the layer");
            Assert.That(Aim(new PlayerInput(0, 0, 40500, 30500, aimYMm: 1500)), Is.EqualTo((SimFix.Ratio(1500, 1000).Raw, 0)), "only the height");
            Assert.That(Aim(new PlayerInput(0, 0, 40500, 30500, aimYMm: 1500, aimLayer: 1)), Is.EqualTo((SimFix.Ratio(1500, 1000).Raw, 1)), "both: the height wins");
            Assert.That(Aim(new PlayerInput(0, 0, 40500, 30500)), Is.EqualTo((0L, 0)), "neither: the ground");
        }

        private static M6World PlayerArena(bool plates)
        {
            MatchPack pack = RespawnWorld.Pack(RespawnWorld.Points(RespawnWorld.Spawn(1, 24, 20)), RespawnWorld.Bases(new SpawnAnchor(1, RespawnWorld.P(5, 38))), 300);
            WorldRecipe recipe = RespawnWorld.Recipe(RespawnWorld.Units(), pack);
            return new M6World(plates ? recipe.WithPlatesPreview() : recipe, Arena(plates));
        }

        // The hash code PlayerInput had before the layer existed.
        private static int OldHash(PlayerInput p)
        {
            unchecked
            {
                int hash = p.MoveXMilli;
                hash = hash * 397 ^ p.MoveZMilli;
                hash = hash * 397 ^ p.AimXMm.GetHashCode();
                hash = hash * 397 ^ p.AimZMm.GetHashCode();
                hash = hash * 397 ^ p.Primary.GetHashCode();
                hash = hash * 397 ^ p.Block.GetHashCode();
                hash = hash * 397 ^ p.Throw.GetHashCode();
                hash = hash * 397 ^ p.SkillSlot;
                return hash * 397 ^ p.AimYMm.GetHashCode();
            }
        }
    }
}
