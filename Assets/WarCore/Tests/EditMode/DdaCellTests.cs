using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class DdaCellTests
    {
        [Test]
        public void ClosedBoxOracleIncludesOriginCornerMixedTieAndPersistentEdge()
        {
            var corner = RayCellOracle.Enumerate(0, 0, 0, 1, 1, -1, 7, 4);
            Assert.That(corner.Count, Is.EqualTo(15));
            Assert.That((corner[0].X, corner[0].Y, corner[0].Z), Is.EqualTo((-1L, -1L, -1L)));
            Assert.That((corner[7].X, corner[7].Y, corner[7].Z), Is.EqualTo((0L, 0L, 0L)));
            Assert.That((corner[8].X, corner[8].Y, corner[8].Z), Is.EqualTo((0L, 0L, -2L)));
            for (int i = 0; i < corner.Count; i++)
                Assert.That(corner[i].Numerator, Is.EqualTo(corner[i].Denominator * (i < 8 ? 0 : 4)));
            var edge = RayCellOracle.Enumerate(1, 0, 0, 1, 0, 0, 7, 4);
            Assert.That(edge.Count, Is.EqualTo(12));
            for (int i = 0; i < edge.Count; i++)
            {
                Assert.That(edge[i].X, Is.EqualTo(i / 4));
                Assert.That(edge[i].Numerator, Is.EqualTo(edge[i].Denominator * (i < 4 ? 0 : i < 8 ? 3 : 7)));
            }
        }

        [Test]
        public void RayCandidatesExcludeBodiesInOffLineCubeCells()
        {
            var store = new EntityStore(2);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromInt(4), 2, 64);
            for (int i = 0; i < 2; i++)
            {
                EntityId id = store.Create();
                store.Set(positions, id, new Position(SimVec3.Metres(6, i == 0 ? 1 : 9, 1)));
                store.Set(colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(1)));
                grid.Upsert(id);
            }
            var output = new RayHit[2];
            Assert.That(grid.Ray(SimVec3.Metres(1, 1, 1), SimVec3.Metres(1, 0, 0), SimFix.FromInt(16), output), Is.EqualTo(1));
            Assert.That(output[0].Entity.Id, Is.Zero);
            Assert.That(output[0].Distance.Raw, Is.EqualTo(19 * SimFix.Scale / 4));
            Assert.That(grid.LastCandidateCount, Is.EqualTo(1));
        }

        [Test]
        public void CompleteEventSequencesMatchIndependentClosedBoxOracle()
        {
            const ulong seed = 0xD1CE1101;
            var random = new A1MathInputs(seed);
            long[] sizes = { 1, 2, 3, 3 * SimFix.Scale, 8 * SimFix.Scale, 1L << 29, 1L << 30, long.MaxValue };
            int events = 0;
            for (int i = 0; i < 6000; i++)
            {
                long size = sizes[i % sizes.Length];
                long span = Math.Min(1L << 30, size > (1L << 27) ? 1L << 30 : size * 8);
                long ox = (long)(random.Next() % (ulong)(span * 2 + 1)) - span;
                long oy = (long)(random.Next() % (ulong)(span * 2 + 1)) - span;
                long oz = (long)(random.Next() % (ulong)(span * 2 + 1)) - span;
                if (i % 4 != 0)
                    ox = 0;
                if (i % 4 >= 2)
                    oy = 0;
                if (i % 4 == 3)
                    oz = 0;
                long dx = (long)(random.Next() % 15) - 7;
                long dy = i % 5 == 0 ? 0 : (long)(random.Next() % 15) - 7;
                long dz = i % 5 == 0 ? 0 : i % 5 == 1 ? -dx : (long)(random.Next() % 15) - 7;
                if ((dx | dy | dz) == 0)
                    dx = 1;
                if (i % 17 == 0)
                {
                    dx = 1L << 30;
                    dy = -(1L << 30);
                    dz = (1L << 30) - 1;
                }
                long limit = size > (1L << 28) ? 1L << 30 : size * 4;
                long range = i % 11 == 0 ? 0 : (long)(random.Next() % (ulong)(limit + 1));
                events += CompareSequence(ox, oy, oz, dx, dy, dz, range, size);
            }
            events += CompareSequence(0, 0, 0, 1, 1, -1, 7, 4);
            events += CompareSequence(1, 0, 0, 1, 0, 0, 7, 4);
            events += CompareSequence(-(1L << 30), 1L << 30, 0, 1L << 30, 0, 0, 1L << 30, 1L << 27);
            events += CompareSequence(1L << 30, -(1L << 30), 0, -(1L << 30), 0, 0, 1L << 30, long.MaxValue);
            TestContext.WriteLine("D1 event oracle: seed=0x{0:X}; rays=6004; cell events={1}; full ordered coordinates and exact rational entry times agree; sizes=1,2,3 raw,3m,8m,8192m,16384m,long.MaxValue raw.", seed, events);
        }

        private static int CompareSequence(long ox, long oy, long oz, long dx, long dy, long dz, long range, long size)
        {
            var expected = RayCellOracle.Enumerate(ox, oy, oz, dx, dy, dz, range, size);
            var request = new SpatialRequest(SpatialQueryKind.Ray, A2SpatialInputs.V(ox, oy, oz), default,
                A2SpatialInputs.V(dx, dy, dz), SimFix.FromRaw(range), default, 0);
            var traversal = new RayCellTraversal(in request, size);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(traversal.MoveNext(out RayCellEvent actual), Is.True, "Missing cell at index " + i);
                RayCellOracle.Entry reference = expected[i];
                Assert.That((actual.X, actual.Y, actual.Z), Is.EqualTo((reference.X, reference.Y, reference.Z)), "Cell index " + i);
                Assert.That((BigInteger)actual.Numerator * reference.Denominator,
                    Is.EqualTo(reference.Numerator * actual.Denominator), "Entry time at index " + i);
            }
            Assert.That(traversal.MoveNext(out _), Is.False, "Extra cell event.");
            Assert.That(traversal.MoveNext(out _), Is.False, "Traversal must stay finished.");
            return expected.Count;
        }

        [Test]
        public void InvalidCellSizesFailBeforeIntegerDivision()
        {
            var request = new SpatialRequest(SpatialQueryKind.Ray, default, default, SimVec3.Metres(1, 0, 0), default, default, 0);
            Assert.Throws<ArgumentException>(() => new RayCellTraversal(in request, 0));
            Assert.Throws<ArgumentException>(() => new RayCellTraversal(in request, -1));
        }

        [Test]
        public void DdaIsAllocationFreeAfterWarmupAndWidthAssumptionsAreFixed()
        {
            var windows = new AllocationWindow();
            do
            {
                Assert.That(SimVec3.WorldLimitMetres * SimFix.Scale, Is.EqualTo(1L << 30));
                var request = new SpatialRequest(SpatialQueryKind.Ray, default, default, SimVec3.Metres(-1, 1, 0), SimFix.FromInt(48), default, 0);
                for (int i = 0; i < 16; i++)
                    Count(in request);
                var meter = new AllocationMeter();
                windows.Begin(meter);
                int count = 0;
                for (int i = 0; i < 1000; i++)
                    count += Count(in request);
                AllocationSample allocated = windows.End();
                Assert.That(count, Is.GreaterThan(0));
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                TestContext.WriteLine("D1 traversal warmup allocation: 1000 rays, {0} cell events; {1}.", count, allocated.Describe());
            }
            while (windows.NeedsRetry);
        }

        private static int Count(in SpatialRequest request)
        {
            var traversal = new RayCellTraversal(in request, 3 * SimFix.Scale);
            int count = 0;
            while (traversal.MoveNext(out _))
                count++;
            return count;
        }
    }
}
