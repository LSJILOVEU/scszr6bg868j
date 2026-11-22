using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Stance
{
    // STANCE-1 S8: a stance scales a unit's speed through the speed line (StatLine.Scale, at least 1 raw), before inertia, for
    // ordered units and the player alike.
    public sealed class Stance1MoveTests
    {
        // One unarmed unit (key 1, squad 1) at (5, 8) m, ordered at tick 1 along +x to (35, 8) m. The straight mover (Interim)
        // walks the exact line, so every step's x is the step length; the crowd mover has its own check below.
        private static SimSession Walker(long speedMmPerSec = 6000, Func<WorldRecipe, WorldRecipe> open = null, MoverKind mover = MoverKind.Interim) =>
            World(Pack(), new[] { Soldier(1, 1, 5000, 8000, 1, -1, speedMmPerSec) }, Map(), new[] { BridgeCommand.MoveTo(1, 1, 35000, 8000) }, open, mover);

        private static long Own(SimSession s) => M3TestWorld.Component<MoveIntent>(s, 1).SpeedPerTick.Raw;
        private static long X(SimSession s) => M3TestWorld.Position(s, 1).X.Raw;
        private static long Scaled(long own, int permille) => StatLine.Scale(own, new[] { permille });

        // The x displacement of each of the next n steps; between runs before each step.
        private static List<long> Steps(SimSession s, int n, Action<SimSession> between = null)
        {
            var list = new List<long>();
            for (int i = 0; i < n; i++)
            {
                between?.Invoke(s);
                long x = X(s);
                s.StepOnce();
                list.Add(X(s) - x);
            }
            return list;
        }

        [TestCase(0, 1000)]
        [TestCase(1, 500)]
        [TestCase(2, 250)]
        public void S8_OrderedUnit_StepsTheScaledSpeed(int level, int permille)
        {
            SimSession s = Walker();
            if (level != 0) Force(s, 1, level);
            s.StepOnce();
            long own = Own(s);
            Assert.That(own, Is.EqualTo(13107L));
            Assert.That(Steps(s, 10), Is.All.EqualTo(Scaled(own, permille)));
        }

        // The crowd mover (its slot puts the target off the straight line): each step's length is the scaled speed, to the
        // rounding of a step along a slanted direction (1 raw).
        [TestCase(0, 1000)]
        [TestCase(1, 500)]
        [TestCase(2, 250)]
        public void S8_CrowdUnit_StepsTheScaledSpeed(int level, int permille)
        {
            SimSession s = Walker(mover: MoverKind.InterimCrowd);
            if (level != 0) Force(s, 1, level);
            s.StepOnce();
            long expected = Scaled(Own(s), permille);
            for (int i = 0; i < 10; i++)
            {
                SimVec3 a = M3TestWorld.Position(s, 1);
                s.StepOnce();
                SimVec3 d = M3TestWorld.Position(s, 1) - a;
                long length = (long)Math.Round(Math.Sqrt((double)d.X.Raw * d.X.Raw + (double)d.Z.Raw * d.Z.Raw));
                Assert.That(length, Is.InRange(expected - 1, expected + 1), "step " + i);
            }
        }

        // Stand to crouch asked between ticks 5 and 6: due at 12; ticks 6..11 already step the crouch speed. Crouch to stand asked
        // between 20 and 21: due at 27; ticks 21..26 still step the crouch speed, tick 27 the standing one.
        [Test]
        public void S8_WhileChanging_TheSlowerSpeed()
        {
            SimSession s = Walker();
            List<long> steps = Steps(s, 30, w =>
            {
                if (w.Tick == 5) Assert.That(w.TryStance(1, 1), Is.True);
                if (w.Tick == 20) Assert.That(w.TryStance(1, 0), Is.True);
            });
            long stand = Own(s), crouch = Scaled(stand, 500);
            for (int tick = 2; tick <= 30; tick++)
            {
                long expected = tick <= 5 || tick >= 27 ? stand : crouch;
                Assert.That(steps[tick - 1], Is.EqualTo(expected), "tick " + tick);
            }
        }

        // 1 mm/s is 2 raw per tick; prone (250) scales it to 0, which the line lifts to 1.
        [Test]
        public void S8_ASlowUnitProneStillMoves()
        {
            SimSession s = Walker(1);
            Force(s, 1, 2);
            s.StepOnce();
            Assert.That(Own(s), Is.EqualTo(2L));
            Assert.That(Steps(s, 10), Is.All.EqualTo(1L));
        }

        // The player's unit (no motion model) steps the same scaled speed with the stick full right.
        [TestCase(0, 1000)]
        [TestCase(1, 500)]
        [TestCase(2, 250)]
        public void S8_Player_StepsTheScaledSpeed(int level, int permille)
        {
            SimSession s = World(Pack(), new[] { Soldier(1, 1, 5000, 8000, 1, -1) }, Map(), open: Player(1));
            if (level != 0) Force(s, 1, level);
            s.SetPlayerInput(1, new PlayerInput(1000, 0));
            s.StepOnce();
            Assert.That(Steps(s, 10), Is.All.EqualTo(Scaled(Own(s), permille)));
        }

        // Inertia (acceleration 137 000 mm/s^2, no braking): the first step from rest of a crouched unit is the inertia model's
        // answer to the crouch speed (6553 = min(6553, accel)), not the standing speed through inertia then scaled
        // (4987 or 4988 = Scale(min(13107, accel), 500)). Standing, the first step is the acceleration itself.
        [Test]
        public void S8_TheGearActsBeforeInertia()
        {
            Func<WorldRecipe, WorldRecipe> open = r => r.WithMotion().With(new InertiaOption(137000, 0));
            SimSession crouched = Walker(open: open), standing = Walker(open: open);
            Force(crouched, 1, 1);
            long accel = M3TestWorld.Component<MotionLimits>(crouched, 1).Accel.Raw;
            Assert.That(accel, Is.InRange(9970L, 9980L));
            long first = Steps(crouched, 3).Find(d => d != 0), firstStanding = Steps(standing, 3).Find(d => d != 0);
            Assert.That(firstStanding, Is.EqualTo(accel));
            Assert.That(first, Is.EqualTo(6553L));
            Assert.That(first, Is.Not.EqualTo(Scaled(Math.Min(13107L, accel), 500)));
        }

        // ---- the speed line itself ----

        private sealed class Gear : ISpeedGear
        {
            private readonly int permille;
            internal Gear(int permille) { this.permille = permille; }
            public int Permille(EntityId unit) => permille;
        }

        [Test]
        public void SpeedLine_SevenGearsThenThrows()
        {
            var line = new SpeedLine(new InstantMotion());
            Assert.DoesNotThrow(() => { for (int i = 0; i < 7; i++) line.AddGear(new Gear(1000)); });
            Assert.That(line.GearCount, Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() => line.AddGear(new Gear(1000)));
            Assert.That(line.GearCount, Is.EqualTo(7));
        }

        [Test]
        public void SpeedLine_RefusesNullAndTheSameGearTwice()
        {
            var line = new SpeedLine(new InstantMotion());
            var gear = new Gear(500);
            Assert.Throws<ArgumentNullException>(() => line.AddGear(null));
            line.AddGear(gear);
            Assert.Throws<ArgumentException>(() => line.AddGear(gear));
            Assert.That(line.GearCount, Is.EqualTo(1));
            Assert.Throws<ArgumentNullException>(() => new SpeedLine(null));
        }

        // The gears multiply and divide once: 9 raw at 500 and 900 is 4 (9 * 0.45 = 4.05); dividing per gear would give 3.
        [Test]
        public void SpeedLine_ScalesOnceAndNeverToZero()
        {
            var inner = new InstantMotion();
            var line = new SpeedLine(inner);
            Assert.That(line.Inner, Is.SameAs(inner));
            Assert.That(line.Wanted(default, SimFix.FromRaw(9)).Raw, Is.EqualTo(9L), "no gear: the own speed");
            line.AddGear(new Gear(500));
            line.AddGear(new Gear(900));
            Assert.That(line.Wanted(default, SimFix.FromRaw(9)).Raw, Is.EqualTo(4L));
            Assert.That(line.StepSpeed(default, SimFix.FromRaw(9)).Raw, Is.EqualTo(4L));
            Assert.That(line.Wanted(default, SimFix.FromRaw(2)).Raw, Is.EqualTo(1L), "0.9 raw is lifted to 1");
            Assert.That(line.Wanted(default, SimFix.FromRaw(1)).Raw, Is.EqualTo(1L));
            Assert.That(line.Wanted(default, default).Raw, Is.EqualTo(0L), "a unit standing still stays still");
            var fast = new SpeedLine(inner);
            fast.AddGear(new Gear(4000));
            Assert.That(fast.Wanted(default, SimFix.FromRaw(9)).Raw, Is.EqualTo(36L));
        }
    }
}
