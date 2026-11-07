using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // M6 acceptance 2, 3, 4, 8 and 10 on the flat Interim world of InertiaWorlds: one AI unit at 6000 mm/s (cap 13107 raw),
    // ordered along +X at tick 1. In this world the k-th moving tick is tick k, and on a straight line along X the step a unit
    // travels in raw units is the step length the model returned (until the arrival tick, which may be shorter).
    public sealed class InertiaStepTests
    {
        private const long Cap = 13107;

        // Acceleration 9000 mm/s^2 = 655 raw per tick squared: 655 * k for k = 1..20, then the cap.
        private static readonly long[] Accelerating =
        {
            655, 1310, 1965, 2620, 3275, 3930, 4585, 5240, 5895, 6550, 7205, 7860, 8515, 9170, 9825, 10480, 11135, 11790, 12445, 13100,
            13107, 13107, 13107, 13107, 13107, 13107, 13107, 13107, 13107, 13107,
        };

        // Braking 18000 mm/s^2 = 1310 raw per tick squared over 10 m: the cap for 45 ticks, then floor(sqrt(2 * 1310 * r)).
        private static readonly long[] BrakingTail = { 13104, 11721, 10328, 8923, 7499, 6049, 4555, 2969, 1019 };

        private static SimSession World(long accel, long decel, long toXMm, long toZMm = InertiaWorlds.StartZMm, bool reverse = false) =>
            InertiaWorlds.Build(InertiaWorlds.Open(InertiaWorlds.Line(toXMm, toZMm), accel, decel), InertiaWorlds.FlatMap(), reverse);

        private static long X(SimSession s) => M3TestWorld.Position(s).X.Raw;

        [Test]
        public void Acceleration_RisesBy655PerTickUpToTheCap()
        {
            SimSession s = World(9000, 0, 25000);
            Assert.That(M3TestWorld.Component<MotionLimits>(s), Is.EqualTo(new MotionLimits(SimFix.FromRaw(Cap), SimFix.FromRaw(655), default, default)));
            Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(default, -1)));
            for (int k = 1; k <= Accelerating.Length; k++)
            {
                long before = X(s);
                s.StepOnce();
                Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(SimFix.FromRaw(Accelerating[k - 1]), k)), "moving tick " + k);
                Assert.That(X(s) - before, Is.EqualTo(Accelerating[k - 1]), "displacement, moving tick " + k);
            }
        }

        // A unit whose MaxSpeed is half of its own speed never steps further than that.
        [Test]
        public void MaxSpeed_CapsTheStepBelowTheWantedSpeed()
        {
            SimSession s = World(9000, 0, 25000);
            EntityStore entities = M3TestWorld.Sim(s).Entities;
            entities.Set(entities.Resolve<MotionLimits>(), M3TestWorld.Unit(s), new MotionLimits(SimFix.FromRaw(6553), SimFix.FromRaw(655), default, default));
            long[] expected = { 655, 1310, 1965, 2620, 3275, 3930, 4585, 5240, 5895, 6550, 6553, 6553, 6553, 6553, 6553 };
            for (int k = 1; k <= expected.Length; k++)
            {
                long before = X(s);
                s.StepOnce();
                Assert.That(InertiaWorlds.State(s).Speed.Raw, Is.EqualTo(expected[k - 1]), "moving tick " + k);
                Assert.That(X(s) - before, Is.EqualTo(expected[k - 1]), "displacement, moving tick " + k);
            }
        }

        [Test]
        public void Braking_SlowsTheLastMetreAndLandsOnTheTarget()
        {
            SimSession s = World(0, 18000, 15000);
            long target = SimFix.FromInt(15).Raw;
            for (int k = 1; k <= 54; k++)
            {
                long before = X(s);
                s.StepOnce();
                long expected = k <= 45 ? Cap : BrakingTail[k - 46];
                Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(SimFix.FromRaw(expected), k)), "moving tick " + k);
                if (k < 54) Assert.That(X(s) - before, Is.EqualTo(expected), "displacement, moving tick " + k);
                Assert.That(Wp23.Intent(s).Active, Is.EqualTo(k < 54), "moving tick " + k);
            }
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(new SimVec3(SimFix.FromRaw(target), default, SimFix.FromInt(12))));
            Assert.That(X(s) - target, Is.Zero);
        }

        [Test]
        public void AStopOfOneTick_StartsTheNextOrderFromRest()
        {
            SimSession s = World(9000, 0, 25000);
            InterimLocomotion locomotion = M3TestWorld.Locomotion(s);
            for (int k = 1; k <= 10; k++) s.StepOnce();
            Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(SimFix.FromRaw(6550), 10)));
            locomotion.Stop(M3TestWorld.Unit(s));
            long held = X(s);
            s.StepOnce();
            Assert.That(X(s), Is.EqualTo(held), "the stopped unit stands");
            Assert.That(InertiaWorlds.State(s).Tick, Is.EqualTo(10L), "the model is not asked while the unit stands");
            locomotion.Order(M3TestWorld.Unit(s), M3TestWorld.Point(25000, InertiaWorlds.StartZMm));
            s.StepOnce();
            Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(SimFix.FromRaw(655), 12)));
            Assert.That(X(s) - held, Is.EqualTo(655L));
        }

        // A unit that lacks its motion state or its motion limits moves at the speed it wanted, and nothing is written.
        [Test]
        public void AUnitWithoutState_MovesAtItsOwnSpeed()
        {
            SimSession s = World(9000, 18000, 25000);
            EntityStore entities = M3TestWorld.Sim(s).Entities;
            entities.Remove(entities.Resolve<MotionState>(), M3TestWorld.Unit(s));
            for (int k = 1; k <= 3; k++)
            {
                long before = X(s);
                s.StepOnce();
                Assert.That(X(s) - before, Is.EqualTo(Cap), "tick " + k);
            }
            Assert.That(entities.TryGet(entities.Resolve<MotionState>(), M3TestWorld.Unit(s), out _), Is.False);
        }

        [Test]
        public void AUnitWithoutLimits_MovesAtItsOwnSpeedAndKeepsItsState()
        {
            SimSession s = World(9000, 18000, 25000);
            EntityStore entities = M3TestWorld.Sim(s).Entities;
            entities.Remove(entities.Resolve<MotionLimits>(), M3TestWorld.Unit(s));
            for (int k = 1; k <= 3; k++)
            {
                long before = X(s);
                s.StepOnce();
                Assert.That(X(s) - before, Is.EqualTo(Cap), "tick " + k);
            }
            Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(default, -1)));
        }

        [Test]
        public void ASnapshotMidAcceleration_ResumesTheOriginalRun([Values(false, true)] bool reverseRestore)
        {
            var original = new List<ulong>();
            SimSession run = World(9000, 0, 25000);
            byte[] saved = null;
            for (int k = 1; k <= 40; k++)
            {
                run.StepOnce();
                original.Add(run.StateHash);
                if (k == 10) saved = M3TestWorld.Sim(run).SaveRetainedSnapshot();
            }
            SimSession resumed = World(9000, 0, 25000);
            M3TestWorld.Sim(resumed).RestoreRetainedSnapshot(saved, reverseRestore);
            Assert.That(resumed.Tick, Is.EqualTo(10L));
            Assert.That(InertiaWorlds.State(resumed), Is.EqualTo(new MotionState(SimFix.FromRaw(6550), 10)));
            Assert.That(resumed.StateHash, Is.EqualTo(original[9]));
            for (int k = 11; k <= 40; k++)
            {
                resumed.StepOnce();
                Assert.That(resumed.StateHash, Is.EqualTo(original[k - 1]), "tick " + k);
            }
            Assert.That(InertiaWorlds.State(resumed).Speed.Raw, Is.EqualTo(Cap));
        }

        [Test]
        public void ForwardAndReverseAllocation_HashTheSame()
        {
            SimSession forward = World(9000, 18000, 25000), reverse = World(9000, 18000, 25000, reverse: true);
            for (int k = 0; k <= 40; k++)
            {
                Assert.That(reverse.StateHash, Is.EqualTo(forward.StateHash), "tick " + k);
                forward.StepOnce(); reverse.StepOnce();
            }
        }

        [Test]
        public void TheMotionStateIsInTheHash()
        {
            SimSession a = World(9000, 0, 25000), b = World(9000, 0, 25000);
            EntityStore entities = M3TestWorld.Sim(b).Entities;
            ComponentType<MotionState> states = entities.Resolve<MotionState>();
            entities.Set(states, M3TestWorld.Unit(b), new MotionState(SimFix.FromRaw(1), -1));
            Assert.That(b.StateHash, Is.Not.EqualTo(a.StateHash), "speed");
            entities.Set(states, M3TestWorld.Unit(b), new MotionState(default, 0));
            Assert.That(b.StateHash, Is.Not.EqualTo(a.StateHash), "tick");
            entities.Set(states, M3TestWorld.Unit(b), MotionState.Initial);
            Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
        }

        // Braking at the 100 mm/s^2 floor (7 raw) still reaches the target in finite time, diagonally and along X.
        [TestCase(6000, 13000, 161), TestCase(15000, 12000, 430)]
        public void TheLowestBraking_StillArrives(long toXMm, long toZMm, long arrivalTick)
        {
            SimSession s = World(0, 100, toXMm, toZMm);
            SimVec3 target = M3TestWorld.Point(toXMm, toZMm);
            long arrived = -1;
            for (int i = 0; i < 600 && arrived < 0; i++)
            {
                s.StepOnce();
                if (M3TestWorld.Position(s).Equals(target)) arrived = s.Tick;
            }
            TestContext.Out.WriteLine($"arrived at tick {arrived}");
            Assert.That(arrived, Is.EqualTo(arrivalTick));
            Assert.That(Wp23.Intent(s).Active, Is.False);
        }
    }
}
