using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S36")]
    public sealed class ElapsedMovementRegressionTests
    {
        internal sealed class WallNav : INavProvider
        {
            public string StateTypeId => "test.thin-wall";
            public int StateVersion => 1;
            public SimVec3 Advance(SimVec3 position, SimVec3 displacement, SimFix halfHeight)
            {
                SimVec3 next = position + displacement;
                // Endpoint-only checks expose an incorrect single large displacement through the thin wall.
                return next.X >= SimFix.FromInt(2) && next.X <= SimFix.FromInt(3) ? position : next;
            }
            public void WriteState(StateHash hash) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
        }

        internal sealed class Scene
        {
            internal readonly Simulation Sim;
            internal readonly ComponentType<Position> Positions;
            internal readonly ComponentType<Movement> Movements;
            internal readonly ComponentType<SimTier> Tiers;
            internal Scene(int period, SimTier tier, bool wall = false, bool spatial = false)
            {
                Sim = new Simulation(81, config: new SimConfig(2, 4, 4, period));
                Positions = Sim.Entities.Register<Position>("position", new PositionSerializer());
                Movements = Sim.Entities.Register<Movement>("movement", new MovementSerializer());
                Tiers = Sim.Entities.Register<SimTier>("tier", new SimTierSerializer());
                for (int id = 0; id < period; id++)
                {
                    EntityId entity = Sim.Entities.Create();
                    Sim.Entities.Set(Positions, entity, new Position());
                    Sim.Entities.Set(Movements, entity, new Movement(wall ? MoveMode.Ground : MoveMode.Flying,
                        wall ? SimVec3.Metres(30, 0, 0) : new SimVec3(SimFix.FromRaw(61), SimFix.FromRaw(-31), SimFix.FromRaw(37))));
                    Sim.Entities.Set(Tiers, entity, tier);
                }
                if (spatial)
                {
                    var colliders = Sim.Entities.Register<Collider>("collider", new ColliderSerializer());
                    foreach (EntityId entity in Sim.Entities)
                        Sim.Entities.Set(colliders, entity, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(2)));
                    Sim.ConfigureSpatial(Positions, colliders, SimFix.FromInt(2), period * 32);
                }
                var nav = new NavProviderRegistry();
                if (wall)
                    nav.Register(MoveMode.Ground, new WallNav());
                Sim.RegisterTiered("move", new MovementSystem(Sim.Entities, Positions, Movements, nav, 1), Tiers, period);
            }
            internal void Tier(SimTier tier)
            {
                foreach (EntityId entity in Sim.Entities)
                    Sim.Entities.Set(Tiers, entity, tier);
            }
            internal SimVec3 Position(int id) => Sim.Entities.Get(Positions, new EntityId(id, 1)).Value;
        }

        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(7, false)]
        [TestCase(3, true)]
        [TestCase(7, true)]
        public void EveryPhaseEqualsFullAtCycleEndWithIdenticalTruncationAndWallChecks(int period, bool wall)
        {
            var full = new Scene(period, SimTier.Full, wall);
            var reduced = new Scene(period, SimTier.Reduced, wall);
            for (int tick = 1; tick <= period * 8; tick++)
            {
                full.Sim.Step(default);
                reduced.Sim.Step(default);
                if (tick % period == 0)
                    for (int id = 0; id < period; id++)
                        Assert.That(reduced.Position(id), Is.EqualTo(full.Position(id)), "tick=" + tick + "; id=" + id);
            }
            Assert.That(full.Position(0).X.Raw, Is.EqualTo(wall ? SimFix.Scale : period * 8L * 2));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void WakeAndTierSwitchCatchUpAndRestoreEveryPhase(int offset)
        {
            var full = new Scene(3, SimTier.Full);
            var online = new Scene(3, SimTier.Reduced);
            var replay = new Scene(3, SimTier.Reduced);
            for (int tick = 1; tick <= 4 + offset; tick++)
            {
                full.Sim.Step(default);
                online.Sim.Step(default);
                replay.Sim.Step(default);
            }
            online.Tier(SimTier.Dormant);
            replay.Tier(SimTier.Dormant);
            for (int tick = 0; tick < 5; tick++)
            {
                full.Sim.Step(default);
                online.Sim.Step(default);
                replay.Sim.Step(default);
            }
            byte[] snapshot = replay.Sim.SaveSnapshot();
            replay.Tier(SimTier.Full);
            replay.Sim.Step(default);
            replay.Sim.RestoreSnapshot(snapshot);
            Assert.That(replay.Sim.StateHash(), Is.EqualTo(online.Sim.StateHash()));
            online.Tier(SimTier.Reduced);
            replay.Tier(SimTier.Reduced);
            full.Sim.Step(default);
            online.Sim.Step(default);
            replay.Sim.Step(default);
            for (int id = 0; id < 3; id++)
                Assert.That(replay.Position(id), Is.EqualTo(full.Position(id)), "wake id=" + id);
            for (int tick = 0; tick < 12; tick++)
            {
                if (tick == 2 || tick == 5)
                {
                    online.Tier(tick == 2 ? SimTier.Full : SimTier.Reduced);
                    replay.Tier(tick == 2 ? SimTier.Full : SimTier.Reduced);
                }
                full.Sim.Step(default);
                online.Sim.Step(default);
                replay.Sim.Step(default);
                Assert.That(replay.Sim.StateHash(), Is.EqualTo(online.Sim.StateHash()));
                if (online.Sim.Clock.Tick % 3 == 0 || tick == 2)
                    for (int id = 0; id < 3; id++)
                        Assert.That(replay.Position(id), Is.EqualTo(full.Position(id)));
            }
        }

        [Test]
        public void SleepingFromCreationCatchesUpOnFirstWake()
        {
            var full = new Scene(3, SimTier.Full);
            var dormant = new Scene(3, SimTier.Dormant);
            for (int tick = 0; tick < 8; tick++)
            {
                full.Sim.Step(default);
                dormant.Sim.Step(default);
                Assert.That(dormant.Position(1), Is.EqualTo(default(SimVec3)));
            }
            dormant.Tier(SimTier.Full);
            full.Sim.Step(default);
            dormant.Sim.Step(default);
            for (int id = 0; id < 3; id++)
                Assert.That(dormant.Position(id), Is.EqualTo(full.Position(id)));
        }
        [Test]
        public void DeferredTimeAffectsHashEvenWhenComponentsMatch()
        {
            var sleeping = new Scene(3, SimTier.Dormant);
            var active = new Scene(3, SimTier.Full);
            for (int i = 0; i < 2; i++)
            {
                sleeping.Sim.Step(default);
                active.Sim.Step(default);
            }
            active.Tier(SimTier.Dormant);
            foreach (EntityId entity in active.Sim.Entities)
                active.Sim.Entities.Set(active.Positions, entity, new Position());
            // Align the prior tier too, leaving only the deferred timestamp different.
            sleeping.Sim.Step(default);
            active.Sim.Step(default);
            Assert.That(Fixtures.Hash(active.Sim.Entities), Is.EqualTo(Fixtures.Hash(sleeping.Sim.Entities)));
            Assert.That(active.Sim.StateHash(), Is.Not.EqualTo(sleeping.Sim.StateHash()));
        }

        [Test]
        public void NewGenerationDoesNotInheritDormantTime()
        {
            var scene = new Scene(3, SimTier.Dormant);
            for (int i = 0; i < 8; i++)
                scene.Sim.Step(default);
            scene.Sim.Entities.Destroy(new EntityId(0, 1));
            scene.Sim.CompleteSpatialBoundary();
            EntityId replacement = scene.Sim.Entities.Create();
            scene.Sim.Entities.Set(scene.Positions, replacement, new Position());
            scene.Sim.Entities.Set(scene.Movements, replacement, new Movement(MoveMode.Flying, SimVec3.Metres(30, 0, 0)));
            scene.Sim.Entities.Set(scene.Tiers, replacement, SimTier.Reduced);
            scene.Sim.Step(default);
            Assert.That(scene.Sim.Entities.Get(scene.Positions, replacement).Value.X, Is.EqualTo(SimFix.FromInt(1)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoredElapsedMovementAndHashAllocateZero(bool wall)
        {
            var scene = new Scene(7, SimTier.Reduced, wall);
            for (int i = 0; i < 21; i++)
                scene.Sim.Step(default);
            scene.Sim.RestoreSnapshot(scene.Sim.SaveSnapshot());
            for (int i = 0; i < 21; i++)
            {
                scene.Sim.Step(default);
                scene.Sim.StateHash();
            }
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero needs the direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            AllocationSample before = meter.Read();
            for (int i = 0; i < 300; i++)
            {
                scene.Sim.Step(default);
                scene.Sim.StateHash();
            }
            AllocationSample sample = meter.Difference(before);
            TestContext.WriteLine("S36 wall={0}; control_bytes={1}; {2}", wall, meter.DirectControlDelta.Bytes, sample.Describe());
            AllocationAssertions.Zero(sample);
        }
        [Test]
        public void BufferedMovementSubstepsReadTheirOwnIntermediatePosition()
        {
            var full = new Scene(7, SimTier.Full, true, true);
            var reduced = new Scene(7, SimTier.Reduced, true, true);
            for (int tick = 1; tick <= 28; tick++)
            {
                full.Sim.Step(default);
                reduced.Sim.Step(default);
                if (tick % 7 == 0)
                    for (int id = 0; id < 7; id++)
                        Assert.That(reduced.Position(id), Is.EqualTo(full.Position(id)));
            }
            Assert.That(reduced.Position(0).X, Is.EqualTo(SimFix.FromInt(1)));
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        public void NonpositiveElapsedTimeFailsBeforeAnyIntegration(long ticks)
        {
            var scene = new Scene(1, SimTier.Full);
            var movement = new MovementSystem(scene.Sim.Entities, scene.Positions, scene.Movements, new NavProviderRegistry(), 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => movement.StepEntity(scene.Sim.Context, new EntityId(0, 1), ticks));
            Assert.That(scene.Position(0), Is.EqualTo(default(SimVec3)));
        }
        private sealed class VersionedElapsed : ISimElapsedEntitySystem, ISimStateSerializer
        {
            public int Order => 1;
            public string StateTypeId => "test.versioned-elapsed";
            public int StateVersion { get; }
            internal VersionedElapsed(int version) { StateVersion = version; }
            public void StepEntity(SimContext context, EntityId entity) { }
            public void StepEntity(SimContext context, EntityId entity, long ticks) { }
            public void WriteState(StateHash hash) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
        }

        [Test]
        public void ElapsedWrapperStillRejectsDifferentInnerSerializerVersion()
        {
            var source = new Simulation(1);
            var target = new Simulation(1);
            var sourceTiers = source.Entities.Register<SimTier>("tier", new SimTierSerializer());
            var targetTiers = target.Entities.Register<SimTier>("tier", new SimTierSerializer());
            source.RegisterTiered("elapsed", new VersionedElapsed(1), sourceTiers, 3);
            target.RegisterTiered("elapsed", new VersionedElapsed(2), targetTiers, 3);
            Assert.Throws<SimSnapshotException>(() => target.RestoreSnapshot(source.SaveSnapshot()));
        }
    }
}
