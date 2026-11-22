using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Nerve1.Nerve1Fixture;

namespace WarCore.Tests.Nerve1
{
    // NERVE-1 on the combat system itself: the pressure drag, the wound drag, their clamp, the factor budget, the read-only
    // receipt and the nerve source. Wp13 worlds: no stance, no move spread, the zero resolver.
    public sealed class Nerve1SpreadTests
    {
        private static Wp13TestWorld Bound(CombatStatus status, out Levels levels, int floor = 0, int ceil = 0, bool controlled = false,
            bool enemy = true, long maxHealthRaw = 100 * S)
        {
            Wp13TestWorld w = World(Pack(status, floor, ceil, maxHealthRaw), controlled, enemy);
            levels = new Levels();
            w.Combat.BindNerve(levels);
            return w;
        }

        [Test]
        public void N1b_TheStatusTakesOneFactorForPressureAndOneForWounds()
        {
            void Budget(CombatStatus status, int free)
            {
                CombatSystem c = World(Pack(status)).Combat;
                Assert.DoesNotThrow(() => { for (int i = 0; i < free; i++) c.AddSpreadDrag(new Fixed(1000)); });
                Assert.Throws<InvalidOperationException>(() => c.AddSpreadDrag(new Fixed(1000)), "factor " + (StatLine.MaxFactors - free + 1));
            }
            Budget(Pinned(), 6);
            Budget(Broken(), 6);
            Budget(Nerve(), 6);
            Budget(new CombatStatus(1400, 0, 500, 1250), 5);
            Budget(Wounded(), 6);
            Budget(All(), 5);
            Budget(null, 7);
        }

        [Test]
        public void N2_CalmPinnedBroken()
        {
            Wp13TestWorld w = Bound(Nerve(), out Levels levels);
            levels.Set(w.Id(1), 1); levels.Set(w.Id(2), 2);
            Assert.That(Angle(w, 0), Is.EqualTo(2000), "calm");
            Assert.That(Angle(w, 1), Is.EqualTo(2800), "pinned 1400");
            Assert.That(Angle(w, 2), Is.EqualTo(3600), "broken 1800, not 1400 * 1800");
        }

        [Test]
        public void N2_AShotKeepsTheWeaponsOwnSpreadState()
        {
            Wp13TestWorld w = Bound(Nerve(), out Levels levels), calm = Bound(Nerve(), out _);
            levels.Set(w.Id(0), 1); levels.Set(w.Id(1), 2);
            w.Step(); calm.Step();
            Assert.That(w.State(0).SpreadStored, Is.GreaterThan(0), "the shot was fired and grew the weapon's spread");
            Assert.That(w.State(0).SpreadStored, Is.EqualTo(calm.State(0).SpreadStored), "pinned");
            Assert.That(w.State(1).SpreadStored, Is.EqualTo(calm.State(1).SpreadStored), "broken");
            Assert.That(w.State(0).SpreadLastMilli, Is.EqualTo(calm.State(0).SpreadLastMilli));
        }

        [Test]
        public void N2a_ALevelWhoseRowIsAbsentCountsOneThousand()
        {
            Wp13TestWorld onlyPinned = Bound(Pinned(), out Levels a), onlyBroken = Bound(Broken(), out Levels b);
            a.Set(onlyPinned.Id(1), 1); a.Set(onlyPinned.Id(2), 2);
            b.Set(onlyBroken.Id(1), 1); b.Set(onlyBroken.Id(2), 2);
            Assert.That(Angle(onlyPinned, 1), Is.EqualTo(2800));
            Assert.That(Angle(onlyPinned, 2), Is.EqualTo(2000), "only pinned written: a broken unit does not fall back to it");
            Assert.That(Angle(onlyBroken, 1), Is.EqualTo(2000), "only broken written: a pinned unit is calm");
            Assert.That(Angle(onlyBroken, 2), Is.EqualTo(3600));
        }

        [Test]
        public void N3b_APressedShooterDrawsAsManySpreadNumbersAsACalmOne()
        {
            Wp13TestWorld pressed = Bound(Nerve(), out Levels levels), calm = Bound(Nerve(), out _);
            for (int i = 0; i < 4; i++) levels.Set(pressed.Id(i), i % 2 + 1);
            int shots = 0;
            for (int t = 0; t < 120; t++)
            {
                pressed.Step(); calm.Step();
                Assert.That(pressed.Combat.Fires.Length, Is.EqualTo(calm.Combat.Fires.Length), "tick " + pressed.Sim.Clock.Tick);
                shots += pressed.Combat.Fires.Length;
            }
            Assert.That(shots, Is.GreaterThan(40), "they fired");
            long draws = Nerve1DefaultOffTests.Draws(pressed.Sim.Rng, "combat.spread");
            Assert.That(draws, Is.EqualTo(Nerve1DefaultOffTests.Draws(calm.Sim.Rng, "combat.spread")));
            Assert.That(draws, Is.EqualTo(shots), "one number per shot: every angle is above 0");
            Assert.That(levels.Calls, Is.GreaterThanOrEqualTo(shots), "the source is asked at every shot and draws nothing");
        }

        [Test]
        public void N5_ThePlayersUnitIsPressedToo()
        {
            Wp13TestWorld w = Bound(Nerve(), out Levels levels, controlled: true);
            levels.Set(w.Id(0), 1);
            Assert.That(w.Control.Selected, Is.EqualTo(w.Id(0)));
            Assert.That(Angle(w, 0), Is.EqualTo(2800));
        }

        [Test]
        public void N6_TheWoundLineIsAShareOfTheMaximum()
        {
            Wp13TestWorld w = World(Pack(Wounded()), enemy: false);
            Assert.That(w.Combat.HealthMaxRaw(w.Id(0)), Is.EqualTo(6553600));
            HurtNextStep(w, (0, 3276800), (1, 3276801));
            w.Step();
            Assert.That(Health(w, 0), Is.EqualTo(3276800));
            Assert.That(Health(w, 1), Is.EqualTo(3276799));
            Assert.That(w.Combat.IsWounded(w.Unit(0)), Is.False, "exactly half: not below");
            Assert.That(Angle(w, 0), Is.EqualTo(2000));
            Assert.That(w.Combat.IsWounded(w.Unit(1)), Is.True, "one raw below half");
            Assert.That(Angle(w, 1), Is.EqualTo(2500));
            Assert.That(w.Combat.IsWounded(w.Unit(2)), Is.False, "full health");

            // 40 health of 100 is wounded, 40 of 60 is not: the line follows the maximum, not a constant.
            Wp13TestWorld hundred = World(Pack(Wounded()), enemy: false), sixty = World(Pack(Wounded(), maxHealthRaw: 60 * S), enemy: false);
            HurtNextStep(hundred, (0, 60 * S));
            HurtNextStep(sixty, (0, 20 * S));
            hundred.Step(); sixty.Step();
            Assert.That(Health(hundred, 0), Is.EqualTo(2621440));
            Assert.That(Health(sixty, 0), Is.EqualTo(2621440));
            Assert.That(Angle(hundred, 0), Is.EqualTo(2500));
            Assert.That(Angle(sixty, 0), Is.EqualTo(2000));
        }

        // A unit wounded below the line (index 0) and broken; the rest calm and whole.
        private static Wp13TestWorld BrokenAndWounded(CombatStatus status, int floor, int ceil, out Levels levels, params int[] gears)
        {
            Wp13TestWorld w = Bound(status, out levels, floor, ceil, enemy: false);
            foreach (int gear in gears) w.Combat.AddSpreadGear(new Fixed(gear));
            HurtNextStep(w, (0, 60 * S));
            w.Step();
            levels.Set(w.Id(0), 2);
            return w;
        }

        [Test]
        public void N7_DragsMultiplyWithGearsAndAreClampedOnce()
        {
            Wp13TestWorld a = BrokenAndWounded(All(1400, 1800, 500, 1250), 0, 3000, out _, 700, 1500);
            Assert.That(Angle(a, 0), Is.EqualTo(4725), "(a) 2000 * 700 * 1500 * 1800 * 1250 / 10^12: drags 2.25 under the ceiling");
            Assert.That(Angle(a, 1), Is.EqualTo(2100), "a calm, whole unit: gears only");

            Wp13TestWorld b = BrokenAndWounded(All(1400, 2500, 500, 1500), 0, 3000, out _, 700, 1500);
            Assert.That(Angle(b, 0), Is.EqualTo(6300), "(b) drags 3.75 capped at 3: 2000 * 700 * 1500 * 3000 / 10^9");

            Wp13TestWorld c = BrokenAndWounded(All(1400, 1999, 500, 1001), 0, 0, out _);
            Assert.That(Angle(c, 0), Is.EqualTo(4001), "(c) multiplied apart and divided once (one merged factor would give 4000)");

            Wp13TestWorld d = Bound(Pinned(500), out Levels dl, floor: 800);
            dl.Set(d.Id(0), 1);
            Assert.That(Angle(d, 0), Is.EqualTo(1600), "(d) 500 under the floor 800");
            Assert.That(Angle(d, 1), Is.EqualTo(2000), "a calm unit's 1000 is above the floor: unchanged");
        }

        [Test]
        public void N7_AClampAloneMovesNoUnitWhoseDragsAreOneThousand()
        {
            Wp13TestWorld onlyClamp = World(Pack(null, 1, 1000));
            Assert.That(Angle(onlyClamp, 0), Is.EqualTo(2000), "no drag at all: nothing to clamp");
            Wp13TestWorld tight = Bound(All(), out _, 1000, 1000);
            Assert.That(Angle(tight, 0), Is.EqualTo(2000), "floor = ceiling = 1000: a calm, whole unit is not moved");
        }

        [Test]
        public void N14_TheReceiptReadsWhatShotAngleUses()
        {
            Wp13TestWorld w = BrokenAndWounded(All(), 0, 0, out Levels levels);
            levels.Set(w.Id(0), 0);
            levels.Set(w.Id(1), 1); levels.Set(w.Id(2), 2);
            // Unit 0 calm and wounded, 1 pinned, 2 broken, 3 calm and whole.
            int[] nerve = { 1000, 1400, 1800, 1000 }, wound = { 1250, 1000, 1000, 1000 }, level = { 0, 1, 2, 0 };
            ulong before = w.Sim.StateHashRetained();
            for (int round = 0; round < 250; round++)
                for (int i = 0; i < 4; i++)
                {
                    EntityId unit = w.Unit(i);
                    Assert.That(w.Combat.NerveLevelOf(unit), Is.EqualTo(level[i]));
                    Assert.That(w.Combat.NerveSpreadPermille(unit), Is.EqualTo(nerve[i]));
                    Assert.That(w.Combat.WoundSpreadPermille(unit), Is.EqualTo(wound[i]));
                    Assert.That(w.Combat.IsWounded(unit), Is.EqualTo(wound[i] != 1000));
                    Assert.That(Angle(w, i), Is.EqualTo(2000L * nerve[i] * wound[i] / 1000000));
                }
            Assert.That(w.Sim.StateHashRetained(), Is.EqualTo(before), "a thousand reads change nothing");
        }

        [Test]
        public void N15_BindTheNerveSourceOnceAndOnlyWithPressureRows()
        {
            Wp13TestWorld w = World(Pack(Pinned()));
            Assert.That(w.Combat.HasNerveSource, Is.False);
            Assert.DoesNotThrow(() => w.Combat.BindNerve(new Levels()));
            Assert.That(w.Combat.HasNerveSource, Is.True);
            Assert.Throws<InvalidOperationException>(() => w.Combat.BindNerve(new Levels()), "a second time");
            Assert.Throws<ArgumentNullException>(() => World(Pack(Pinned())).Combat.BindNerve(null));
            Assert.Throws<InvalidOperationException>(() => World(Pack(Wounded())).Combat.BindNerve(new Levels()), "only wounded");
            Assert.Throws<InvalidOperationException>(() => World(Pack(null)).Combat.BindNerve(new Levels()), "no status");
            Assert.DoesNotThrow(() => World(Pack(Broken())).Combat.BindNerve(new Levels()), "only broken");
        }

        [Test]
        public void N15_WithoutASourceOrWithALevelOutOfRangeAShotIsRefused()
        {
            Wp13TestWorld none = World(Pack(Pinned()));
            Assert.Throws<InvalidOperationException>(() => Angle(none, 0));
            Assert.Throws<InvalidOperationException>(() => none.Combat.NerveSpreadPermille(none.Unit(0)));
            Assert.That(() => none.Step(), Throws.InstanceOf<InvalidOperationException>(), "a shot needs the level");
            foreach (int bad in new[] { -1, 3 })
            {
                Wp13TestWorld w = World(Pack(Nerve()));
                w.Combat.BindNerve(new Always(bad));
                Assert.Throws<InvalidOperationException>(() => Angle(w, 0), "level " + bad);
            }
            Wp13TestWorld wounded = World(Pack(Wounded()));
            Assert.That(Angle(wounded, 0), Is.EqualTo(2000), "only wounded: no source needed");
            Assert.That(wounded.Combat.NerveLevelOf(wounded.Unit(0)), Is.EqualTo(0));
        }
    }
}
