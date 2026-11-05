using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class HybridBoundaryTests
    {
        [Test]
        public void OpenAddressingComparesAllThreeFullLongCoordinates()
        {
            var table = new SpatialCellTable(16, 8, false);
            long[] values = { long.MinValue, long.MaxValue, -(1L << 40), 1L << 40, 0, 1, -1, 65536 };
            for (int i = 0; i < values.Length; i++)
                table.CountMember(values[i], values[(i + 1) % 8], values[(i + 2) % 8], i % 5);
            table.Pack();
            for (int i = 0; i < values.Length; i++)
                table.Fill(values[i], values[(i + 1) % 8], values[(i + 2) % 8], i % 5, i);
            Assert.That(table.Count, Is.EqualTo(8));
            Assert.That(table.Count * 2, Is.EqualTo(table.Cells.Length));
            for (int i = 0; i < values.Length; i++)
            {
                SpatialCell cell = table.Cells[table.Find(values[i], values[(i + 1) % 8], values[(i + 2) % 8], i % 5)];
                Assert.That(table.Members[cell.Start], Is.EqualTo(i));
                Assert.That(cell.Count, Is.EqualTo(1));
            }
            Assert.Throws<SimBoundaryException>(() => table.CountMember(2, 3, 4, 0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TinyRawGridPrunesWithTopCellRegionsWithoutDirectoryScan(bool ray)
        {
            var store = new EntityStore(2000);
            var p = store.Register<Position>("position");
            var c = store.Register<Collider>("collider");
            var random = new A1MathInputs(0xB30002);
            for (int i = 0; i < 2000; i++)
                HybridStorageTests.Add(store, p, c, SimVec3.Metres((long)(random.Next() % 32001) - 16000,
                    (long)(random.Next() % 32001) - 16000, (long)(random.Next() % 32001) - 16000));
            var grid = new GridSpatialQuery(store, p, c, SimFix.FromRaw(1), 2000);
            grid.Rebuild();
            grid.Diagnostics = new SpatialQueryDiagnostics();
            var origin = SimVec3.Metres(-16000, -16000, -16000);
            int count = ray ? grid.Ray(origin, SimVec3.Metres(1, 0, 0), SimFix.FromInt(1000), new RayHit[2000])
                : grid.Sphere(origin, SimFix.FromInt(1000), new EntityId[2000]);
            Assert.That(count, Is.Zero);
            Assert.That(grid.Diagnostics.RegionNodes, Is.InRange(1, 128));
            Assert.That(grid.Diagnostics.LeafMembers, Is.LessThan(100));
            Assert.That(grid.Diagnostics.DirectorySlotsScanned, Is.Zero);
            TestContext.WriteLine("2bB raw=1 pruning: ray={0}; region_nodes={1}; members={2}; hash_probes={3}.",
                ray, grid.Diagnostics.RegionNodes, grid.Diagnostics.LeafMembers, grid.Diagnostics.HashProbes);
        }

        [Test]
        public void InvalidBudgetsFailExplicitlyBeforeArrayAllocation()
        {
            Assert.Throws<SimBoundaryException>(() => new SimConfig(spatialMembershipCapacity: int.MaxValue));
            Assert.Throws<SimBoundaryException>(() => new SimConfig(spatialMembershipCapacity: 0));
            Assert.Throws<SimBoundaryException>(() => new SimConfig(spatialCellCapacity: 3));
            Assert.Throws<SimBoundaryException>(() => new SimConfig(spatialCellCapacity: int.MaxValue));
            Assert.Throws<SimBoundaryException>(() => new SimConfig(spatialMaxCellsPerBody: 0));
            Assert.Throws<SimBoundaryException>(() => GridSpatialIndex.ResolveCellCapacity(int.MaxValue, 0));
        }
    }
}
