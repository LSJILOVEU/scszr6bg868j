using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;
using static WarCore.Tests.VSpread1.VSpread1Worlds;

namespace WarCore.Tests.VSpread1
{
    // VSPREAD-1 V4 (the vertical angle and its read port), V6 (the tilt) and V7 (the weapon's spread state keeps the weapon's angle).
    public sealed class VSpread1AngleTests
    {
        private static UnitSpawn[] Pair() => new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) };

        private static (SimSession S, CombatSystem Combat, EntityId A) Shooter(CombatPack pack)
        {
            SimSession s = World(pack, Pair(), Map());
            return (s, VSpread1Worlds.CombatOf(s), VSpread1Worlds.Unit(s, 1));
        }

        // ---- V4 ----

        [Test]
        public void V4_AngleIsShotTimesTheLevelPermille()
        {
            Assert.That(VerticalSpread.Angle(2000, 800), Is.EqualTo(1600));
            Assert.That(VerticalSpread.Angle(1, 600), Is.EqualTo(0));
            Assert.That(VerticalSpread.Angle(90000, 4000), Is.EqualTo(45000));
            Assert.That(VerticalSpread.Angle(11250, 4000), Is.EqualTo(45000));
            Assert.That(VerticalSpread.Angle(11249, 4000), Is.EqualTo(44996));
            Assert.That(VerticalSpread.Angle(1999, 999), Is.EqualTo(1997));

            var (s, combat, a) = Shooter(VPack(Shipped()));
            Assert.That(combat.VerticalAngle(a, 2000), Is.EqualTo(2000), "standing 1000");
            Force(s, 1, 1);
            Assert.That(combat.VerticalAngle(a, 2000), Is.EqualTo(1600), "crouched 800");
            Force(s, 1, 2);
            Assert.That(combat.VerticalAngle(a, 2000), Is.EqualTo(1200), "prone 600");
            Assert.That(combat.VerticalAngle(a, 1), Is.EqualTo(0));
            var (_, wide, b) = Shooter(VPack(new VerticalSpread(4000, new[] { 4000, 4000 })));
            Assert.That(wide.VerticalAngle(b, 90000), Is.EqualTo(45000));
        }

        // While changing stance the wider of the two levels counts, both ways.
        [Test]
        public void V4_WhileChangingTheWiderLevel()
        {
            var (s, combat, a) = Shooter(VPack(Shipped()));
            Force(s, 1, 2);
            Assert.That(s.TryStance(1, 1), Is.True);
            Assert.That(s.StanceOf(1).IsChanging, Is.True);
            Assert.That(combat.VerticalAngle(a, 2000), Is.EqualTo(1600), "prone to crouch: 600 -> 800");
            var (t, other, b) = Shooter(VPack(Shipped()));
            Force(t, 1, 1);
            Assert.That(t.TryStance(1, 2), Is.True);
            Assert.That(other.VerticalAngle(b, 2000), Is.EqualTo(1600), "crouch to prone: 800 -> 600");
        }

        // A stance byStance leaves out takes the standing value (900), not 1000; a pack without stances the standing value.
        [Test]
        public void V4_AnUnnamedLevelAndNoStancesTakeTheStandingValue()
        {
            ContentPack content = VSpread1ContentTests.Load("{ \"permille\": 900, \"byStance\": { \"prone\": 500 } }");
            var (s, combat, a) = Shooter(content.BuildCombat());
            Force(s, 1, 1);
            Assert.That(combat.VerticalAngle(a, 2000), Is.EqualTo(1800));
            var (_, flat, b) = Shooter(VPack(new VerticalSpread(700), stanced: false));
            Assert.That(flat.VerticalAngle(b, 2000), Is.EqualTo(1400));
            var (_, off, c) = Shooter(VPack(null));
            Assert.That(off.VerticalAngle(c, 2000), Is.EqualTo(0), "off");
        }

        [Test]
        public void V4_TheReadPortRefusesAnAngleOutOfRange()
        {
            foreach (CombatPack pack in new[] { VPack(Shipped()), VPack(null) })
            {
                var (_, combat, a) = Shooter(pack);
                Assert.Throws<ArgumentOutOfRangeException>(() => combat.VerticalAngle(a, -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => combat.VerticalAngle(a, 90001));
                Assert.DoesNotThrow(() => combat.VerticalAngle(a, 0));
                Assert.DoesNotThrow(() => combat.VerticalAngle(a, 90000));
            }
        }

        [Test]
        public void V4_ReadingChangesNothing()
        {
            var (s, combat, a) = Shooter(VPack(Shipped()));
            for (int i = 0; i < 5; i++) s.StepOnce();
            ulong before = s.StateHash;
            int draws = SpreadDraws(s);
            for (int i = 0; i < 1000; i++) combat.VerticalAngle(a, i * 90);
            Assert.That(s.StateHash, Is.EqualTo(before));
            Assert.That(SpreadDraws(s), Is.EqualTo(draws));
        }

        // ---- V6: the tilt ----

        private static readonly SimVec3 X1 = new SimVec3(SimFix.FromInt(1), default, default);

        [Test]
        public void V6_TheCardsFormulaAndARecordedValue()
        {
            SimVec3 t = VerticalSpread.Tilt(X1, 1000, 65535);
            var parts = TiltParts(X1, 1000, 65535);
            Assert.That(t.Y.Raw, Is.EqualTo(parts.Up.Raw));
            Assert.That(t.X.Raw, Is.EqualTo(parts.Across.Raw));
            Assert.That(t.Z.Raw, Is.EqualTo(0L));
            // First recorded by VSPREAD-1: 1 degree up from +X.
            Assert.That(t.X.Raw, Is.EqualTo(RecordedAcross));
            Assert.That(t.Y.Raw, Is.EqualTo(RecordedUp));
            // A direction off the axes, raised: X and Z are scaled by across / h (one truncation each).
            SimVec3 slant = new SimVec3(SimFix.FromInt(3), SimFix.FromInt(1), SimFix.FromInt(-4));
            SimVec3 u = VerticalSpread.Tilt(slant, 3000, 50000);
            var p = TiltParts(slant, 3000, 50000);
            Assert.That(u.X.Raw, Is.EqualTo(p.D.X.Raw * p.Across.Raw / p.H.Raw));
            Assert.That(u.Z.Raw, Is.EqualTo(p.D.Z.Raw * p.Across.Raw / p.H.Raw));
            Assert.That(u.Y.Raw, Is.EqualTo(p.Up.Raw));
            Assert.That(u.X.Raw, Is.Not.EqualTo(p.D.X.Raw), "the horizontal part shrinks or grows with the tilt");
        }

        internal const long RecordedAcross = 65526, RecordedUp = 1143;

        [Test]
        public void V6_ZeroOffsetGivesTheNormalizedInput()
        {
            SimVec3 slant = new SimVec3(SimFix.FromInt(3), SimFix.FromInt(1), SimFix.FromInt(4));
            foreach (int roll in new[] { 32767, 32768 })
            {
                SimVec3 t = VerticalSpread.Tilt(slant, 1000, roll), n = slant.Normalized;
                Assert.That((t.X.Raw, t.Y.Raw, t.Z.Raw), Is.EqualTo((n.X.Raw, n.Y.Raw, n.Z.Raw)), "roll " + roll);
            }
        }

        [Test]
        public void V6_TheEndRollsAreMinusAndPlusTheAngle()
        {
            Assert.That(VerticalSpread.Tilt(X1, 1000, 65535).Y.Raw, Is.EqualTo(SimMath.Sin(SimFix.Ratio(1000, 360000)).Raw));
            Assert.That(VerticalSpread.Tilt(X1, 1000, 0).Y.Raw, Is.EqualTo(SimMath.Sin(SimFix.Ratio(-1000, 360000)).Raw));
            Assert.That(VerticalSpread.Tilt(X1, 1000, 65535).Y.Raw, Is.GreaterThan(0L), "a positive offset goes up");
        }

        [Test]
        public void V6_AVerticalDirectionComesBackUntouched()
        {
            SimVec3 up = new SimVec3(default, SimFix.FromInt(5), default);
            SimVec3 t = VerticalSpread.Tilt(up, 3000, 65535);
            Assert.That((t.X.Raw, t.Y.Raw, t.Z.Raw), Is.EqualTo((0L, 327680L, 0L)));
        }

        [Test]
        public void V6_LengthDoesNotMatter()
        {
            SimVec3 far = VerticalSpread.Tilt(new SimVec3(SimFix.FromInt(30), default, default), 2000, 60000);
            SimVec3 tiny = VerticalSpread.Tilt(new SimVec3(SimFix.FromRaw(1), default, default), 2000, 60000);
            Assert.That((far.X.Raw, far.Y.Raw, far.Z.Raw), Is.EqualTo((tiny.X.Raw, tiny.Y.Raw, tiny.Z.Raw)));
        }

        // ---- V7: the weapon's spread state keeps the weapon's angle ----

        [Test]
        public void V7_StoredSpreadUsesTheWeaponAngle()
        {
            WeaponDef gun = Fixed(growth: 100, max: 3000);
            var (on, onCombat, a) = Shooter(VPack(Shipped(), gun));
            var (off, offCombat, b) = Shooter(VPack(null, gun));
            Force(on, 1, 1); Force(off, 1, 1);
            Assert.That(onCombat.ShotAngle(a, onCombat.Unit(a), 2000, 1), Is.EqualTo(1400), "crouched spread 700");
            Assert.That(onCombat.VerticalAngle(a, 1400), Is.EqualTo(1120), "crouched vertical 800");
            int[] expected = { 2100, 2200, 2300 };
            for (int shot = 0; shot < 3; shot++)
            {
                on.StepOnce(); off.StepOnce();
                Assert.That(onCombat.Fires.Length, Is.EqualTo(1), "one shot a tick");
                Combatant u = onCombat.Unit(a), v = offCombat.Unit(b);
                Assert.That(u.SpreadStored, Is.EqualTo(expected[shot]));
                Assert.That(u.SpreadStored, Is.EqualTo(v.SpreadStored));
                Assert.That(u.SpreadLastMilli, Is.EqualTo(v.SpreadLastMilli));
            }
        }
    }
}
