using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class LayerQueryTests
    {
        private static void Compare(LayerQueryFixture fixture, ISpatialQuery oracle, SpatialRequest request, uint mask, bool legacy = false)
        {
            int expected = LayerQueryFixture.Run(oracle, request, mask, fixture.ExpectedEntities, fixture.ExpectedRays);
            int actual = LayerQueryFixture.Run(fixture.Grid, request, mask, fixture.Entities, fixture.Rays, legacy);
            Assert.That(actual, Is.EqualTo(expected), request.Kind + " mask=" + mask);
            for (int i = 0; i < actual; i++)
            {
                if (request.Kind == SpatialQueryKind.Ray)
                {
                    Assert.That(fixture.Rays[i].Entity, Is.EqualTo(fixture.ExpectedRays[i].Entity));
                    Assert.That(fixture.Rays[i].Distance.Raw, Is.EqualTo(fixture.ExpectedRays[i].Distance.Raw));
                }
                else
                    Assert.That(fixture.Entities[i], Is.EqualTo(fixture.ExpectedEntities[i]));
            }
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        [TestCase(0x80000000u)]
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(0x80000001u)]
        [TestCase(0x80000002u)]
        [TestCase(0x40000000u)]
        public void AllFiveMaskedQueriesMatchIndependentGeometryAndOrderedRawResults(uint mask)
        {
            var fixture = new LayerQueryFixture();
            fixture.Populate();
            NaiveSpatialQuery oracle = fixture.Oracle();
            for (int kind = 0; kind < 5; kind++)
                Compare(fixture, oracle, LayerQueryFixture.Request((SpatialQueryKind)kind), mask);
        }

        [Test]
        public void LegacyAndExplicitAllMasksKeepOrderGenerationAndRawDistances()
        {
            var fixture = new LayerQueryFixture();
            fixture.Populate();
            NaiveSpatialQuery oracle = fixture.Oracle();
            for (int kind = 0; kind < 5; kind++)
            {
                SpatialRequest request = LayerQueryFixture.Request((SpatialQueryKind)kind);
                Compare(fixture, oracle, request, uint.MaxValue, legacy: true);
                Compare(fixture, oracle, request, uint.MaxValue);
            }
        }

        [Test]
        public void ZeroMaskDoesNoTraversalOrWritesAndNonmatchingLayersDoNoNarrowPhase()
        {
            var fixture = new LayerQueryFixture();
            fixture.Add(default, new Collider(SimFix.FromInt(1), SimFix.FromInt(2), 1u));
            var diagnostics = new SpatialQueryDiagnostics();
            fixture.Grid.Diagnostics = diagnostics;
            for (int kind = 0; kind < 5; kind++)
            {
                SpatialRequest request = LayerQueryFixture.Request((SpatialQueryKind)kind);
                Assert.That(LayerQueryFixture.Run(fixture.Grid, request, 1u, fixture.Entities, fixture.Rays), Is.EqualTo(1));
                Assert.That(LayerQueryFixture.Run(fixture.Grid, request, 2u, Span<EntityId>.Empty, Span<RayHit>.Empty), Is.Zero);
                Assert.That(fixture.Grid.LastVisitedCells, Is.GreaterThan(0));
                Assert.That(fixture.Grid.LastCandidateCount, Is.Zero);
                Assert.That(fixture.Grid.LastNarrowPhaseCount, Is.Zero);
                Assert.That(LayerQueryFixture.Run(fixture.Grid, request, 0u, Span<EntityId>.Empty, Span<RayHit>.Empty), Is.Zero);
                Assert.That(fixture.Grid.LastVisitedCells, Is.Zero);
                Assert.That(diagnostics.WalkCalls, Is.Zero);
                Assert.That(diagnostics.LeafMembers, Is.Zero);
                Assert.That(fixture.Grid.LastCandidateCount, Is.Zero);
                Assert.That(fixture.Grid.LastNarrowPhaseCount, Is.Zero);
            }
        }

        [Test]
        public void RuntimeLayerChangesApplyBeforeNextQueryWithoutUpsert()
        {
            var fixture = new LayerQueryFixture();
            fixture.Populate();
            int memberships = fixture.Grid.MembershipCount;
            uint[] masks = { 0, 1, 2, 0x80000000, 0x80000001, uint.MaxValue };
            foreach (uint layer in masks)
            {
                foreach (EntityId entity in fixture.Sim.Entities)
                {
                    ref Collider collider = ref fixture.Sim.Entities.Get(fixture.Colliders, entity);
                    collider = collider.WithLayer(layer);
                }
                NaiveSpatialQuery oracle = fixture.Oracle();
                foreach (uint mask in masks)
                    for (int kind = 0; kind < 5; kind++)
                        Compare(fixture, oracle, LayerQueryFixture.Request((SpatialQueryKind)kind), mask);
                Assert.That(fixture.Grid.MembershipCount, Is.EqualTo(memberships));
            }
        }

        [TestCase(0x3A0123)]
        [TestCase(0x3A4567)]
        public void BoundaryBiasedMaskedQueriesMatchFrozenOracle(int seed)
        {
            var random = new Random(seed);
            var fixture = new LayerQueryFixture();
            for (int i = 0; i < 96; i++)
            {
                var position = new SimVec3(RawCell(random), RawCell(random), RawCell(random));
                long radius = i % 5 == 0 ? 0 : random.Next(1, 2 * (int)SimFix.Scale);
                long height = 2 * radius + random.Next(0, 2 * (int)SimFix.Scale);
                uint layer = i % 8 == 0 ? 0 : 1u << (i % 32);
                if (i % 7 == 0)
                    layer |= 0x80000001u;
                fixture.Add(position, new Collider(SimFix.FromRaw(radius), SimFix.FromRaw(height), layer));
            }
            NaiveSpatialQuery oracle = fixture.Oracle();
            int checkedQueries = 0;
            for (int sample = 0; sample < 128; sample++)
            {
                var origin = new SimVec3(RawCell(random), RawCell(random), RawCell(random));
                SimVec3 direction = SimVec3.Metres(random.Next(1, 8), 0, random.Next(-8, 9));
                uint mask = sample % 9 == 0 ? uint.MaxValue : (1u << (sample % 32)) | (1u << random.Next(32));
                for (int kind = 0; kind < 5; kind++)
                {
                    var request = new SpatialRequest((SpatialQueryKind)kind, origin, SimFix.FromInt(sample % 17), direction,
                        SimFix.FromInt(sample % 31), SimFix.FromInt(sample % 9), sample % 2 == 0 ? 600 : -1000);
                    Compare(fixture, oracle, request, mask);
                    checkedQueries++;
                }
            }
            TestContext.WriteLine("Layer differential seed=0x{0:X}; bodies=96; queries={1}; mismatches=0; independent frozen geometry.", seed, checkedQueries);
        }

        private static SimFix RawCell(Random random) => SimFix.FromRaw(random.Next(-4, 5) * 3L * SimFix.Scale + random.Next(-1, 2));

        [TestCase(false)]
        [TestCase(true)]
        public void SnapshotRoundTripRestoresLayersAndAllFiveQueryResults(bool fresh)
        {
            var source = new LayerQueryFixture();
            source.Populate();
            source.Sim.Step(default);
            byte[] snapshot = source.Sim.SaveSnapshot();
            ulong hash = source.Sim.StateHash();
            NaiveSpatialQuery oracle = source.Oracle();
            LayerQueryFixture target = fresh ? new LayerQueryFixture() : source;
            if (!fresh)
                foreach (EntityId entity in target.Sim.Entities)
                    target.Sim.Entities.Get(target.Colliders, entity) = target.Sim.Entities.Get(target.Colliders, entity).WithLayer(0);
            target.Sim.RestoreSnapshot(snapshot);
            // New grids still use the existing explicit rebuild boundary; automatic ownership is step 2b.
            if (fresh)
                target.Grid.Rebuild();
            Assert.That(target.Sim.StateHash(), Is.EqualTo(hash));
            Assert.That(target.Sim.SaveSnapshot(), Is.EqualTo(snapshot));
            uint[] masks = { 0, 1, 2, 3, 0x80000000, uint.MaxValue };
            foreach (uint mask in masks)
                for (int kind = 0; kind < 5; kind++)
                    Compare(target, oracle, LayerQueryFixture.Request((SpatialQueryKind)kind), mask);
        }

        [Test]
        public void PostureProjectionPreservesLayerAndLayerChangesRemainVisible()
        {
            var fixture = new PostureFixture();
            uint[] layers = { 0, 1, 0x80000000, uint.MaxValue };
            foreach (uint layer in layers)
            {
                ref Collider collider = ref fixture.Sim.Entities.Get(fixture.Colliders, fixture.Unit);
                collider = collider.WithLayer(layer);
                for (int posture = 1; posture <= 3; posture++)
                {
                    fixture.Set(posture);
                    SpatialBody body = fixture.Geometry.Body(fixture.Unit, fixture.Sim.Entities.Get(fixture.Positions, fixture.Unit).Value, collider);
                    Assert.That(body.Collider.Layer, Is.EqualTo(layer));
                    var hits = new EntityId[1];
                    int count = fixture.Grid.Sphere(default, SimFix.FromInt(4), 0x80000000u, hits);
                    Assert.That(count, Is.EqualTo((layer & 0x80000000u) == 0 ? 0 : 1));
                }
            }
        }

        [Test]
        public void WarmMaskedQueriesLayerChangesAndColliderHashAllocateZeroBytes()
        {
            var windows = new AllocationWindow();
            do
            {
                var fixture = new LayerQueryFixture();
                EntityId unit = fixture.Add(default, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
                var requests = new SpatialRequest[5];
                for (int kind = 0; kind < requests.Length; kind++)
                    requests[kind] = LayerQueryFixture.Request((SpatialQueryKind)kind);
                var hash = new StateHash();
                var meter = new AllocationMeter();
                int count = 0;
                for (int i = 0; i < 64; i++)
                    count += QueryAndHash(fixture, unit, requests, hash, i);
                windows.Begin(meter);
                for (int i = 0; i < 512; i++)
                    count += QueryAndHash(fixture, unit, requests, hash, i);
                AllocationSample allocated = windows.End();
                Assert.That(count, Is.GreaterThan(0));
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                TestContext.WriteLine("Layer paths: 2560 warmed masked queries + 512 layer replacements/collider hashes; {0}.", allocated.Describe());
            }
            while (windows.NeedsRetry);
        }

        private static int QueryAndHash(LayerQueryFixture fixture, EntityId unit, SpatialRequest[] requests, StateHash hash, int iteration)
        {
            ref Collider collider = ref fixture.Sim.Entities.Get(fixture.Colliders, unit);
            collider = collider.WithLayer(iteration % 2 == 0 ? 0x80000000u : 1u);
            collider.WriteState(hash);
            int count = 0;
            foreach (SpatialRequest request in requests)
                count += LayerQueryFixture.Run(fixture.Grid, request, 0x80000000u, fixture.Entities, fixture.Rays);
            return count;
        }
    }
}
