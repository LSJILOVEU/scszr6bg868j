using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    [Category("Partitions")]
    public sealed class PartitionDifferentialTests
    {
        [Test]
        public void TwoHundredStepsMoveChangeLayersMigrateDestroyAndRestoreAgainstNaive()
        {
            const ulong seed = 0x5100200;
            var random = new A1MathInputs(seed);
            var scene = new GridOwnershipTestFixture(32, 2048);
            var handles = new EntityId[32];
            for (int i = 0; i < handles.Length; i++)
                handles[i] = PartitionContractTests.Add(scene, i - 16, i % 3 == 0 ? 2u : 1u, i % 2 == 0);
            scene.InitializeIndex();
            var output = new SpatialHit[7];
            int comparisons = 0;
            int migrations = 0;
            for (int step = 0; step < 200; step++)
            {
                int slot = (int)(random.Next() % 32);
                EntityId entity = handles[slot];
                if (step % 5 == 0)
                {
                    scene.Store.Destroy(entity);
                    scene.Sim.CompleteSpatialBoundary();
                    handles[slot] = scene.CreateBody(default);
                    entity = handles[slot];
                }
                var point = SimVec3.Metres((long)(random.Next() % 81) - 40, (long)(random.Next() % 21) - 10, (long)(random.Next() % 81) - 40);
                scene.Store.Set(scene.Positions, entity, new Position(point));
                uint layer = step % 4 == 0 ? 0u : step % 4 == 1 ? 1u : step % 4 == 2 ? 0x80000000u : uint.MaxValue;
                int radius = step % 7 == 0 ? 24 : 1;
                bool stationary = !PartitionContractTests.IsStatic(scene.Store.Read(scene.Colliders, entity));
                scene.Store.Set(scene.Colliders, entity, PartitionContractTests.Static(new Collider(SimFix.FromInt(radius), SimFix.FromInt(radius * 2), layer), stationary));
                migrations++;
                scene.Sim.CompleteSpatialBoundary();
                string[] logical = GridIndexRegressionAudit.Logical(scene.Grid);
                if (step % 17 == 0)
                {
                    ulong hash = scene.Sim.StateHash();
                    byte[] saved = scene.Sim.SaveSnapshot();
                    scene.Store.Set(scene.Colliders, entity, PartitionContractTests.Static(new Collider(default, default), !stationary));
                    scene.Sim.CompleteSpatialBoundary();
                    scene.Sim.RestoreSnapshot(saved);
                    Assert.That(scene.Sim.StateHash(), Is.EqualTo(hash));
                    GridIndexRegressionAudit.Equal(logical, GridIndexRegressionAudit.Logical(scene.Grid), "Both restored partitions at " + step);
                    var fresh = new GridOwnershipTestFixture(32, 2048);
                    fresh.Sim.RestoreSnapshot(saved);
                    GridIndexRegressionAudit.Equal(logical, GridIndexRegressionAudit.Logical(fresh.Grid), "Fresh engine rebuild at " + step);
                    Assert.That(fresh.Sim.StateHash(), Is.EqualTo(hash));
                }
                var bodies = new List<SpatialBody>();
                foreach (EntityId id in scene.Store)
                    bodies.Add(new SpatialBody(id, scene.Store.Read(scene.Positions, id).Value, scene.Store.Read(scene.Colliders, id)));
                var naive = new NaiveSpatialQuery(bodies);
                foreach (SpatialQueryKind kind in Enum.GetValues(typeof(SpatialQueryKind)))
                {
                    SpatialRequest q = GridOwnershipTestFixture.Request(kind, point);
                    GridOwnershipTestFixture.EqualResults(GridOwnershipTestFixture.Results(naive, in q, 32),
                        GridOwnershipTestFixture.Results(scene.Grid, in q, 32), "Legacy differential " + step);
                    foreach (uint mask in new uint[] { 0, 1, 2, 0x80000000u, uint.MaxValue })
                    {
                        var request = new SpatialRequest(q.Kind, q.Origin, q.Radius, q.Direction, q.Range, q.HalfHeight, q.Cosine, mask);
                        List<SpatialHit> expected = QueryModeOracle.Query(bodies, scene.Store, scene.Colliders, request, point);
                        Assert.That(scene.Grid.Any(in request), Is.EqualTo(expected.Count != 0));
                        Assert.That(scene.Grid.Nearest(in request, point, out SpatialHit hit), Is.EqualTo(expected.Count != 0));
                        QueryModeFixture.Same(hit, expected.Count == 0 ? default : expected[0]);
                        SpatialQueryResult result = scene.Grid.AllCapped(in request, point, 7, output);
                        Assert.That(result.TotalHits, Is.EqualTo(expected.Count));
                        Assert.That(result.Written, Is.EqualTo(Math.Min(7, expected.Count)));
                        Assert.That(result.Truncated, Is.EqualTo(expected.Count > 7));
                        for (int i = 0; i < result.Written; i++)
                            QueryModeFixture.Same(output[i], expected[i]);
                        comparisons++;
                    }
                }
                GridIndexRegressionAudit.Equal(logical, GridIndexRegressionAudit.Logical(scene.Grid), "Query preserves both partitions.");
            }
            Assert.That(comparisons, Is.EqualTo(5000));
            Assert.That(migrations, Is.EqualTo(200));
            TestContext.WriteLine("S1 partitions differential: seed=0x{0:X}; steps=200; migrations=200; five-shape/mask sets=5000; legacy/Any/Nearest/AllCapped; restore every 17 steps; mismatches=0.", seed);
        }
    }
}
