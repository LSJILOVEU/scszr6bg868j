using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class TierCounter : ISimEntitySystem, ISimStateSerializer
    {
        private readonly ComponentType<Health> health;
        public int Order => 10;
        public string StateTypeId => "test.tier-counter";
        public int StateVersion => 1;
        internal TierCounter(ComponentType<Health> health)
        {
            this.health = health;
        }

        public void StepEntity(SimContext context, EntityId entity)
        {
            context.Entities.Get(health, entity).Value++;
        }

        public void WriteState(StateHash hash)
        {
        }

        public void SaveState(SimStateWriter writer)
        {
        }

        public void LoadState(SimStateReader reader)
        {
        }
    }

    internal sealed class GlobalTickProbe : ISimSystem, ISimStateSerializer
    {
        public int Ticks;
        public int Commands;
        public int Timers;
        public int Order => 0;
        public string StateTypeId => "test.global-probe";
        public int StateVersion => 1;
        public void Step(SimContext context)
        {
            Ticks++;
            if (context.Input.PrimaryAttack)
                Commands++;
            while (context.Scheduler.TryDequeue(out _))
                Timers++;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Ticks);
            hash.Add(Commands);
            hash.Add(Timers);
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(Ticks);
            writer.Int32(Commands);
            writer.Int32(Timers);
        }

        public void LoadState(SimStateReader reader)
        {
            Ticks = reader.Int32();
            Commands = reader.Int32();
            Timers = reader.Int32();
        }
    }

    public sealed class TieredSystemTests
    {
        private static Simulation Create(int period, out ComponentType<SimTier> tiers, out ComponentType<Health> health,
            out GlobalTickProbe global, out SnapshotMode mode)
        {
            mode = new SnapshotMode();
            var sim = new Simulation(1, mode, "mode", config: new SimConfig(3, 4, 4, 6));
            tiers = sim.Entities.Register<SimTier>("tiers", new SimTierSerializer());
            health = sim.Entities.Register<Health>("counts", new HealthSerializer());
            for (int i = 0; i < 6; i++)
            {
                EntityId entity = sim.Entities.Create();
                sim.Entities.Set(health, entity, new Health());
                if (i != 5)
                    sim.Entities.Set(tiers, entity, i < 3 ? SimTier.Reduced : i == 3 ? SimTier.Full : SimTier.Dormant);
            }

            global = new GlobalTickProbe();
            sim.Register("global", global);
            sim.RegisterTiered("entities", new TierCounter(health), tiers, period);
            return sim;
        }

        [Test]
        public void EveryThreeTicksSpreadsEntityPhasesAndLeavesGlobalClockTimersAndCommandsActive()
        {
            Simulation sim = Create(3, out _, out ComponentType<Health> health, out GlobalTickProbe global, out SnapshotMode mode);
            var previous = new int[6];
            var last = new long[3];
            for (int tick = 1; tick <= 12; tick++)
            {
                sim.Scheduler.Schedule(tick, new SimEvent(new EventTypeId(81)));
                sim.Step(new InputFrame(default, default, primaryAttack: true));
                for (int id = 0; id < 6; id++)
                {
                    int value = sim.Entities.Get(health, new EntityId(id, 1)).Value;
                    bool executes = id < 3 ? tick % 3 == id % 3 : id != 4;
                    Assert.That(value - previous[id], Is.EqualTo(executes ? 1 : 0), "Entity " + id + ", tick " + tick);
                    if (id < 3 && executes)
                    {
                        if (last[id] != 0)
                            Assert.That(tick - last[id], Is.EqualTo(3));
                        last[id] = tick;
                    }

                    previous[id] = value;
                }
            }

            Assert.That(previous, Is.EqualTo(new[] { 4, 4, 4, 12, 0, 12 }));
            Assert.That(global.Ticks, Is.EqualTo(12));
            Assert.That(global.Commands, Is.EqualTo(12));
            Assert.That(global.Timers, Is.EqualTo(12));
            Assert.That(mode.Steps, Is.EqualTo(12));
            Assert.That(sim.Clock.Tick, Is.EqualTo(12));
        }

        [Test]
        public void TierSwitchesRetainAbsolutePhaseAndDormantComponentsStillAffectHash()
        {
            Simulation sim = Create(3, out ComponentType<SimTier> tiers, out ComponentType<Health> health, out _, out _);
            EntityId entity = new EntityId(0, 1);
            sim.Step(default);
            sim.Step(default);
            sim.Step(default);
            Assert.That(sim.Entities.Get(health, entity).Value, Is.EqualTo(1));
            sim.Entities.Set(tiers, entity, SimTier.Full);
            sim.Step(default);
            Assert.That(sim.Entities.Get(health, entity).Value, Is.EqualTo(2));
            ulong fullHash = sim.StateHash();
            sim.Entities.Set(tiers, entity, SimTier.Dormant);
            Assert.That(sim.StateHash(), Is.Not.EqualTo(fullHash));
            sim.Step(default);
            Assert.That(sim.Entities.Get(health, entity).Value, Is.EqualTo(2));
            sim.Entities.Set(tiers, entity, SimTier.Reduced);
            sim.Step(default);
            Assert.That(sim.Entities.Get(health, entity).Value, Is.EqualTo(3));
            sim.Step(default);
            Assert.That(sim.Entities.Get(health, entity).Value, Is.EqualTo(3));
        }

        [Test]
        public void SnapshotRestoresTierSystemStateAndPhaseWithoutResettingTheCycle()
        {
            Simulation baseline = Create(3, out ComponentType<SimTier> aTiers, out _, out _, out _);
            Simulation actual = Create(3, out ComponentType<SimTier> bTiers, out _, out _, out _);
            for (int tick = 1; tick <= 4; tick++)
            {
                baseline.Step(default);
                actual.Step(default);
            }

            byte[] snapshot = actual.SaveSnapshot();
            for (int tick = 5; tick <= 10; tick++)
                actual.Step(default);
            actual.RestoreSnapshot(snapshot);
            for (int tick = 5; tick <= 16; tick++)
            {
                if (tick == 7 || tick == 8)
                {
                    SimTier tier = tick == 7 ? SimTier.Dormant : SimTier.Reduced;
                    baseline.Entities.Set(aTiers, new EntityId(1, 1), tier);
                    actual.Entities.Set(bTiers, new EntityId(1, 1), tier);
                }

                baseline.Step(default);
                actual.Step(default);
                Assert.That(actual.StateHash(), Is.EqualTo(baseline.StateHash()), "Tier continuation " + tick);
            }
        }

        [TestCase(1)]
        [TestCase(int.MaxValue)]
        public void PositivePeriodBoundariesUseModuloWithoutOverflow(int period)
        {
            Simulation sim = Create(period, out _, out ComponentType<Health> health, out _, out _);
            for (int tick = 1; tick <= 4; tick++)
                sim.Step(default);
            Assert.That(sim.Entities.Get(health, new EntityId(0, 1)).Value, Is.EqualTo(period == 1 ? 4 : 0));
            Assert.That(sim.Entities.Get(health, new EntityId(1, 1)).Value, Is.EqualTo(period == 1 ? 4 : 1));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NonpositivePeriodFailsBeforeModulo(int period)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(period, out _, out _, out _, out _));
        }

        [Test]
        public void DifferentPeriodIsHashedAndRejectedBySnapshotManifest()
        {
            Simulation a = Create(3, out _, out _, out _, out _);
            Simulation b = Create(4, out _, out _, out _, out _);
            Assert.That(a.StateHash(), Is.Not.EqualTo(b.StateHash()));
            Assert.That(Assert.Throws<SimSnapshotException>(() => b.RestoreSnapshot(a.SaveSnapshot())).Message, Does.Contain("manifest"));
            var foreign = new EntityStore().Register<SimTier>("tier");
            Assert.Throws<ArgumentException>(() => a.RegisterTiered("foreign", new TierCounter(a.Entities.Resolve<Health>()), foreign, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimTier((SimTierLevel)3));
        }
    }
}
