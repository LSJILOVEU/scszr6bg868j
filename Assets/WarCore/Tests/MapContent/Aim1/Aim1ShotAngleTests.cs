using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Aim1.Aim1Worlds;

namespace WarCore.Tests.Aim1
{
    // AIM-1 A3 (ShotAngle) and A4 (the weapon's own spread state). Units 1..3 of team 1 move along +x at 0, 3000 and
    // 6000 mm/s (still, walking, running); nobody of team 2 is near, so nobody shoots unless a test places a target.
    public sealed class Aim1ShotAngleTests
    {
        private static SimSession Movers(string layer = "aim1-harmless", int runnerWeapon = 0) => World(Load(layer), new[]
        {
            Unit(1, 1, 1, 5000, 10000), Unit(2, 2, 1, 5000, 20000, Walk), Unit(3, 3, 1, 5000, 30000, Run, runnerWeapon),
        }, new[] { BridgeCommand.MoveTo(1, 2, 95000, 20000), BridgeCommand.MoveTo(2, 3, 95000, 30000) });

        private static int Angle(SimSession s, ulong key, int weaponAngle) =>
            CombatOf(s).ShotAngle(Id(s, key), State(s, key), weaponAngle, s.Tick);

        private static SimSession MovingFor(int ticks, string layer = "aim1-harmless", int runnerWeapon = 0)
        {
            SimSession s = Movers(layer, runnerWeapon);
            for (int i = 0; i < ticks; i++) s.StepOnce();
            return s;
        }

        [Test]
        public void A3_StandingWalkingRunning()
        {
            SimSession s = MovingFor(20);
            Assert.That(Angle(s, 1, 3000), Is.EqualTo(3000), "still: 1000");
            Assert.That(Angle(s, 2, 3000), Is.EqualTo(4500), "walking: 1500");
            Assert.That(Angle(s, 3, 3000), Is.EqualTo(9000), "running: 3000");
            Assert.That(Angle(s, 3, 40000), Is.EqualTo(90000), "40000 * 3 is capped at 90000");
            Assert.That(Angle(s, 3, 30000), Is.EqualTo(90000), "exactly the cap");
            Assert.That(Angle(s, 3, 29999), Is.EqualTo(89997));
            Assert.That(Angle(s, 3, 0), Is.Zero, "a weapon without spread stays without spread");
        }

        [Test]
        public void A3_AWeaponTableReplacesTheDefault()
        {
            SimSession s = MovingFor(20, "aim1-by-weapon", runnerWeapon: 2);
            Assert.That(Angle(s, 3, 3000), Is.EqualTo(12000), "rwr.weapon-3 running: 4000");
            Assert.That(Angle(s, 2, 3000), Is.EqualTo(4500), "rwr.weapon-1 walking keeps the default 1500");
        }

        [Test]
        public void A3_TheMarkIsOnlyGoodForItsTick()
        {
            SimSession s = Movers();
            Assert.That(Angle(s, 3, 3000), Is.EqualTo(3000), "before the first tick nobody was marked: moved 0");
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(CombatOf(s).ShotAngle(Id(s, 3), State(s, 3), 3000, s.Tick + 1), Is.EqualTo(3000), "another tick: not marked, moved 0");
            Assert.That(CombatOf(s).ShotAngle(Id(s, 3), State(s, 3), 3000, s.Tick - 1), Is.EqualTo(3000));
        }

        [Test]
        public void A3_AGearMultipliesWithTheMoveColumn()
        {
            SimSession s = Movers();
            CombatOf(s).AddSpreadGear(new Fixed(400));
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(Angle(s, 2, 3000), Is.EqualTo(1800), "3000 * 400 * 1500 / 10^6");
            Assert.That(Angle(s, 1, 3000), Is.EqualTo(1200), "3000 * 400 * 1000 / 10^6");
            Assert.That(Angle(s, 1, 2), Is.Zero, "2 * 400 / 1000 = 0.8 floors to 0");
        }

        [Test]
        public void A3_WithoutMoveSpreadAGearAloneRunsTheLine()
        {
            SimSession s = World(Load("aim1-harmless-field"), new[] { Unit(1, 1, 1, 5000, 10000) }, new[] { BridgeCommand.MoveTo(1, 1, 95000, 10000) });
            Assert.That(CombatOf(s).MoveMark, Is.Null);
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(Angle(s, 1, 3000), Is.EqualTo(3000), "no source: the weapon's angle");
            CombatOf(s).AddSpreadGear(new Fixed(700));
            Assert.That(Angle(s, 1, 3000), Is.EqualTo(2100), "a gear only, no move column");
        }

        [Test]
        public void A3_DragsAreClampedGearsAreNot()
        {
            SimSession s = MovingFor(20);
            CombatSystem c = CombatOf(s);
            c.AddSpreadDrag(new Fixed(2000));
            Assert.That(Angle(s, 2, 3000), Is.EqualTo(9000), "3000 * 1500 * 2000 / 10^6");
            c.SetSpreadClamp(0, 1500);
            Assert.That(Angle(s, 2, 3000), Is.EqualTo(6750), "drag product capped at 1500: 3000 * 1500 * 1500 / 10^6");
            c.SetSpreadClamp(2500, 0);
            Assert.That(Angle(s, 2, 3000), Is.EqualTo(11250), "drag product raised to 2500");
            Assert.Throws<ArgumentOutOfRangeException>(() => c.SetSpreadClamp(3000, 2000));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.SetSpreadClamp(4001, 0));
            Assert.That(Angle(s, 2, 3000), Is.EqualTo(11250), "a refused clamp leaves the last one");
        }

        [Test]
        public void A3_AtMostSevenFactors()
        {
            SimSession s = Movers();
            CombatSystem c = CombatOf(s);
            for (int i = 0; i < 3; i++) c.AddSpreadGear(new Fixed(1000));
            for (int i = 0; i < 3; i++) c.AddSpreadDrag(new Fixed(1000));
            Assert.Throws<InvalidOperationException>(() => c.AddSpreadGear(new Fixed(1000)), "the move column is the seventh");
            Assert.Throws<InvalidOperationException>(() => c.AddSpreadDrag(new Fixed(1000)));
            Assert.Throws<ArgumentNullException>(() => CombatOf(Movers()).AddSpreadGear(null));
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(Angle(s, 3, 3000), Is.EqualTo(9000), "seven factors of which six are 1000");

            SimSession field = World(Load("aim1-harmless-field"), new[] { Unit(1, 1, 1, 5000, 10000) });
            CombatSystem f = CombatOf(field);
            for (int i = 0; i < 4; i++) f.AddSpreadGear(new Fixed(1000));
            for (int i = 0; i < 3; i++) f.AddSpreadDrag(new Fixed(1000));
            Assert.Throws<InvalidOperationException>(() => f.AddSpreadGear(new Fixed(1000)), "without the move column seven others fit");
        }

        // A target at 30 m on +z of a still shooter; the shooter's weapon has a spread of 2 thousandths of a degree.
        private static SimSession Duel(string layer, int gear)
        {
            SimSession s = World(Load(layer), new[] { Unit(1, 1, 1, 50000, 10000), Unit(2, 2, 2, 50000, 40000, weapon: -1) });
            if (gear > 0) CombatOf(s).AddSpreadGear(new Fixed(gear));
            return s;
        }

        [Test]
        public void A3_AnAngleThatFloorsToZeroDrawsNoSpreadNumber()
        {
            SimSession zero = Duel("aim1-tiny", 400), none = Duel("aim1-tiny-none", 0), two = Duel("aim1-tiny", 0);
            zero.StepOnce(); none.StepOnce(); two.StepOnce();
            Assert.That(CombatOf(zero).Fires.Length, Is.EqualTo(1));
            Assert.That(CombatOf(none).Fires.Length, Is.EqualTo(1));
            Assert.That(CombatOf(two).Fires.Length, Is.EqualTo(1));
            Assert.That(RngHash(zero), Is.EqualTo(RngHash(none)), "2 * 400 / 1000 = 0: like a weapon without spread, no spread draw");
            Assert.That(RngHash(two), Is.Not.EqualTo(RngHash(none)), "an angle of 2 draws one spread number");
            Assert.That(State(zero, 1).SpreadStored, Is.EqualTo(2), "the weapon's state keeps its own angle");
        }

        // A4: one gun, three shots, once standing and once walking: after every shot the stored spread and its time agree.
        [Test]
        public void A4_MovingDoesNotTouchTheWeaponsSpreadState()
        {
            SimSession still = Shooter(false), walking = Shooter(true);
            int shots = 0;
            for (int tick = 1; tick <= 40 && shots < 3; tick++)
            {
                still.StepOnce(); walking.StepOnce();
                Assert.That(CombatOf(walking).Fires.Length, Is.EqualTo(CombatOf(still).Fires.Length), "tick " + tick);
                if (CombatOf(still).Fires.Length == 0) continue;
                shots++;
                Assert.That(State(walking, 1).SpreadStored, Is.EqualTo(State(still, 1).SpreadStored), "shot " + shots);
                Assert.That(State(walking, 1).SpreadLastMilli, Is.EqualTo(State(still, 1).SpreadLastMilli), "shot " + shots);
                Assert.That(Angle(walking, 1, 3000), Is.EqualTo(4500), "the walker is in the walking band when it fires");
            }
            Assert.That(shots, Is.EqualTo(3));
            Assert.That(State(still, 1).SpreadStored, Is.EqualTo(1694), "ticks 1, 5, 8: 1000 -> 1340; 1340 - 186 = 1154 -> 1494; 1494 - 140 = 1354 -> 1694");
        }

        private static SimSession Shooter(bool walk) => World(Load("aim1-harmless"),
            new[] { Unit(1, 1, 1, 20000, 10000, Walk), Unit(2, 2, 2, 50000, 40000, weapon: -1) },
            walk ? new[] { BridgeCommand.MoveTo(1, 1, 90000, 10000) } : null);
    }
}
