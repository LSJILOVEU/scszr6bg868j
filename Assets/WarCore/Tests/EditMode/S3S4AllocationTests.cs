using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S3S4Allocation")]
    public sealed class S3S4AllocationTests
    {
        private static AllocationMeter DirectMeter()
        {
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero requires a working direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            return meter;
        }

        private static long MathCycle()
        {
            SimVec3 vector = SimVec3.Metres(3, 4, 0);
            SimVec3 wide = new SimVec3(SimFix.FromRaw(long.MinValue), SimFix.FromRaw(long.MaxValue), default);
            SimFix a = SimFix.Ratio(-1, 30);
            SimFix b = SimFix.Max(SimFix.Min(SimFix.Abs(a), SimFix.FromInt(2)), SimFix.FromInt(1));
            SimFix scalar = SimFix.Round(SimFix.Floor(SimFix.Clamp(SimFix.Lerp(a, b, b), a, b)));
            SimVec3 result = vector * scalar;
            SimWide squared = vector.LengthSquared;
            return result.Length.Raw + vector.Normalized.X.Raw + wide.Normalized.X.Raw
                + SimVec3.DotFix(vector, vector).Raw + SimVec3.DistanceSquared(result, vector).Raw
                + (squared >= default(SimWide) && squared != default(SimWide) ? 1 : 0);
        }

        [Test]
        public void NewMathPathsAllocateZeroAfterWarmup()
        {
            long expected = MathCycle();
            for (int i = 0; i < 64; i++)
                MathCycle();
            AllocationMeter meter = DirectMeter();
            AllocationSample before = meter.Read();
            long total = 0;
            for (int i = 0; i < 4096; i++)
                total += MathCycle();
            AllocationSample sample = meter.Difference(before);
            Assert.That(total, Is.EqualTo(expected * 4096));
            AllocationAssertions.Zero(sample);
        }

        private static long Visit(EntityStore store, ComponentType<Health> health)
        {
            long sum = 0;
            foreach (EntityId entity in store.With(health))
                sum += store.Read(health, entity).Value;
            return sum;
        }

        [Test]
        public void ComponentViewUsesStructEnumerationWithoutWarmAllocations()
        {
            var store = new EntityStore(2000);
            ComponentType<Health> health = store.Register<Health>("health");
            for (int i = 0; i < 2000; i++)
            {
                EntityId id = store.Create();
                if (i % 2 == 0)
                    store.Set(health, id, new Health { Value = 1 });
            }
            store.CompleteWarmup();
            for (int i = 0; i < 64; i++)
                Visit(store, health);
            AllocationMeter meter = DirectMeter();
            AllocationSample before = meter.Read();
            long total = 0;
            for (int i = 0; i < 512; i++)
                total += Visit(store, health);
            AllocationSample sample = meter.Difference(before);
            Assert.That(total, Is.EqualTo(512000));
            AllocationAssertions.Zero(sample);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EveryTickAndTheEntirePublicHashPathAreStrictlyZero(bool v2)
        {
            var sim = new Simulation(123);
            sim.Register("load", new LoadSystem(sim, 2000));
            sim.CompleteWarmup();
            for (int i = 0; i < 32; i++)
            {
                sim.Step(default);
                sim.StateHash();
                sim.StateHashV2();
            }
            AllocationMeter meter = DirectMeter();
            var independent = new StateHash();
            long bytes = 0;
            for (int i = 0; i < 300; i++)
            {
                AllocationSample before = meter.Read();
                sim.Step(default);
                ulong actual = v2 ? sim.StateHashV2() : sim.StateHash();
                AllocationSample sample = meter.Difference(before);
                bytes += sample.Bytes;
                AllocationAssertions.Check(sample);
                independent.Reset();
                if (v2)
                    sim.WriteStateV2(independent);
                else
                    sim.WriteState(independent);
                Assert.That(actual, Is.EqualTo(independent.Value));
            }
            TestContext.WriteLine("S4 whole Step + public hash: v2={0}; ticks=300; entities=2000; basis=direct; bytes={1}; positive_control={2}",
                v2, bytes, meter.DirectControlDelta.Bytes);
            Assert.That(bytes, Is.Zero);
        }
    }
}
