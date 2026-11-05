using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class PostureTests
    {
        [Test]
        public void G20_TwoHeightsDistinguishAllThreePosturesAndSameRayChangesHit()
        {
            var f = new PostureFixture();
            var hits = new RayHit[4];
            int[] high = new int[3];
            int[] low = new int[3];
            for (int id = 1; id <= 3; id++)
            {
                f.Set(id);
                high[id - 1] = f.Grid.Ray(SimVec3.Metres(-4, 2, 0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(8), hits);
                low[id - 1] = f.Grid.Ray(SimVec3.Metres(-4, 1, 0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(8), hits);
                SpatialBody body = f.Geometry.Body(f.Unit, f.Sim.Entities.Get(f.Positions, f.Unit).Value, f.Sim.Entities.Get(f.Colliders, f.Unit));
                Assert.That(body.Position.Y.Raw - body.Collider.Height.Raw / 2, Is.Zero);
                Assert.That(f.Sim.Entities.Get(f.Positions, f.Unit).Value.Y, Is.EqualTo(SimFix.Ratio(3, 2)));
                CompareAll(f.Grid, f.Oracle(true), SimVec3.Metres(-4, 1, 0));
            }

            Assert.That(high, Is.EqualTo(new[] { 1, 0, 0 }));
            Assert.That(low, Is.EqualTo(new[] { 1, 1, 0 }));
            TestContext.WriteLine("G20 rays y=2m/y=1m: standing=hit/hit, crouching=miss/hit, prone=miss/miss; baseline foot and Position preserved.");
        }

        [Test]
        public void G20_AbsoluteHeightDataControlsSpeedDetectionAndHitModifierUntilExactDueTick()
        {
            var f = new PostureFixture(true);
            long task = f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 3);
            for (int tick = 1; tick <= 4; tick++)
            {
                f.Sim.Step(default);
                Assert.That(f.Sim.Entities.Get(f.Postures, f.Unit).CurrentId, Is.EqualTo(tick < 3 ? 1 : 2));
                Assert.That(f.Sim.Entities.Get(f.Positions, f.Unit).Value.X.Raw,
                    Is.EqualTo(tick < 3 ? tick * SimFix.Scale : (2 * SimFix.Scale + (tick - 2) * SimFix.Scale / 2)));
                Assert.That(f.Sim.Entities.Get(f.Positions, f.Unit).Value.Y, Is.EqualTo(SimFix.Ratio(3, 2)));
                Assert.That(f.Geometry.DetectionDistance(f.Unit, SimFix.FromInt(10)),
                    Is.EqualTo(tick < 3 ? SimFix.FromInt(10) : PostureScale.Apply(SimFix.FromInt(10), SimFix.Ratio(3, 5))));
                Assert.That(f.Geometry.HitModifier(f.Unit).Raw, Is.EqualTo(tick < 3 ? 10 : -20));
            }

            Assert.That(f.Sim.Entities.Get(f.Postures, f.Unit).TaskId, Is.Zero);
            Assert.That(f.Sim.Scheduler.Cancel(task), Is.False);
            Assert.That(f.Sim.Scheduler.DueCount, Is.Zero);
            f.Set(3);
            f.Sim.Step(default);
            Assert.That(f.Sim.Entities.Get(f.Positions, f.Unit).Value.X, Is.EqualTo(SimFix.Ratio(13, 4)));
            Assert.That(f.Geometry.DetectionDistance(f.Unit, SimFix.FromInt(10)), Is.EqualTo(PostureScale.Apply(SimFix.FromInt(10), SimFix.Ratio(3, 10))));
            Assert.That(f.Geometry.HitModifier(f.Unit).Raw, Is.EqualTo(-30));
        }

        [Test]
        public void G20_IncrementalRebuiltNaiveAndIndependentOracleAgreeForFiveQueries()
        {
            var f = new PostureFixture();
            var random = new System.Random(207123);
            for (int i = 0; i < 63; i++)
                f.Add(random.Next(-12, 13), random.Next(-12, 13), i % 3 + 1);
            var rebuilt = new GridSpatialQuery(f.Sim.Entities, f.Positions, f.Colliders, SimFix.FromInt(2), 128, 4096, f.Geometry);
            for (int round = 0; round < 24; round++)
            {
                foreach (EntityId entity in f.Sim.Entities)
                {
                    f.Sim.Entities.Set(f.Postures, entity, new Posture(random.Next(4)));
                    f.Sim.CompleteSpatialBoundary();
                }

                rebuilt.Rebuild();
                var origin = SimVec3.Metres(random.Next(-10, 11), random.Next(4), random.Next(-10, 11));
                CompareAll(f.Grid, rebuilt, origin);
                CompareAll(f.Grid, f.Oracle(false), origin);
                CompareAll(f.Grid, f.Oracle(true), origin);
            }
        }

        internal static void CompareAll(ISpatialQuery left, ISpatialQuery right, SimVec3 origin)
        {
            var a = new EntityId[128];
            var b = new EntityId[128];
            var ra = new RayHit[128];
            var rb = new RayHit[128];
            for (int kind = 0; kind < 5; kind++)
            {
                int ac = Query(left, kind, origin, a, ra);
                int bc = Query(right, kind, origin, b, rb);
                Assert.That(ac, Is.EqualTo(bc), "query kind " + kind);
                for (int i = 0; i < ac; i++)
                    if (kind == 4)
                    {
                        Assert.That(ra[i].Entity, Is.EqualTo(rb[i].Entity));
                        Assert.That(ra[i].Distance, Is.EqualTo(rb[i].Distance));
                    }
                    else
                        Assert.That(a[i], Is.EqualTo(b[i]));
            }
        }

        private static int Query(ISpatialQuery query, int kind, SimVec3 origin, EntityId[] entities, RayHit[] rays)
        {
            switch (kind)
            {
                case 0: return query.Sphere(origin, SimFix.FromInt(5), entities);
                case 1: return query.Cylinder(origin, SimFix.FromInt(5), SimFix.Ratio(1, 4), entities);
                case 2: return query.Sector(origin, SimFix.FromInt(8), SimVec3.Metres(1, 0, 1), 600, SimFix.Ratio(1, 4), entities);
                case 3: return query.Capsule(origin, origin + SimVec3.Metres(8, 0, 0), SimFix.Ratio(1, 8), entities);
                default: return query.Ray(origin, SimVec3.Metres(1, 0, 0), SimFix.FromInt(24), rays);
            }
        }

        [Test]
        public void G20_TransitionSnapshotsRestoreCurrentTargetDueIdAndCachedGeometry()
        {
            var a = new PostureFixture();
            var b = new PostureFixture();
            long task = a.System.BeginTransition(a.Sim.Context, a.Unit, 3, 4);
            a.Sim.Step(default);
            byte[] snapshot = a.Sim.SaveSnapshot();
            b.Sim.RestoreSnapshot(snapshot);
            Assert.That(b.Sim.Entities.Get(b.Postures, b.Unit), Is.EqualTo(new Posture(1, 3, 4, task)));
            Assert.That(b.Sim.StateHash(), Is.EqualTo(a.Sim.StateHash()));
            for (int tick = 2; tick <= 6; tick++)
            {
                a.Sim.Step(default);
                b.Sim.Step(default);
                Assert.That(b.Sim.StateHash(), Is.EqualTo(a.Sim.StateHash()));
                CompareAll(b.Grid, b.Oracle(true), SimVec3.Metres(-4, 1, 0));
                Assert.That(b.Sim.Entities.Get(b.Postures, b.Unit).CurrentId, Is.EqualTo(tick < 4 ? 1 : 3));
            }
        }

        [Test]
        public void G20_BaselineFallbackOddHalfHeightsAndInvalidGeometryAreExplicit()
        {
            var f = new PostureFixture();
            f.Sim.Entities.Remove(f.Postures, f.Unit);
            Collider baseline = f.Sim.Entities.Get(f.Colliders, f.Unit);
            SimVec3 position = f.Sim.Entities.Get(f.Positions, f.Unit).Value;
            Assert.That(f.Geometry.Body(f.Unit, position, baseline).Collider.Height, Is.EqualTo(baseline.Height));
            Assert.That(f.Geometry.SpeedFactor(f.Unit), Is.EqualTo(SimFix.FromInt(1)));
            Assert.That(f.Geometry.DetectionDistance(f.Unit, SimFix.FromInt(7)), Is.EqualTo(SimFix.FromInt(7)));
            var catalog = new PostureCatalog(new PostureDef(1, SimFix.FromRaw(32769), default, default, default));
            var geometry = new PostureGeometry(f.Sim.Entities, f.Postures, catalog);
            f.Set(1);
            var odd = new Collider(SimFix.FromRaw(1), SimFix.FromRaw(65539));
            SpatialBody body = geometry.Body(f.Unit, position, odd);
            Assert.That(body.Position.Y.Raw - body.Collider.Height.Raw / 2, Is.EqualTo(position.Y.Raw - odd.Height.Raw / 2));
            f.Sim.Entities.Set(f.Colliders, f.Unit, new Collider(SimFix.FromInt(1), SimFix.FromInt(3)));
            ulong before = f.Sim.StateHash();
            Assert.Throws<ArgumentOutOfRangeException>(() => f.System.BeginTransition(f.Sim.Context, f.Unit, 3, 1));
            Assert.That(f.Sim.StateHash(), Is.EqualTo(before));
            Assert.Throws<ArgumentOutOfRangeException>(() => geometry.Body(f.Unit, SimVec3.Metres(0, -16384, 0), odd));
        }

        [Test]
        public void G20_TransitionCancellationStaleEntityAndUnrelatedDueTasksDoNotInterfere()
        {
            var f = new PostureFixture();
            long other = f.Sim.Scheduler.Schedule(1, new SimEvent(new EventTypeId(811)));
            f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 1);
            Assert.Throws<InvalidOperationException>(() => f.System.BeginTransition(f.Sim.Context, f.Unit, 3, 2));
            Assert.That(f.System.CancelTransition(f.Sim.Context, f.Unit), Is.True);
            Assert.That(f.System.CancelTransition(f.Sim.Context, f.Unit), Is.False);
            f.System.BeginTransition(f.Sim.Context, f.Unit, 3, 1);
            f.Sim.Entities.Destroy(f.Unit);
            EntityId reused = f.Add(0, 0, 2);
            f.Sim.Step(default);
            Assert.That(f.Sim.Entities.Get(f.Postures, reused).CurrentId, Is.EqualTo(2));
            Assert.That(f.Sim.Scheduler.TryDequeue(out ScheduledTask due), Is.True);
            Assert.That(due.Id, Is.EqualTo(other));
            Assert.That(f.Sim.Scheduler.DueCount, Is.Zero);
        }

        [Test]
        public void G20_ExplicitScalingAndDeadlineBoundariesDoNotDependOnRuntimeExceptions()
        {
            SimFix one = SimFix.FromInt(1);
            Assert.That(PostureScale.Apply(SimFix.FromRaw(long.MinValue), one).Raw, Is.EqualTo(long.MinValue));
            Assert.That(PostureScale.Apply(SimFix.FromRaw(long.MaxValue), one).Raw, Is.EqualTo(long.MaxValue));
            Assert.That(PostureScale.Apply(SimFix.FromRaw(long.MinValue), default).Raw, Is.Zero);
            Assert.That(PostureScale.Apply(SimFix.FromRaw(-3), SimFix.Ratio(1, 2)).Raw, Is.EqualTo(-1));
            Assert.Throws<SimBoundaryException>(() => PostureScale.Apply(SimFix.FromRaw(long.MinValue), SimFix.FromInt(2)));
            Assert.Throws<SimBoundaryException>(() => PostureScale.Apply(SimFix.FromRaw(long.MaxValue), SimFix.FromInt(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => PostureScale.Apply(one, SimFix.FromRaw(-1)));
            var f = new PostureFixture();
            f.Sim.Step(default);
            ulong before = f.Sim.StateHash();
            Assert.Throws<SimBoundaryException>(() => f.System.BeginTransition(f.Sim.Context, f.Unit, 2, long.MaxValue));
            Assert.That(f.Sim.StateHash(), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 0));
        }

        [Test]
        public void G20_WarmTransitionsMovementAndQueriesAllocateNothing()
        {
            var windows = new AllocationWindow();
            do
            {
                var f = new PostureFixture(true);
                var output = new EntityId[128];
                f.Sim.Step(default);
                var meter = new AllocationMeter();
                for (int i = 0; i < 16; i++)
                    TransitionStep(f, output, i);
                windows.Begin(meter);
                for (int i = 0; i < 128; i++)
                    TransitionStep(f, output, i);
                AllocationSample allocated = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                Assert.That(f.Sim.Events.EventsDroppedTotal, Is.Zero);
                TestContext.WriteLine("G20 warm transition/movement/query: {0}; EventsDropped=0.", allocated.Describe());
            }
            while (windows.NeedsRetry);
        }

        private static void TransitionStep(PostureFixture f, EntityId[] output, int i)
        {
            f.System.BeginTransition(f.Sim.Context, f.Unit, i % 2 == 0 ? 2 : 1, 1);
            f.Sim.Step(default);
            f.Sim.CompleteSpatialBoundary();
            f.Grid.Sphere(f.Sim.Entities.Get(f.Positions, f.Unit).Value, SimFix.FromInt(1), output);
        }
    }
}
