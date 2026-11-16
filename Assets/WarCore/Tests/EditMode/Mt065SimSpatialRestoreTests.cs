using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class Mt065SimSpatialRestoreTests : ISimSystem, ISimWarmupSystem, IStableStateSerializer
    {
        private Action onWarmup;
        public int Order => 1;
        public string StateTypeId => "test.spatial-warmup";
        public int StateVersion => 1;
        public void PrepareWarmup() => onWarmup?.Invoke();
        public void Step(SimContext context) { }
        public void WriteState(StateHash hash) { }
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }

        [Test]
        public void StartedRestoreRejectsSpatialConfigurationWithoutPublishingAQuery()
        {
            onWarmup = null;
            var sim = new Simulation(1, config: new SimConfig(entityCapacity: 1),
                identityMode: IdentityMode.StableWorld);
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            sim.Register("warmup", this);
            sim.Step(default);
            byte[] snapshot = sim.SaveStableSnapshot();

            int attempts = 0;
            onWarmup = () =>
            {
                attempts++;
                Assert.Throws<InvalidOperationException>(() =>
                    sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(8)));
            };
            sim.RestoreStableSnapshot(snapshot);

            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(sim.SpatialQuery, Is.Null);
            Assert.That(sim.Context.SpatialQuery, Is.Null);
        }
    }
}
