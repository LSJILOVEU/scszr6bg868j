using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S31")]
    public sealed class CachedManifestRegressionTests
    {
        [Test]
        public void TierManifestAndMovementHashRetainUtf8BytesWithoutSteadyAllocations()
        {
            var sim = new Simulation(1);
            var positions = sim.Entities.Register<Position>("position-\u00e9", new PositionSerializer());
            var movements = sim.Entities.Register<Movement>("movement", new MovementSerializer());
            var tiers = sim.Entities.Register<SimTier>("tier", new SimTierSerializer());
            var nav = new NavProviderRegistry();
            nav.Register(MoveMode.Ground, new FlatGroundNavProvider());
            var movement = new MovementSystem(sim.Entities, positions, movements, nav, 1);
            Type type = typeof(Simulation).Assembly.GetType("WarCore.Sim.TieredSystem", true);
            var tiered = (ISimStateSerializer)Activator.CreateInstance(type, movement, tiers, 3);
            string expected = "warcore.tiered/3/tier/" + movement.StateTypeId + "/elapsed-v2/1";
            var actualHash = new StateHash();
            var original = new StateHash();
            original.Add(movement.StateTypeId);
            nav.WriteState(original);
            movement.WriteState(actualHash);
            Assert.That(actualHash.Value, Is.EqualTo(original.Value));
            var rng = sim.Rng.Stream("warm");
            for (int i = 0; i < 32; i++)
            {
                _ = tiered.StateTypeId;
                ((IStateHash)tiered).WriteState(actualHash);
                movement.WriteState(actualHash);
                sim.Rng.NextInt(rng, 7);
            }
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero needs a passing direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            bool same = true;
            AllocationSample before = meter.Read();
            for (int i = 0; i < 1000; i++)
            {
                same &= tiered.StateTypeId == expected;
                actualHash.Reset();
                movement.WriteState(actualHash);
                ((IStateHash)tiered).WriteState(actualHash);
                sim.Rng.NextInt(rng, 7);
            }
            AllocationSample sample = meter.Difference(before);
            TestContext.WriteLine("S31 manifest/movement/tier/RNG: control_bytes={0}; {1}", meter.DirectControlDelta.Bytes, sample.Describe());
            Assert.That(same, Is.True);
            AllocationAssertions.Zero(sample);
        }
    }
}
