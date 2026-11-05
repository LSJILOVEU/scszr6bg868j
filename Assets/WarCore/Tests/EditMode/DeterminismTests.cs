using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class DeterminismTests
    {
        [Test]
        public void ThousandTickReplayMatchesAtEveryTickAndDifferentSeedDiverges()
        {
            var first = Fixtures.Create(123);
            var replay = Fixtures.Create(123);
            var other = Fixtures.Create(124);
            var script = new InputFrame[1000];
            for (int i = 0; i < script.Length; i++)
                script[i] = Fixtures.Input(i + 1);
            foreach (InputFrame input in script)
            {
                first.Step(input);
                replay.Step(input);
                other.Step(input);
                Assert.That(replay.StateHash(), Is.EqualTo(first.StateHash()), "Tick " + first.Clock.Tick);
            }

            Assert.That(first.Clock.Tick, Is.EqualTo(1000));
            Assert.That(other.StateHash(), Is.Not.EqualTo(first.StateHash()));
            Assert.That(other.Entities.Get<Health>(new EntityId(0, 1)).Value, Is.Not.EqualTo(first.Entities.Get<Health>(new EntityId(0, 1)).Value));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void RenderRateAndCameraRandomDoNotChangeSimulation(int fps)
        {
            var expected = Fixtures.Create(12);
            var actual = Fixtures.Create(12);
            var accumulator = new TickAccumulator(720);
            var visual = new PresentationRandom(fps);
            double cameraAngle = 0;
            for (int frame = 0; frame < fps * 40; frame++)
            {
                cameraAngle += visual.NextUnit() * 360;
                long due = accumulator.AddElapsed(720 / fps);
                for (long i = 0; i < due; i++)
                    actual.Step(Fixtures.Input(actual.Clock.Tick + 1));
            }

            for (int i = 1; i <= 1200; i++)
                expected.Step(Fixtures.Input(i));
            Assert.That(cameraAngle, Is.GreaterThan(0));
            Assert.That(actual.Clock.Tick, Is.EqualTo(1200));
            Assert.That(actual.StateHash(), Is.EqualTo(expected.StateHash()));
        }

        [Test]
        public void AccumulatorRetainsFractionsAndDoesNotDropLongFrames()
        {
            var accumulator = new TickAccumulator(1000);
            Assert.That(accumulator.AddElapsed(33), Is.Zero);
            Assert.That(accumulator.AddElapsed(1), Is.EqualTo(1));
            Assert.That(accumulator.AddElapsed(966), Is.EqualTo(29));
            Assert.That(accumulator.AddElapsed(10000), Is.EqualTo(300));
            Assert.Throws<ArgumentOutOfRangeException>(() => accumulator.AddElapsed(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickAccumulator(0));
        }

        [Test]
        public void NamedStreamsIgnoreCreationOrderAndUnrelatedConsumption()
        {
            var a = new SimRandom(42);
            var b = new SimRandom(42);
            b.NextUInt64(b.Stream("ai"));
            for (int i = 0; i < 100; i++)
            {
                b.NextUInt64(b.Stream("extra"));
                Assert.That(a.NextUInt64(a.Stream("combat")), Is.EqualTo(b.NextUInt64(b.Stream("combat"))));
            }

            var c = new SimRandom(42);
            var d = new SimRandom(42);
            c.Stream("z");
            c.Stream("a");
            d.Stream("a");
            d.Stream("z");
            Assert.That(Fixtures.Hash(c), Is.EqualTo(Fixtures.Hash(d)));
            Assert.That(c.NextUInt64(c.Stream("a")), Is.Not.EqualTo(c.NextUInt64(c.Stream("z"))));
        }

        [Test]
        public void SplitMixHasKnownVectorAndBoundedSampling()
        {
            // Access through the name-derived seed fixes the complete public RNG protocol.
            var random = new SimRandom(42);
            var stream = random.Stream("combat");
            Assert.That(random.NextUInt64(stream), Is.EqualTo(4447859020387897724UL));
            for (int i = 0; i < 1000; i++)
                Assert.That(random.NextInt(stream, 7), Is.InRange(0, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(stream, 0));
        }

        [Test]
        public void StableHashUsesKnownLittleEndianUtf8Vector()
        {
            var hash = new StateHash();
            hash.Add(1);
            hash.Add("abc");
            hash.Add(true);
            Assert.That(hash.Value, Is.EqualTo(2663804496378673336UL));
        }

        private sealed class OrderedSystem : ISimSystem
        {
            public int Order
            {
                get;
            }

            public OrderedSystem(int order)
            {
                Order = order;
            }

            public void Step(SimContext context)
            {
                context.Emit(new SimEvent(Fixtures.Type("order"), tag: Order));
            }

            public void WriteState(StateHash hash)
            {
                hash.Add(Order);
            }
        }

        [Test]
        public void SystemsExecuteSortedAndRejectDuplicateOrderOrIdAndLateRegistration()
        {
            var sim = Fixtures.Empty(1);
            sim.Register("last", new OrderedSystem(20));
            sim.Register("first", new OrderedSystem(-10));
            sim.Register("middle", new OrderedSystem(0));
            Assert.Throws<InvalidOperationException>(() => sim.Register("duplicate", new OrderedSystem(0)));
            Assert.Throws<InvalidOperationException>(() => sim.Register("middle", new OrderedSystem(1)));
            sim.Step(default);
            var values = new List<int>();
            foreach (var item in sim.Events.Read(1))
                values.Add(item.Tag);
            Assert.That(values, Is.EqualTo(new[]{-10, 0, 20}));
            Assert.Throws<InvalidOperationException>(() => sim.Register("late", new OrderedSystem(30)));
        }

        [Test]
        public void InputSystemAppliesAllCommandsAndPulseSystemEmitsOnSeventhTick()
        {
            var sim = Fixtures.Create(9);
            var entity = new EntityId(0, 1);
            var input = new InputFrame(SimVec3.Metres(2, -3, 0), SimVec3.Metres(0, 1, 0), true, true, true, 4, new SquadCommand(SquadCommandKind.Hold, 7, default, entity));
            var oracle = new SimRandom(9);
            int damage = oracle.NextInt(oracle.Stream("combat"), 3);
            sim.Step(input);
            Assert.That(sim.Entities.Get<Position>(entity).Value.X.Raw / SimFix.Scale, Is.EqualTo(2));
            Assert.That(sim.Entities.Get<Position>(entity).Value.Y.Raw / SimFix.Scale, Is.EqualTo(-3));
            Assert.That(sim.Entities.Get<Facing>(entity).Direction.Y.Raw / SimFix.Scale, Is.EqualTo(1));
            Assert.That(sim.Entities.Get<Health>(entity).Value, Is.EqualTo(10000 - damage));
            Assert.That(sim.Events.Read(1).Length, Is.EqualTo(4));
            Assert.That(sim.Events.Read(1)[2].Tag, Is.EqualTo(4));
            Assert.That(sim.Events.Read(1)[3].Tag, Is.EqualTo((int)SquadCommandKind.Hold));
            for (int i = 2; i <= 7; i++)
                sim.Step(default);
            Assert.That(sim.Events.Read(4)[0].Type.Value, Is.EqualTo(Fixtures.Throw));
            Assert.That(sim.Events.Read(7)[0].Type.Value, Is.EqualTo(Fixtures.Pulse));
        }
    }
}
