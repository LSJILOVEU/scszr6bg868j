using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class SimBenchTests
    {
        [TestCase(SimBenchWorkload.Light)]
        [TestCase(SimBenchWorkload.Heavy)]
        [TestCase(SimBenchWorkload.DenseAimedRays)]
        [TestCase(SimBenchWorkload.SphereOnly)]
        public void G10_Headless2000EntityProfilesReportWholeTicksWithoutTimingAssertions(SimBenchWorkload workload)
        {
            SimBench.RunProfile(workload, 2000, 51, TestContext.Out);
        }

        [Test]
        public void FullPublicQueryDistributionsTimeIndividualCallsAndValidateOutputs()
        {
            SimBench.RunSingleQueries(2000, TestContext.Out);
        }

        [Test]
        public void PreciseRayCounterExcludesSlabRejectsButIncludesPreciseMisses()
        {
            var sim = Fixtures.Empty(1);
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            EntityId first = sim.Entities.Create();
            EntityId second = sim.Entities.Create();
            sim.Entities.Set(positions, first, new Position());
            sim.Entities.Set(positions, second, new Position(SimVec3.Metres(3, 0, 3)));
            sim.Entities.Set(colliders, first, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
            sim.Entities.Set(colliders, second, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
            var grid = new GridSpatialQuery(sim.Entities, positions, colliders, SimFix.FromInt(8), 4, 32);
            grid.Rebuild();
            var rays = new RayHit[4];
            Assert.That(grid.Ray(new SimVec3(SimFix.FromInt(-3), SimFix.Ratio(9, 10), SimFix.Ratio(9, 10)),
                SimVec3.Metres(1, 0, 0), SimFix.FromInt(10), rays), Is.Zero);
            Assert.That(grid.LastCandidateCount, Is.EqualTo(2));
            Assert.That(grid.LastNarrowPhaseCount, Is.EqualTo(1));
            Assert.That(grid.Ray(SimVec3.Metres(-3, 0, 0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(10), rays), Is.EqualTo(1));
            Assert.That(grid.LastNarrowPhaseCount, Is.EqualTo(1));
            Assert.That(rays[0].Distance, Is.EqualTo(SimFix.FromInt(2)));
        }
    }
}
