using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;

namespace WarCore.Tests
{
    // ML-11a acceptance 3 (one slot numbering over the ground and the plates; the move ports' defaults) and 9 (a slot the AI
    // may choose is one an order keeps as it is).
    public sealed class Ml11aSlotTests
    {
        private static SimVec3 At(long xMm, long y, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), SimFix.FromRaw(y), SimFix.Ratio(zMm, 1000));

        // A floor on layers 1 and 2, 3 m up, over x, z in [8, 16) m.
        private sealed class Floor : ISlotFloor
        {
            public bool TryFloor(int layer, SimFix x, SimFix z, out SimFix y)
            {
                y = SimFix.FromRaw(3 * M);
                long cx = x.Raw >> 16, cz = z.Raw >> 16;
                return (layer == 1 || layer == 2) && cx >= 8 && cx < 16 && cz >= 8 && cz < 16;
            }
        }

        // 3. Points on the ground only: every port over both tables is the old port.
        [Test]
        public void GroundSlotsKeepTheirNumbers()
        {
            ControlPointDef[] points = RespawnWorld.Points(new ControlPointDef(1, At(20500, 0, 20500), SimFix.FromInt(2)));
            var slots = new GuardSlots(points);
            Assert.That((slots.AllTotal, slots.PlateTotal), Is.EqualTo((slots.Total, 0)));
            Assert.That(slots.Total, Is.GreaterThan(20));
            for (int p = 0; p < points.Length; p++)
                Assert.That((slots.AllStart(p), slots.AllCount(p)), Is.EqualTo((slots.Start(p), slots.Count(p))), "point " + p);
            for (int s = 0; s < slots.Total; s++)
            {
                Assert.That((slots.AllAt(s), slots.AllLayerOf(s), slots.AllCanonical(s)), Is.EqualTo((slots.At(s), 0, slots.Canonical(s))), "slot " + s);
                Assert.That(slots.TryFindAll(0, slots.At(s), -1, out int found) && found == slots.Canonical(s), Is.True, "slot " + s);
                Assert.That(slots.NearestAll(0, slots.At(s)), Is.EqualTo(slots.Nearest(slots.At(s))), "slot " + s);
                Assert.That(slots.TryFindAll(1, slots.At(s), -1, out _), Is.False, "slot " + s);
                Assert.That(slots.NearestAll(1, slots.At(s)), Is.EqualTo(-1), "slot " + s);
            }
            Assert.Throws<IndexOutOfRangeException>(() => slots.AllAt(slots.AllTotal));
            Assert.Throws<IndexOutOfRangeException>(() => slots.AllAt(-1));
        }

        // 3. A point on the ground (7 slots), one on layer 1 and one on layer 2 at the same place (7 each): the plate slots
        // follow the ground slots, [7, 21), each as its plate port says plus the ground total.
        [Test]
        public void PlateSlotsFollowTheGroundSlots()
        {
            ControlPointDef[] points =
            {
                new ControlPointDef(1, At(12500, 0, 12500), SimFix.FromInt(1)),
                new ControlPointDef(2, At(12500, 3 * M, 12500), SimFix.FromInt(1), 0, false, default, 0, 1),
                new ControlPointDef(3, At(12500, 3 * M, 12500), SimFix.FromInt(1), 0, false, default, 0, 2),
            };
            var slots = new GuardSlots(points, M, 0, new Floor());
            Assert.That((slots.Total, slots.PlateTotal, slots.AllTotal), Is.EqualTo((7, 14, 21)));
            Assert.That((slots.AllStart(0), slots.AllCount(0)), Is.EqualTo((0, 7)));
            Assert.That((slots.AllStart(1), slots.AllCount(1)), Is.EqualTo((7, 7)));
            Assert.That((slots.AllStart(2), slots.AllCount(2)), Is.EqualTo((14, 7)));
            foreach (int s in new[] { 0, 6 })
                Assert.That((slots.AllAt(s), slots.AllLayerOf(s), slots.AllCanonical(s)), Is.EqualTo((slots.At(s), 0, s)), "ground slot " + s);
            foreach (int s in new[] { 7, 8, 13, 14, 20 })
            {
                int plate = s - 7;
                Assert.That((slots.AllAt(s), slots.AllLayerOf(s), slots.AllCanonical(s)),
                    Is.EqualTo((slots.PlateAt(plate), slots.PlateLayerOf(plate), 7 + slots.PlateCanonical(plate))), "slot " + s);
                Assert.That(slots.AllLayerOf(s), Is.EqualTo(s < 14 ? 1 : 2), "slot " + s);
                Assert.That(slots.AllCanonical(s), Is.EqualTo(s), "slot " + s);
                Assert.That(slots.AllAt(s).Y.Raw, Is.EqualTo(3 * M), "slot " + s);
            }
            Assert.Throws<IndexOutOfRangeException>(() => slots.AllAt(21));
            Assert.Throws<IndexOutOfRangeException>(() => slots.AllLayerOf(21));
            Assert.Throws<IndexOutOfRangeException>(() => slots.AllCanonical(21));

            // The three centres share x and z: each is found, and is nearest, on its own layer only.
            SimVec3 up = At(12500, 3 * M, 12500), down = At(12500, 0, 12500);
            Assert.That(slots.TryFindAll(0, down, -1, out int found) && found == 0, Is.True);
            Assert.That(slots.TryFindAll(1, up, -1, out found) && found == 7, Is.True);
            Assert.That(slots.TryFindAll(2, up, -1, out found) && found == 14, Is.True);
            Assert.That(slots.TryFindAll(1, up, 1, out found) && found == 7, Is.True);
            Assert.That(slots.TryFindAll(1, up, 2, out _) || slots.TryFindAll(1, up, 0, out _) || slots.TryFindAll(3, up, -1, out _), Is.False);
            // A slot is told by its layer, x and z (an order's height is the mover's business).
            Assert.That(slots.TryFindAll(0, up, -1, out found) && found == 0, Is.True);
            Assert.That(slots.TryFindAll(1, down, -1, out found) && found == 7, Is.True);
            Assert.That((slots.NearestAll(0, down), slots.NearestAll(1, up), slots.NearestAll(2, up), slots.NearestAll(3, up)), Is.EqualTo((0, 7, 14, -1)));
            Assert.That(slots.NearestAll(1, At(30500, 3 * M, 30500)), Is.EqualTo(-1));
        }

        // The move ports without plates: the layer ports fall to the old ones on layer 0 and refuse every other layer.
        private sealed class Plain : IMoveOrders
        {
            internal int Orders;
            internal bool Stands = true;
            public bool CanStandAt(SimVec3 at) => Stands;
            public void Order(StableId unit, SimVec3 target) => Orders++;
            public void Stop(StableId unit) { }
            public bool TryGetOrder(StableId unit, out SimVec3 target) { target = At(1000, 0, 2000); return true; }
        }

        [Test]
        public void TheLayerPortsDefaultToTheGround()
        {
            var plain = new Plain();
            IMoveOrders moves = plain;
            moves.Order(default, default, 0);
            Assert.That(plain.Orders, Is.EqualTo(1));
            Assert.That(Assert.Throws<NotSupportedException>(() => moves.Order(default, default, 1)).Message, Is.EqualTo("Layer orders need a world with floor plates."));
            Assert.That(plain.Orders, Is.EqualTo(1));
            Assert.That(moves.TryGetOrder(default, out SimVec3 target, out int layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((At(1000, 0, 2000), 0)));
            Assert.That((moves.CanStandAt(default, 0), moves.CanStandAt(default, 1)), Is.EqualTo((true, false)));
            plain.Stands = false;
            Assert.That((moves.CanStandAt(default, 0), moves.CanStandAt(default, 1)), Is.EqualTo((false, false)));
        }

        // 9. On the house, with a point that covers the whole floor: a plate slot can be stood at exactly when an order to it
        // keeps the slot as its target. Slots too near the floor's open edges or the cutout cannot, the others can.
        [Test]
        public void ASlotThatCanBeStoodAtIsKeptByAnOrder()
        {
            M6World w = World(new[] { Walker(1, 41500, 32500, 1) }, new[] { PointUp(1, 41500, 32500, 3) }, think: 1000000);
            GuardSlots slots = Slots(w);
            IMoveOrders moves = Moves(w);
            int stand = 0, refuse = 0;
            for (int s = slots.AllStart(0); s < slots.AllStart(0) + slots.AllCount(0); s++)
            {
                SimVec3 at = slots.AllAt(s);
                bool can = moves.CanStandAt(at, slots.AllLayerOf(s));
                moves.Order(Id(w, 1), at, 1);
                bool kept = moves.TryGetOrder(Id(w, 1), out SimVec3 target, out int layer) && layer == 1 && target.Equals(at);
                moves.Stop(Id(w, 1));
                Assert.That(kept, Is.EqualTo(can), "slot " + s);
                if (can) stand++; else refuse++;
            }
            Assert.That((stand, refuse), Is.EqualTo((12, 3)));
            // The bridge's ports: the ground port on layer 0, nothing on a layer the map does not have or off the plate.
            Assert.That(moves.CanStandAt(Flat(41500, 32500), 0), Is.True);
            Assert.That(moves.CanStandAt(Flat(37500, 31500), 0), Is.False, "the blocked ground under the stair");
            Assert.That(moves.CanStandAt(Up(42500, 31500), 1), Is.False, "the cutout");
            Assert.That(moves.CanStandAt(Up(41500, 32500), 2), Is.False, "the stair's layer has no cell there");
            Assert.That(moves.CanStandAt(Up(41500, 32500), 9), Is.False);
            Assert.That(moves.CanStandAt(Up(41500, 32500), 1), Is.True);
            Assert.That(moves.CanStandAt(Up(41800, 31500), 1), Is.False, "0.2 m from the cutout");
            Assert.That(moves.CanStandAt(Up(41700, 31500), 1), Is.True, "0.3 m from it");
            Assert.Throws<NotSupportedException>(() => M3TestWorld.Locomotion(w.Session).CanStandOn(0, Flat(41500, 32500)));
        }

        // The bridge reads an order's layer from the unit: the ground port orders to the ground, the layer port to its layer.
        [Test]
        public void TheBridgeCarriesTheOrdersLayer()
        {
            M6World w = World(new[] { Walker(1, 41500, 32500, 1) }, new[] { PointUp(1, 41500, 32500) }, think: 1000000);
            IMoveOrders moves = Moves(w);
            Assert.That(moves.TryGetOrder(Id(w, 1), out _, out int layer), Is.False);
            Assert.That(layer, Is.EqualTo(0));
            moves.Order(Id(w, 1), Up(40500, 33500), 1);
            Assert.That(moves.TryGetOrder(Id(w, 1), out SimVec3 target, out layer), Is.True);
            Assert.That((target, layer), Is.EqualTo((Up(40500, 33500), 1)));
            moves.Order(Id(w, 1), Flat(30500, 31500), 0);
            Assert.That(moves.TryGetOrder(Id(w, 1), out target, out layer), Is.True);
            Assert.That((target.X, target.Z, layer), Is.EqualTo((Flat(30500, 31500).X, Flat(30500, 31500).Z, 0)));
        }
    }
}
