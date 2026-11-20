using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;
using static WarCore.Tests.Ml7Worlds;
using static WarCore.Tests.Ml8Worlds;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-8 acceptance 5 (climbing), 8 (snapshots), 9 (invariant M-i 3) and 11a (the demo map) on the demo house.
    public sealed class Ml8DemoTests
    {
        // 11a: the description bakes and passes the gate with no problem; the seam table is exactly the stair's two ends.
        [Test]
        public void TheDemoHouseHasTwoSeams()
        {
            Assert.That(MapGate.Check(PlateDemoMap.DescBytes(), RwrPack.Load("rwr-m6")), Is.Empty);
            WcMap map = WcMapReader.Read(Demo);
            LayeredNav nav = WcMapLayeredNav.Build(map, null, map.ContentId);
            Assert.That((nav.GroundCount, nav.LayerCount, nav.Count), Is.EqualTo((384, 3, 408)));
            for (int x = 10; x <= 13; x++) Assert.That(Node(nav, 1, x, 9), Is.EqualTo(384 + x - 10), "stair");
            for (int z = 6; z <= 8; z++)
                for (int x = 9; x <= 14; x++) Assert.That(Node(nav, 2, x, z), Is.EqualTo(388 + (z - 6) * 6 + x - 9), "first floor");
            Assert.That((Node(nav, 2, 9, 9), Node(nav, 2, 14, 9)), Is.EqualTo((406, 407)));
            Assert.That(nav.IsPassable(225), Is.True, "the cell before the stair's foot is walkable ground");
            Assert.That(AllSeams(nav), Is.EqualTo(new[] { (225, 384), (384, 225), (387, 407), (407, 387) }),
                "the stair's foot <-> the ground in front of it, the stair's top <-> the first floor; none along the stair's sides");
        }

        // 5: a ground unit ordered onto the first floor climbs 0 -> stair -> floor without skipping a layer, never stops or
        // fails on the way, and arrives exactly at the target on the floor's top; a two-argument order (as the AI gives) brings
        // it back down the same way.
        [TestCase(MoverKind.InterimCrowd)]
        [TestCase(MoverKind.InterimPath)]
        public void AUnitClimbsToTheFirstFloorAndBack(MoverKind mover)
        {
            SimSession s = Walk(Demo, new[] { Unit(1, 1, 3500, 7500) }, new[] { BridgeCommand.MoveTo(1, 1, 12500, 7500, 2) }, mover);
            InterimLocomotion l = M3TestWorld.Locomotion(s);
            EntityId unit = M3TestWorld.Unit(s);
            var layers = new List<int> { 0 };
            var waypoints = new List<SimVec3>();
            s.StepOnce();
            for (int t = 2; t <= 300 && !l.IsArrived(unit); t++)
            {
                Assert.That(l.HasFailed(unit), Is.False, "tick " + t);
                Assert.That(M3TestWorld.Component<MoveIntent>(s).Active, Is.True, "no stop on the way, tick " + t);
                SimVec3 w = M3TestWorld.Component<MoveWaypoint>(s).Point;
                if (mover == MoverKind.InterimPath && (waypoints.Count == 0 || !waypoints[waypoints.Count - 1].Equals(w))) waypoints.Add(w);
                s.StepOnce();
                if (layers[layers.Count - 1] != Vertical(s, 1).Layer) layers.Add(Vertical(s, 1).Layer);
            }
            Assert.That(l.IsArrived(unit), Is.True);
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(P(12500, 3 * M, 7500)));
            Assert.That(layers, Is.EqualTo(new[] { 0, 1, 2 }));
            if (mover == MoverKind.InterimPath)
            {
                // A run of same-direction steps ends where the layer changes: the ground in front of the stair, the stair's first
                // cell, its top, the floor cell it faces are all waypoints.
                var expected = new[] { P(9500, 0, 9500), P(10500, StairTop(10 * M + M / 2), 9500), P(13500, StairTop(13 * M + M / 2), 9500), P(14500, 3 * M, 9500) };
                int at = 0;
                foreach (SimVec3 w in waypoints) if (at < expected.Length && w.Equals(expected[at])) at++;
                Assert.That(at, Is.EqualTo(expected.Length), "waypoints: " + string.Join(" ", waypoints));
            }

            l.Order(unit, M3TestWorld.Point(3500, 7500));
            layers = new List<int> { 2 };
            for (int t = 1; t <= 300 && !l.IsArrived(unit); t++)
            {
                Assert.That(l.HasFailed(unit), Is.False, "down, tick " + t);
                s.StepOnce();
                if (layers[layers.Count - 1] != Vertical(s, 1).Layer) layers.Add(Vertical(s, 1).Layer);
            }
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(P(3500, 0, 7500)));
            Assert.That(layers, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        // The demo run: four soldiers from the open ground up to the first floor, then back. M-i 3: every tick every unit
        // stands on its layer's floor.
        internal static readonly UnitSpawn[] Squad = { Unit(1, 1, 1500, 2000), Unit(2, 1, 3000, 2000), Unit(3, 1, 1500, 3500), Unit(4, 1, 3000, 3500) };
        internal static readonly BridgeCommand[] Script = { BridgeCommand.MoveTo(100, 1, 12000, 7500, 2), BridgeCommand.MoveTo(400, 1, 3000, 3000) };

        [Test]
        public void EveryUnitStandsOnItsFloor()
        {
            SimSession s = Walk(Demo, Squad, Script);
            WcMap map = WcMapReader.Read(Demo);
            var visited = new HashSet<int>();
            for (int t = 1; t <= 600; t++)
            {
                s.StepOnce();
                for (ulong k = 1; k <= 4; k++)
                {
                    SimVec3 p = M3TestWorld.Position(s, k);
                    int layer = Vertical(s, k).Layer;
                    visited.Add(layer);
                    Assert.That(p.Y.Raw, Is.EqualTo(Floor(map, layer, p)), "unit " + k + " tick " + t);
                }
                if (t == 380)
                    for (ulong k = 1; k <= 4; k++) Assert.That(Vertical(s, k).Layer, Is.EqualTo((ushort)2), "all up, unit " + k);
            }
            Assert.That(visited, Is.EquivalentTo(new[] { 0, 1, 2 }));
            for (ulong k = 1; k <= 4; k++) Assert.That((Vertical(s, k).Layer, M3TestWorld.Position(s, k).Y.Raw), Is.EqualTo(((ushort)0, 0L)), "all down, unit " + k);
        }

        // M-i 4: a snapshot taken mid-climb (tick 150) resumes to the same hashes up to tick 300.
        [Test]
        public void AClimbResumesToTheSameHashes()
        {
            SimSession a = Walk(Demo, Squad, Script);
            for (int i = 0; i < 150; i++) a.StepOnce();
            Assert.That(Enumerable.Range(1, 4).Any(k => Vertical(a, (ulong)k).Layer == 1), Is.True, "someone is on the stair at tick 150");
            byte[] snapshot = M3TestWorld.Sim(a).SaveRetainedSnapshot();
            var expected = new List<ulong>();
            for (int i = 0; i < 150; i++) { a.StepOnce(); expected.Add(M3TestWorld.Sim(a).StateHashRetained()); }

            SimSession b = Walk(Demo, Squad, Script);
            Simulation sim = M3TestWorld.Sim(b);
            sim.RestoreRetainedSnapshot(snapshot);
            var resumed = new List<ulong>();
            for (int i = 0; i < 150; i++) { b.StepOnce(); resumed.Add(sim.StateHashRetained()); }
            Assert.That(resumed, Is.EqualTo(expected));
        }

        // 8: a snapshot of the demo house does not restore under other plates (the first floor 0.25 m higher).
        [Test]
        public void ASnapshotRefusesOtherPlates()
        {
            MapDesc d = PlateDemoMap.Desc();
            var other = new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null,
                new[] { d.Plates[0], new MapPlate(2, new MapXZ(9, 6), new MapXZ(6, 4), 3 * M + M / 4, null, M / 2) }, d.Cutouts.ToArray(),
                d.Walls.ToArray(), d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(), d.Run, d.Owners.ToArray(), null, null,
                d.Stamps.ToArray(), d.Removed.ToArray(), d.Next);
            byte[] snapshot = M3TestWorld.Sim(Walk(Demo, Squad, Script)).SaveRetainedSnapshot();
            Assert.Catch<SimSnapshotException>(() => M3TestWorld.Sim(Walk(Bytes(other), Squad, Script)).RestoreRetainedSnapshot(snapshot));
        }

        // A squad's slots round a plate target can fall off the floor or hang over its edge: each slot moves to where a unit
        // can stand on that layer (its cell's centre, or the nearest walkable cell's), so the whole squad arrives up there.
        [Test]
        public void SquadSlotsStandOnTheFloor()
        {
            SimSession s = Walk(Demo, Squad, new[] { BridgeCommand.MoveTo(1, 1, 12000, 6200, 2) });
            InterimLocomotion l = M3TestWorld.Locomotion(s);
            for (int t = 0; t < 400; t++) s.StepOnce();
            for (ulong k = 1; k <= 4; k++)
            {
                EntityId unit = M3TestWorld.Unit(s, k);
                Assert.That((l.IsArrived(unit), l.HasFailed(unit), Vertical(s, k).Layer), Is.EqualTo((true, false, (ushort)2)), "unit " + k);
            }
            var targets = Enumerable.Range(1, 4).Select(k => M3TestWorld.Component<MoveIntent>(s, (ulong)k).Target).OrderBy(p => p.X.Raw).ThenBy(p => p.Z.Raw).ToArray();
            Assert.That(targets, Is.EqualTo(new[] { P(10500, 3 * M, 6500), P(11000, 3 * M, 7200), P(12500, 3 * M, 6500), P(13000, 3 * M, 7200) }),
                "slots (11, 5.2) and (13, 5.2) move to the first walkable cell of their ring, (10, 6) and (12, 6); slots on z 7.2 stay: "
                + string.Join(" ", targets.Select(Show)));

            // A point on a walkable cell whose footprint would hang over the floor's edge moves to the cell's centre.
            l.Order(M3TestWorld.Unit(s, 1), M3TestWorld.Point(11000, 6100), 2);
            Assert.That(Show(M3TestWorld.Component<MoveIntent>(s, 1).Target), Is.EqualTo(Show(P(11500, 3 * M, 6500))));
            l.Order(M3TestWorld.Unit(s, 1), M3TestWorld.Point(11000, 6250), 2);
            Assert.That(Show(M3TestWorld.Component<MoveIntent>(s, 1).Target), Is.EqualTo(Show(P(11000, 3 * M, 6250))), "touching the edge stays");
        }
    }
}
