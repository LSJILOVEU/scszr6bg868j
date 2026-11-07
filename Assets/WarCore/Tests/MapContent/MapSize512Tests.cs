using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class MapSize512Tests
    {
        private const int Side = 512;

        [Test]
        public void MaximumSide_IsOneSharedValue()
        {
            Assert.That(HeightField.MaxSide, Is.EqualTo(Side));
            Assert.That(WcMapValidation.MaxSide, Is.EqualTo(HeightField.MaxSide));
        }

        [Test]
        public void GridAndHeightField_AcceptMaximumSideAndRejectOneMore()
        {
            var grid = new NavGrid(Side, Side, default, default, new byte[Side * Side / 8]);
            Assert.That(grid.Count, Is.EqualTo(262144));
            var ground = new HeightField(Side, Side, default, default, new int[(Side + 1) * (Side + 1)]);
            Assert.That(ground.PointsX * ground.PointsZ, Is.EqualTo(263169));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HeightField(Side + 1, 1, default, default, new int[(Side + 2) * 2]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HeightField(1, Side + 1, default, default, new int[2 * (Side + 2)]));
        }

        [Test]
        public void MaximumOpenGrid_CornerToCornerAndClosedTarget_ZeroAllocationOnReuse()
        {
            var bits = new byte[Side * Side / 8];
            for (int i = 0; i < bits.Length; i++) bits[i] = 255;
            int last = Side * Side - 1;
            var search = new GridPathfinder(new NavGrid(Side, Side, default, default, bits));
            Assert.That(search.Find(0, last), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.Path.Length, Is.EqualTo(Side));
            Assert.That(search.TotalCost, Is.EqualTo((Side - 1) * 1414));
            for (int i = 0; i < Side; i++) Assert.That(search.Path[i], Is.EqualTo(i * (Side + 1)));
            // Seal the northeast corner cell so the next search exhausts the other open cells.
            foreach (int blocked in new[] { last - 1, last - Side, last - Side - 1 }) bits[blocked / 8] &= (byte)~(1 << (blocked % 8));
            var sealedSearch = new GridPathfinder(new NavGrid(Side, Side, default, default, bits));
            Assert.That(sealedSearch.Find(0, last), Is.EqualTo(GridPathStatus.Unreachable));
            Assert.That(sealedSearch.Find(0, last - Side - 2), Is.EqualTo(GridPathStatus.Found));
            long before = GC.GetAllocatedBytesForCurrentThread();
            sealedSearch.Find(0, last);
            sealedSearch.Find(0, last - Side - 2);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        // A search after the generation counter wraps must not read stamps left by searches before the wrap.
        [Test]
        public void GenerationWrap_ForgetsEarlierStamps()
        {
            var spec = (Seed: 104, Width: 97, Depth: 131, WallPercent: 35, MaxCost: 255);
            NavGrid grid = GridPathReuseLockTests.Build(spec, out TerrainTables tables);
            var reused = new GridPathfinder(tables);
            FieldInfo generation = typeof(GridPathfinder).GetField("generation", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(generation, Is.Not.Null);
            (int, int)[] queries = GridPathReuseLockTests.Queries(spec.Seed, grid.Count);
            int compared = 0;
            for (int i = 0; i + 1 < queries.Length; i += 2)
            {
                // Search with generation 1, then jump to the last value so the next search wraps back to 1.
                generation.SetValue(reused, 0U);
                reused.Find(queries[i].Item1, queries[i].Item2);
                generation.SetValue(reused, uint.MaxValue);
                (int start, int target) = queries[i + 1];
                var fresh = new GridPathfinder(tables);
                GridPathStatus a = reused.Find(start, target), b = fresh.Find(start, target);
                Assert.That(a, Is.EqualTo(b), $"{start}->{target}");
                Assert.That(reused.TotalCostWide, Is.EqualTo(fresh.TotalCostWide), $"{start}->{target}");
                Assert.That(reused.Path.ToArray(), Is.EqualTo(fresh.Path.ToArray()), $"{start}->{target}");
                if (a != GridPathStatus.Rejected) compared++;
            }
            Assert.That(compared, Is.GreaterThan(5));
        }

        [Test]
        public void MaximumMap_RoundTripsThroughCodecAndNavigation()
        {
            WcMap map = Build();
            byte[] bytes = WcMapWriter.Write(map);
            TestContext.Out.WriteLine($"512 x 512 map: {bytes.Length} bytes");
            Assert.That(bytes.Length, Is.LessThanOrEqualTo(WcMapValidation.MaxFileBytes));
            WcMap read = WcMapReader.Read(bytes);
            Assert.That(read.Width, Is.EqualTo((uint)Side));
            Assert.That(read.Depth, Is.EqualTo((uint)Side));
            Assert.That(read.Walls.Count, Is.EqualTo(2));
            Assert.That(read.Walls[1].HalfX, Is.EqualTo(SimFix.FromInt(150)));
            Assert.That(WcMapWriter.Write(read), Is.EqualTo(bytes));
            NavGrid grid = WcMapNavigation.Create(read);
            var search = new GridPathfinder(grid);
            Assert.That(search.Find(0, Side * Side - 1), Is.EqualTo(GridPathStatus.Found));
            Assert.That(search.TotalCost, Is.EqualTo((Side - 1) * 1414));
            Assert.That(grid.IsPassable(505, 492), Is.False);
            Assert.That(grid.IsPassable(299, 510), Is.False);
            Assert.That(grid.IsPassable(300, 510), Is.True);
        }

        // Heights rise 0.25 m per cell in a 4-cell saw along x; one small wall near the far corner and one 300 m wall.
        private static WcMap Build()
        {
            var heights = new int[(Side + 1) * (Side + 1)];
            for (int j = 0; j <= Side; j++)
                for (int i = 0; i <= Side; i++) heights[j * (Side + 1) + i] = (int)(i % 4 * SimFix.Scale / 4);
            var surfaces = new byte[Side * Side];
            for (int key = 0; key < surfaces.Length; key++) surfaces[key] = (byte)(key % Side >= 256 ? 1 : 0);
            var walls = new[]
            {
                new WcMapWall(0, 1, "wall", "walls", new SimVec3(SimFix.Ratio(1011, 2), SimFix.Ratio(3, 2), SimFix.Ratio(985, 2)),
                    SimFix.Ratio(1, 2), SimFix.FromInt(3), SimFix.Ratio(5, 2)),
                new WcMapWall(0, 2, "wall", "walls", new SimVec3(SimFix.FromInt(150), SimFix.Ratio(3, 2), SimFix.Ratio(1021, 2)),
                    SimFix.FromInt(150), SimFix.FromInt(3), SimFix.Ratio(1, 2)),
            };
            var bits = new byte[Side * Side / 8];
            for (int key = 0; key < Side * Side; key++)
            {
                int x = key % Side, z = key / Side;
                bool blocked = x == 505 && z >= 490 && z < 495 || x < 300 && z == 510;
                if (!blocked) bits[key >> 3] |= (byte)(1 << (key & 7));
            }
            return new WcMap("size-512", "rwr", default, SimVec3.Metres(Side, 10, Side), SimFix.FromInt(Side), default,
                Side, Side, new[] { new WcMapLayer("walls", 1) }, walls, bits, heights, new[] { "grass", "road" }, surfaces);
        }
    }
}
