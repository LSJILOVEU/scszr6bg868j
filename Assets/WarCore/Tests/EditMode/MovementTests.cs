using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class MovementTests
    {
        private sealed class WaterProbe : INavProvider
        {
            public int Calls;
            public string StateTypeId => "test.water";
            public int StateVersion => 1;
            public SimVec3 Advance(SimVec3 position, SimVec3 displacement, SimFix halfHeight)
            {
                Calls++;
                return SimVec3.Metres(10, 20, 30);
            }

            public void WriteState(StateHash hash) => hash.Add(Calls);
            public void SaveState(SimStateWriter writer) => writer.Int32(Calls);
            public void LoadState(SimStateReader reader)
            {
                Calls = reader.Int32();
            }
        }

        private static Simulation Create(int count, out ComponentType<Position> positions, out ComponentType<Movement> movements,
            out ComponentType<SimTier> tiers, bool tiered, INavProvider water = null)
        {
            var sim = new Simulation(123, config: new SimConfig(3, 16, 32, count));
            positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            movements = sim.Entities.Register<Movement>("movement", new MovementSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            tiers = sim.Entities.Register<SimTier>("tier", new SimTierSerializer());
            for (int i = 0; i < count; i++)
            {
                EntityId entity = sim.Entities.Create();
                sim.Entities.Set(positions, entity, new Position(SimVec3.Metres(0, 1, 0)));
                sim.Entities.Set(movements, entity, new Movement(MoveMode.Flying, SimVec3.Metres(30, -30, 60)));
                sim.Entities.Set(colliders, entity, new Collider(SimFix.Ratio(1, 2), SimFix.FromInt(2)));
                sim.Entities.Set(tiers, entity, i % 3 == 0 ? SimTier.Full : i % 3 == 1 ? SimTier.Reduced : SimTier.Dormant);
            }

            var nav = new NavProviderRegistry();
            nav.Register(MoveMode.Ground, new FlatGroundNavProvider(SimFix.FromInt(5)));
            if (water != null)
                nav.Register(MoveMode.Water, water);
            var system = new MovementSystem(sim.Entities, positions, movements, nav, 10, colliders);
            if (tiered)
                sim.RegisterTiered("movement", system, tiers, 3);
            else
                sim.Register("movement", system);
            return sim;
        }

        [Test]
        public void MovementDispatchesGroundAndWaterProvidersWhileFlyingAdvancesAllThreeAxes()
        {
            var water = new WaterProbe();
            Simulation sim = Create(3, out ComponentType<Position> positions, out ComponentType<Movement> movements, out _, false, water);
            sim.Entities.Set(movements, new EntityId(0, 1), new Movement(MoveMode.Ground, SimVec3.Metres(30, 900, -60)));
            sim.Entities.Set(movements, new EntityId(2, 1), new Movement(MoveMode.Water, default));
            sim.Step(default);
            Assert.That(sim.Entities.Get(positions, new EntityId(0, 1)).Value, Is.EqualTo(SimVec3.Metres(1, 6, -2)));
            Assert.That(sim.Entities.Get(positions, new EntityId(1, 1)).Value, Is.EqualTo(SimVec3.Metres(1, 0, 2)));
            Assert.That(sim.Entities.Get(positions, new EntityId(2, 1)).Value, Is.EqualTo(SimVec3.Metres(10, 20, 30)));
            Assert.That(water.Calls, Is.EqualTo(1));
            sim.Step(default);
            Assert.That(sim.Entities.Get(positions, new EntityId(1, 1)).Value, Is.EqualTo(SimVec3.Metres(2, -1, 4)));
            Assert.That(water.Calls, Is.EqualTo(2));
        }

        [Test]
        public void MovementAndProviderStateSurviveRestoreAndContinueWithIdenticalHashes()
        {
            var firstWater = new WaterProbe();
            var nextWater = new WaterProbe();
            Simulation first = Create(3, out _, out ComponentType<Movement> movements, out _, false, firstWater);
            Simulation restored = Create(3, out _, out _, out _, false, nextWater);
            first.Entities.Set(movements, new EntityId(0, 1), new Movement(MoveMode.Ground, SimVec3.Metres(30, 10, 60)));
            first.Entities.Set(movements, new EntityId(2, 1), new Movement(MoveMode.Water, default));
            for (int tick = 1; tick <= 5; tick++)
                first.Step(default);
            byte[] snapshot = first.SaveSnapshot();
            restored.RestoreSnapshot(snapshot);
            Assert.That(nextWater.Calls, Is.EqualTo(5));
            Assert.That(restored.SaveSnapshot(), Is.EqualTo(snapshot));
            for (int tick = 6; tick <= 30; tick++)
            {
                first.Step(default);
                restored.Step(default);
                Assert.That(restored.StateHash(), Is.EqualTo(first.StateHash()), "Movement tick " + tick);
            }
        }

        [Test]
        public void MissingWaterProviderFailsClearlyAndRegistrationFreezes()
        {
            Simulation sim = Create(1, out _, out ComponentType<Movement> movements, out _, false);
            sim.Entities.Set(movements, new EntityId(0, 1), new Movement(MoveMode.Water, default));
            Assert.That(Assert.Throws<InvalidOperationException>(() => sim.Step(default)).Message, Does.Contain("Water"));
            var nav = new NavProviderRegistry();
            var ground = new FlatGroundNavProvider();
            nav.Register(MoveMode.Ground, ground);
            Assert.That(nav.For(MoveMode.Ground), Is.SameAs(ground));
            Assert.Throws<InvalidOperationException>(() => nav.Register(MoveMode.Ground, ground));
            Assert.Throws<ArgumentOutOfRangeException>(() => nav.Register(MoveMode.Flying, ground));
            Assert.Throws<ArgumentOutOfRangeException>(() => nav.For((MoveMode)(-1)));
            var store = new EntityStore();
            var positions = store.Register<Position>("p");
            var movement = store.Register<Movement>("m");
            new MovementSystem(store, positions, movement, nav, 1);
            Assert.Throws<InvalidOperationException>(() => nav.Register(MoveMode.Water, new WaterProbe()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Movement((MoveMode)3, default));
        }

        [TestCase(long.MaxValue, 1)]
        [TestCase(long.MinValue, -1)]
        public void NavigationAdditionOverflowHasExplicitSimulationException(long position, long displacement)
        {
            var nav = new FlatGroundNavProvider();
            var from = new SimVec3(SimFix.FromRaw(position), default, default);
            var delta = new SimVec3(SimFix.FromRaw(displacement), default, default);
            Assert.Throws<SimBoundaryException>(() => nav.Advance(from, delta, default));
            var elevated = new FlatGroundNavProvider(SimFix.FromRaw(1));
            Assert.Throws<SimBoundaryException>(() => elevated.Advance(default, default, SimFix.FromRaw(long.MaxValue)));
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void ExtremeVelocityFailsWorldValidationWithoutArithmeticOverflow(long raw)
        {
            Simulation sim = Create(1, out _, out ComponentType<Movement> movement, out _, false);
            sim.Entities.Set(movement, new EntityId(0, 1), new Movement(MoveMode.Flying, new SimVec3(SimFix.FromRaw(raw), default, default)));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Step(default));
        }

        [Test]
        public void MovementModeAndVelocityAffectHashAndMissingComponentsAreSkipped()
        {
            Simulation sim = Create(1, out ComponentType<Position> positions, out ComponentType<Movement> movement, out _, false);
            EntityId entity = new EntityId(0, 1);
            ulong before = sim.StateHash();
            sim.Entities.Set(movement, entity, new Movement(MoveMode.Ground, SimVec3.Metres(30, -30, 60)));
            Assert.That(sim.StateHash(), Is.Not.EqualTo(before));
            before = sim.StateHash();
            sim.Entities.Set(movement, entity, new Movement(MoveMode.Ground, SimVec3.Metres(60, -30, 60)));
            Assert.That(sim.StateHash(), Is.Not.EqualTo(before));
            sim.Entities.Remove(positions, entity);
            Assert.DoesNotThrow(() => sim.Step(default));
            sim.Entities.Set(positions, entity, new Position());
            sim.Entities.Remove(movement, entity);
            sim.Step(default);
            Assert.That(sim.Entities.Get(positions, entity).Value, Is.EqualTo(default(SimVec3)));
        }

        [Test]
        public void TwoThousandTieredMoversRemainAllocationFreeAfterSnapshotRestore()
        {
            var windows = new AllocationWindow();
            do
            {
                Simulation sim = Create(2000, out ComponentType<Position> positions, out ComponentType<Movement> movement,
                    out _, true);
                foreach (EntityId entity in sim.Entities)
                    sim.Entities.Set(movement, entity, new Movement(entity.Id % 2 == 0 ? MoveMode.Ground : MoveMode.Flying,
                        SimVec3.Metres(1, 1, -1)));
                for (int tick = 0; tick < 16; tick++)
                    sim.Step(default);
                byte[] snapshot = sim.SaveSnapshot();
                sim.Step(default);
                sim.RestoreSnapshot(snapshot);
                var samples = new long[1000];
                var meter = new AllocationMeter();
                Stopwatch.GetTimestamp();
                windows.Begin(meter);
                for (int tick = 0; tick < samples.Length; tick++)
                {
                    long start = Stopwatch.GetTimestamp();
                    sim.Step(default);
                    samples[tick] = Stopwatch.GetTimestamp() - start;
                }

                AllocationSample bytes = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(bytes);
                Assert.That(sim.Events.EventsDroppedTotal, Is.Zero);
                Assert.That(sim.Entities.Get(positions, new EntityId(2, 1)).Value, Is.EqualTo(SimVec3.Metres(0, 1, 0)));
                Assert.That(sim.Entities.Get(positions, new EntityId(0, 1)).Value.X.Raw, Is.EqualTo(1016L * (SimFix.Scale / 30)));
                Array.Sort(samples);
                double scale = 1000000.0 / Stopwatch.Frequency;
                TestContext.WriteLine("2000 tiered movers after restore / 1000 ticks: {0}, EventsDropped=0; median={1:F3} us, p95={2:F3} us.",
                    bytes.Describe(), samples[500] * scale, samples[949] * scale);
            }
            while (windows.NeedsRetry);
        }
    }
}
