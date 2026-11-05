using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class PostureDataTests
    {
        [Test]
        public void SnapshotReplacesDifferentPostureDataAndRestoresFutureMovementAndDetection()
        {
            var source = new PostureFixture(true);
            source.System.BeginTransition(source.Sim.Context, source.Unit, 2, 3);
            source.Sim.Step(default);
            var different = new PostureCatalog(
                new PostureDef(1, SimFix.FromInt(4), SimFix.FromInt(2), SimFix.FromInt(5), SimFix.FromInt(2)),
                new PostureDef(2, SimFix.FromInt(2), SimFix.FromInt(1), SimFix.FromInt(7), SimFix.FromInt(3)));
            var target = new PostureFixture(true, data: different);
            Assert.That(Fixtures.Hash(target.Catalog), Is.Not.EqualTo(Fixtures.Hash(source.Catalog)));
            target.Sim.RestoreSnapshot(source.Sim.SaveSnapshot());
            Assert.That(Fixtures.Hash(target.Catalog), Is.EqualTo(Fixtures.Hash(source.Catalog)));
            Assert.That(target.Catalog.Get(3).Height, Is.EqualTo(SimFix.Ratio(1, 2)));
            for (int tick = 2; tick <= 5; tick++)
            {
                source.Sim.Step(default);
                target.Sim.Step(default);
                Assert.That(target.Sim.StateHash(), Is.EqualTo(source.Sim.StateHash()));
                Assert.That(target.Geometry.DetectionDistance(target.Unit, SimFix.FromInt(10)),
                    Is.EqualTo(source.Geometry.DetectionDistance(source.Unit, SimFix.FromInt(10))));
            }

            Assert.That(target.Sim.Entities.Get(target.Positions, target.Unit).Value.X, Is.EqualTo(SimFix.Ratio(7, 2)));
            target.System.BeginTransition(target.Sim.Context, target.Unit, 0, 1);
            target.Sim.Step(default);
            Assert.That(target.Sim.Entities.Get(target.Postures, target.Unit).CurrentId, Is.Zero);
            Assert.That(target.Geometry.SpeedFactor(target.Unit), Is.EqualTo(SimFix.FromInt(1)));
            Assert.That(target.Geometry.DetectionDistance(target.Unit, SimFix.FromInt(10)), Is.EqualTo(SimFix.FromInt(10)));
            Assert.That(target.System.BeginTransition(target.Sim.Context, target.Unit, 0, 1), Is.Zero);
        }

        [Test]
        public void EveryPostureDefinitionFieldChangesHashAndInvalidDataFailsExplicitly()
        {
            SimFix one = SimFix.FromInt(1);
            SimFix two = SimFix.FromInt(2);
            var baseline = new PostureDef(1, one, one, one, one);
            var variants = new[]
            {
                new PostureDef(2, one, one, one, one),
                new PostureDef(1, two, one, one, one),
                new PostureDef(1, one, two, one, one),
                new PostureDef(1, one, one, two, one),
                new PostureDef(1, one, one, one, two)
            };
            foreach (PostureDef value in variants)
                Assert.That(Fixtures.Hash(value), Is.Not.EqualTo(Fixtures.Hash(baseline)));
            Assert.Throws<ArgumentException>(() => new PostureCatalog(baseline, baseline));
            Assert.Throws<ArgumentException>(() => new PostureCatalog(default(PostureDef)));
            Assert.Throws<InvalidOperationException>(() => new PostureCatalog(baseline).Get(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PostureDef(0, one, one, one, one));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PostureDef(1, SimFix.FromRaw(long.MaxValue), one, one, one));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PostureDef(1, one, SimFix.FromRaw(-1), one, one));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PostureDef(1, one, one, one, SimFix.FromRaw(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Posture(1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Posture(1, 1, 2, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Posture(1, 2, 0, 3));
        }

        [Test]
        public void DimensionlessScaleMatchesIndependentIntegerOracleAcrossFullLongRange()
        {
            var random = new System.Random(0x207123);
            var bytes = new byte[8];
            int success = 0;
            int boundary = 0;
            for (int i = 0; i < 4096; i++)
            {
                random.NextBytes(bytes);
                long value = BitConverter.ToInt64(bytes, 0);
                random.NextBytes(bytes);
                long factor = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
                if (i % 2 == 0)
                    factor %= SimFix.Scale * 2;
                BigInteger expected = (BigInteger)value * factor / SimFix.Scale;
                if (expected < long.MinValue || expected > long.MaxValue)
                {
                    Assert.Throws<SimBoundaryException>(() => PostureScale.Apply(SimFix.FromRaw(value), SimFix.FromRaw(factor)));
                    boundary++;
                }
                else
                {
                    Assert.That(PostureScale.Apply(SimFix.FromRaw(value), SimFix.FromRaw(factor)).Raw, Is.EqualTo((long)expected));
                    success++;
                }
            }

            Assert.That(success, Is.GreaterThan(0));
            Assert.That(boundary, Is.GreaterThan(0));
            TestContext.WriteLine("Posture dimensionless scale: independent BigInteger oracle, seed=0x207123, cases=4096, success={0}, explicit boundary={1}, mismatches=0.", success, boundary);
        }

        [Test]
        public void TypedDueDispatchPreservesWrappedQueueOrderAndOriginalTaskIds()
        {
            var scheduler = new SimScheduler(8);
            var own = new EventTypeId(801);
            var other = new EventTypeId(802);
            for (int i = 0; i < 6; i++)
                scheduler.Schedule(1, new SimEvent(other));
            scheduler.Advance(1);
            for (int i = 0; i < 5; i++)
                scheduler.TryDequeue(out _);
            for (int i = 0; i < 6; i++)
                scheduler.Schedule(2, new SimEvent(i % 2 == 0 ? own : other));
            scheduler.Advance(2);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(scheduler.TryDequeue(own, out ScheduledTask due), Is.True);
                Assert.That(due.Id, Is.EqualTo(7 + i * 2));
            }

            Assert.That(scheduler.TryDequeue(own, out _), Is.False);
            foreach (long expected in new long[] { 6, 8, 10, 12 })
            {
                Assert.That(scheduler.TryDequeue(out ScheduledTask due), Is.True);
                Assert.That(due.Id, Is.EqualTo(expected));
            }

            Assert.That(scheduler.DueCount, Is.Zero);
        }
    }
}
