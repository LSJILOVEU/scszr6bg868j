using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-9b acceptance 6 (guard slots by plate) and 8 (slot-zone spacing by layer).
    public sealed class Ml9bSlotTests
    {
        private const long M = SimFix.Scale;

        // A floor on layers 1 and 2 over x, z in [8, 16) m without cell (12, 12); its top rises with x (y = x / 4), so a slot's
        // height is visibly the floor's and not the centre's.
        private sealed class Floor : ISlotFloor
        {
            internal int Calls;
            public bool TryFloor(int layer, SimFix x, SimFix z, out SimFix y)
            {
                Calls++;
                y = SimFix.FromRaw(x.Raw / 4);
                long cx = x.Raw >> 16, cz = z.Raw >> 16;
                return (layer == 1 || layer == 2) && cx >= 8 && cx < 16 && cz >= 8 && cz < 16 && !(cx == 12 && cz == 12);
            }
        }

        private static SimVec3 At(long xMm, long y, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), SimFix.FromRaw(y), SimFix.Ratio(zMm, 1000));
        private static ControlPointDef Point(int id, long xMm, long zMm, int layer, long y = 3 * M) =>
            new ControlPointDef(id, At(xMm, y, zMm), SimFix.FromInt(1), 0, false, default, 0, layer);

        // Radius 1 m, spacing 1 m: the lattice is the centre and the six slots of ring 1 (Build). Centred on the cutout cell
        // (12, 12) the centre goes and six stay; centred on (8.5, 12.5) the slot at x = 7.5 is off the floor and six stay.
        [TestCase(12500, 12500, 6, false)]
        [TestCase(8500, 12500, 6, true)]
        public void PlateSlotsStandOnTheFloor(long xMm, long zMm, int count, bool centre)
        {
            ControlPointDef[] points = { Point(1, 2500, 2500, 0, 0), Point(2, xMm, zMm, 1) };
            var slots = new GuardSlots(points, M, 0, new Floor());
            SimVec3[] lattice = GuardSlots.Build(points[1].Center, points[1].Radius, M);
            Assert.That(lattice.Length, Is.EqualTo(7));
            Assert.That((slots.PlateStart(1), slots.PlateCount(1), slots.PlateTotal, slots.PlateCount(0)), Is.EqualTo((0, count, count, 0)));
            var kept = new List<SimVec3>();
            for (int s = slots.PlateStart(1); s < slots.PlateStart(1) + slots.PlateCount(1); s++)
            {
                SimVec3 p = slots.PlateAt(s);
                Assert.That((slots.PlateLayerOf(s), slots.PlatePointOf(s), slots.PlateCanonical(s)), Is.EqualTo((1, 1, s)));
                Assert.That(p.Y.Raw, Is.EqualTo(p.X.Raw / 4), "the floor's height");
                kept.Add(new SimVec3(p.X, points[1].Center.Y, p.Z));
            }
            Assert.That(kept, Is.EqualTo(lattice.Where(l => new Floor().TryFloor(1, l.X, l.Z, out _)).ToArray()), "the lattice in order, filtered");
            Assert.That(kept.Contains(points[1].Center), Is.EqualTo(centre), "slot 0, the centre");
            Assert.That((slots.Total, slots.Count(0), slots.Count(1), slots.Start(1)), Is.EqualTo((7, 7, 0, 7)), "the ground ports: point 1 has no slots");
        }

        // The ports with the old signatures see only ground slots; the plate ports find plate slots on their own layer only.
        [Test]
        public void TheOldPortsSeeOnlyTheGround()
        {
            ControlPointDef[] points = { Point(1, 2500, 2500, 0, 0), Point(2, 9500, 9500, 1) };
            var slots = new GuardSlots(points, M, 0, new Floor());
            SimVec3 plate = slots.PlateAt(0);
            Assert.That(slots.TryFind(plate, -1, out int found) || slots.TryFind(plate, 1, out found), Is.False);
            Assert.That(slots.Nearest(plate), Is.EqualTo(-1));
            Assert.That(slots.TryFindOnPlate(1, plate, -1, out found) && found == 0, Is.True);
            Assert.That(slots.TryFindOnPlate(1, plate, 1, out found) && found == 0, Is.True);
            Assert.That(slots.TryFindOnPlate(1, plate, 0, out _) || slots.TryFindOnPlate(2, plate, -1, out _), Is.False);
            Assert.That(slots.NearestOnPlate(1, plate), Is.EqualTo(0));
            Assert.That(slots.NearestOnPlate(2, plate), Is.EqualTo(-1));
            Assert.That(slots.NearestOnPlate(0, slots.At(0)), Is.EqualTo(-1), "ground slots are not plate slots");
            Assert.That(slots.TryFind(slots.At(0), -1, out found) && found == 0 && slots.Nearest(slots.At(0)) == 0, Is.True);
        }

        // The same plane position on two layers stays two slots; on one layer it shares the lower point's slot.
        [Test]
        public void LayersKeepSlotsApart()
        {
            var apart = new GuardSlots(new[] { Point(1, 9500, 9500, 1), Point(2, 9500, 9500, 2) }, M, 0, new Floor());
            Assert.That(apart.PlateCount(0), Is.EqualTo(apart.PlateCount(1)));
            for (int k = 0; k < apart.PlateCount(1); k++)
            {
                int s = apart.PlateStart(1) + k;
                Assert.That((apart.PlateCanonical(s), apart.PlateLayerOf(s)), Is.EqualTo((s, 2)));
                Assert.That(apart.TryFindOnPlate(2, apart.PlateAt(s), -1, out int found) && found == s, Is.True);
            }
            // Neighbours in the sort order: point 1's last slot (layer 1) and point 2's first (layer 2) share the plane position
            // (10.5, 9.5) and stay apart.
            var touching = new GuardSlots(new[] { Point(1, 9500, 9500, 1), Point(2, 11500, 9500, 2) }, M, 0, new Floor());
            SimVec3 seam = At(10500, 0, 9500);
            Assert.That(touching.TryFindOnPlate(1, seam, 0, out int one), Is.True);
            Assert.That(touching.TryFindOnPlate(2, seam, 1, out int two), Is.True);
            Assert.That((touching.PlateCanonical(one), touching.PlateCanonical(two)), Is.EqualTo((one, two)));
            Assert.That(touching.TryFindOnPlate(2, seam, -1, out int found2) && found2 == two, Is.True);
            var shared = new GuardSlots(new[] { Point(1, 9500, 9500, 1), Point(2, 9500, 9500, 1) }, M, 0, new Floor());
            for (int k = 0; k < shared.PlateCount(1); k++)
                Assert.That(shared.PlateCanonical(shared.PlateStart(1) + k), Is.EqualTo(k));
        }

        [Test]
        public void APlatePointNeedsTheFloor()
        {
            Assert.That(Assert.Throws<ArgumentException>(() => new GuardSlots(new[] { Point(1, 9500, 9500, 1) })).Message,
                Does.StartWith("Guard slots of a point on a floor plate need the plates' floor."));
            Assert.DoesNotThrow(() => new GuardSlots(new[] { Point(1, 9500, 9500, 0) }));
        }

        // With ground points only the table is the one built without the floor, slot by slot, and the floor is never asked.
        [Test]
        public void GroundPointsKeepTheirTable()
        {
            ControlPointDef[] points = { Point(1, 2500, 2500, 0, 0), Point(2, 4500, 2500, 0, 0), Point(3, 20000, 20000, 0, 0) };
            var floor = new Floor();
            var with = new GuardSlots(points, M, M / 2, floor);
            var without = new GuardSlots(points, M, M / 2);
            Assert.That(floor.Calls, Is.EqualTo(0));
            Assert.That((with.Total, with.PlateTotal), Is.EqualTo((without.Total, 0)));
            for (int s = 0; s < with.Total; s++)
                Assert.That((with.At(s), with.PointOf(s), with.Canonical(s), with.Nearest(with.At(s))),
                    Is.EqualTo((without.At(s), without.PointOf(s), without.Canonical(s), without.Nearest(without.At(s)))));
            int start = 0;
            for (int p = 0; p < points.Length; p++)
            {
                SimVec3[] lattice = GuardSlots.Build(points[p].Center, points[p].Radius + SimFix.FromRaw(M / 2), M);
                Assert.That((with.Start(p), with.Count(p)), Is.EqualTo((start, lattice.Length)));
                for (int k = 0; k < lattice.Length; k++) Assert.That(with.At(start + k), Is.EqualTo(lattice[k]));
                start += lattice.Length;
            }
            Assert.That(Enumerable.Range(0, with.Total).Count(s => with.Canonical(s) != s), Is.GreaterThan(0), "points 1 and 2 share slots");
        }

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["p1-test"] = "{ \"id\": \"p1-test\", \"extends\": \"rwr\", \"ai\": " + ai + " }"
        }, FileContentSource.Rwr()), "p1-test");

        // 8: radius 6, ring 6, spacing 2 needs centres 26 m apart (AiP1Tests.Content_TouchingSlotZonesAreAnError). One point
        // upstairs and one on the ground at the same plane position are not compared; two on the same layer are.
        [Test]
        public void SlotZonesOnOtherLayersAreNotCompared()
        {
            AiRules rules = Pack("{ \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000 }").Section<AiRules>();
            ControlPointDef Zone(int id, int layer) => new ControlPointDef(id, SimVec3.Metres(50, 3 * layer, 20), SimFix.FromInt(6), 0, false, default, 0, layer);
            Assert.That(() => ContentPolicies.Parts(rules, new[] { Zone(4, 1), Zone(9, 0) }), Throws.Nothing);
            Assert.That(() => ContentPolicies.Parts(rules, new[] { Zone(4, 2), Zone(9, 1) }), Throws.Nothing);
            foreach (int layer in new[] { 0, 1 })
            {
                var e = Assert.Throws<ContentException>(() => ContentPolicies.Parts(rules, new[] { Zone(4, layer), Zone(9, layer) }));
                Assert.That(e.Message, Is.EqualTo("content pack 'p1-test' layer 'p1-test' ai field 'slotRingMm': slot zones of points 4 and 9 are less than one slot spacing apart"));
            }
        }
    }
}
