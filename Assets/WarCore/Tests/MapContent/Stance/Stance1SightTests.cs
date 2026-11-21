using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Stance
{
    // STANCE-1 S5 (what a stance sees over a wall), S6 (the capsule is really lower) and S7 (muzzle and the foot of a shot).
    public sealed class Stance1SightTests
    {
        // A at (10.5, 8) m, B at (20.5, 8) m (walls lie on whole metres), enemies, both armed with the shipped weapon. Walls cross
        // z 6..10 m: the near one x 19..20 m, the middle one x 15..16 m (the card's 8.5..9.5 and 4.5..5.5 m from A).
        private const long NearX0 = 19000, NearX1 = 20000, MidX0 = 15000, MidX1 = 16000;
        private const long OneMetre = 65536, HalfMetre = 32768;

        private static UnitSpawn[] Pair(int weaponB = 0) => new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, weaponB) };
        private static Box Wall(long x0, long x1, long top) => new Box(x0, 6000, x1, 10000, top);

        // Hurt records in 90 steps with A and B put in their stances before the first step.
        private static int Hurt90(int a, int b, params Box[] walls) => Fires90(a, b, walls).Hurt;

        // All fire records and the hurt ones in 90 steps.
        private static (int All, int Hurt) Fires90(int a, int b, params Box[] walls)
        {
            SimSession s = World(Pack(), Pair(), Map(walls));
            if (a != 0) Force(s, 1, a);
            if (b != 0) Force(s, 2, b);
            List<FireRecord> fires = Fires(s, 90);
            return (fires.Count, Hurt(fires));
        }

        // ---- S5: the card's table (line heights on the two wall faces in its last comment) ----

        [TestCase(true, OneMetre, 0, 0, true, TestName = "S5 row 1 near 1.0 stand-stand (1.5, 1.5)")]
        [TestCase(true, OneMetre, 0, 1, false, TestName = "S5 row 2 near 1.0 stand-crouch (0.8625, 0.7875)")]
        [TestCase(true, OneMetre, 0, 2, false, TestName = "S5 row 3 near 1.0 stand-prone (0.4375, 0.3125)")]
        [TestCase(true, OneMetre, 1, 1, false, TestName = "S5 row 4 near 1.0 crouch-crouch (0.75, 0.75)")]
        [TestCase(true, OneMetre, 2, 0, true, TestName = "S5 row 5 near 1.0 prone-stand (1.3125, 1.4375)")]
        [TestCase(true, OneMetre, 1, 0, true, TestName = "S5 row 6 near 1.0 crouch-stand (1.3875, 1.4625)")]
        [TestCase(false, OneMetre, 0, 1, true, TestName = "S5 row 7 mid 1.0 stand-crouch (1.1625, 1.0875)")]
        [TestCase(false, OneMetre, 1, 1, false, TestName = "S5 row 8 mid 1.0 crouch-crouch (0.75, 0.75)")]
        [TestCase(true, HalfMetre, 0, 2, false, TestName = "S5 row 9 near 0.5 stand-prone (0.4375, 0.3125)")]
        [TestCase(true, HalfMetre, 0, 1, true, TestName = "S5 row 10 near 0.5 stand-crouch (0.8625, 0.7875)")]
        public void S5_Table(bool near, long top, int a, int b, bool hurt)
        {
            (int all, int hurtCount) = Fires90(a, b, Wall(near ? NearX0 : MidX0, near ? NearX1 : MidX1, top));
            Assert.That(hurtCount > 0, Is.EqualTo(hurt));
            // Where nobody is hurt the eye line is cut: neither sees the other, so nobody fires at all.
            if (!hurt) Assert.That(all, Is.EqualTo(0), "fire records");
        }

        // Control: with no wall all nine pairs of stances hurt someone.
        [Test]
        public void S5_NoWall_EveryPairHurts()
        {
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                    Assert.That(Hurt90(a, b), Is.GreaterThan(0), "A " + a + ", B " + b);
        }

        // Row 2 with B standing and asked to crouch, on the deterministic pack (no spread, 10 damage of 100 health, one shot a
        // second): B is hit at ticks 1, 31, 61, ... The crouch is asked between ticks 26 and 27, so it is due at 33: B is still
        // hit at 31, inside the five steps after the request, and never after 33.
        [Test]
        public void S5_Row2_BIsHitUntilTheCrouchIsDone()
        {
            Box wall = Wall(NearX0, NearX1, OneMetre);
            List<long> standing = HitsOn(World(Exact(), Pair(), Map(wall)), 2, 120, null);
            Assert.That(standing, Is.EqualTo(new long[] { 1, 31, 61, 91 }), "standing B behind the wall is hit every second");
            SimSession s = World(Exact(), Pair(), Map(wall));
            List<long> crouching = HitsOn(s, 2, 120, w => { if (w.Tick == 26) Assert.That(w.TryStance(2, 1), Is.True); });
            Assert.That(s.StanceOf(2).Level, Is.EqualTo(1));
            Assert.That(crouching, Is.EqualTo(new long[] { 1, 31 }));
        }

        // The ticks at which unit key took a hurting hit, over n steps; between runs before each step.
        private static List<long> HitsOn(SimSession s, ulong key, int n, System.Action<SimSession> between)
        {
            var ticks = new List<long>();
            StableId target = SimOf(s).Entities.StableOf(Unit(s, key));
            CombatSystem combat = CombatOf(s);
            for (int i = 0; i < n; i++)
            {
                between?.Invoke(s);
                s.StepOnce();
                foreach (FireRecord f in combat.Fires)
                    if (f.Outcome == FireOutcome.Hit && f.DamageRaw > 0 && f.Target.Equals(target)) ticks.Add(s.Tick);
            }
            return ticks;
        }

        // ---- S6: an aimed shot along a 1.5 m line passes over a crouched or prone B ----

        // A shoots at the ground point (30, 8) m behind an unarmed B in the Requests phase of tick 1: the line runs level at 1.5 m.
        private static FireRecord Aimed(int b, int a = 0, CombatPack pack = null)
        {
            SimSession s = World(pack ?? Exact(), new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) }, Map());
            if (a != 0) Force(s, 1, a);
            if (b != 0) Force(s, 2, b);
            CombatSystem combat = CombatOf(s);
            StableId shooter = SimOf(s).Entities.StableOf(Unit(s, 1));
            Run(s, 1, SimSystemPhase.Requests, 7, c => combat.Submit(c, FireIntent.Aimed(shooter, new SimVec3(SimFix.FromInt(30), default, SimFix.FromInt(8)))));
            List<FireRecord> fires = Fires(s, 1);
            Assert.That(fires.Count, Is.EqualTo(1));
            return fires[0];
        }

        [TestCase(0, FireOutcome.Hit)]
        [TestCase(1, FireOutcome.Open)]
        [TestCase(2, FireOutcome.Open)]
        public void S6_TheCapsuleIsLower(int b, FireOutcome outcome)
        {
            FireRecord f = Aimed(b);
            Assert.That(f.Outcome, Is.EqualTo(outcome));
            Assert.That((f.Origin.Y.Raw, f.End.Y.Raw), Is.EqualTo((98304L, 98304L)));
        }

        // ---- S7: the muzzle of each stance and the foot of the shot ----

        private static readonly long[] Muzzles = { 98304, 49152, 16384 };

        private static void AtFoot(SimSession s, in FireRecord f, string label)
        {
            SimVec3 foot = CombatOf(s).GroundOfFireOrigin(in f);
            Assert.That((foot.X.Raw, foot.Y.Raw, foot.Z.Raw), Is.EqualTo((10 * M + M / 2, 0L, 8 * M)), label);
        }

        // The automatic path: A's first shot at B.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void S7_AutoShot_OriginIsTheStanceMuzzle(int a)
        {
            SimSession s = World(Exact(), new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) }, Map());
            if (a != 0) Force(s, 1, a);
            FireRecord f = Fires(s, 1)[0];
            Assert.That((f.Origin.Y.Raw, f.MuzzleRaw), Is.EqualTo((Muzzles[a], Muzzles[a])));
            AtFoot(s, f, "auto " + a);
            // The record keeps the muzzle of the shot: after a change of stance its foot is the same.
            Force(s, 1, a == 2 ? 0 : 2);
            AtFoot(s, f, "auto " + a + " after the stance changed");
        }

        // The aimed path.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void S7_AimedShot_OriginIsTheStanceMuzzle(int a)
        {
            SimSession s = World(Exact(), new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) }, Map());
            if (a != 0) Force(s, 1, a);
            CombatSystem combat = CombatOf(s);
            StableId shooter = SimOf(s).Entities.StableOf(Unit(s, 1));
            Run(s, 1, SimSystemPhase.Requests, 7, c => combat.Submit(c, FireIntent.Aimed(shooter, new SimVec3(SimFix.FromInt(30), default, SimFix.FromInt(8)))));
            FireRecord f = Fires(s, 1)[0];
            Assert.That((f.Origin.Y.Raw, f.MuzzleRaw), Is.EqualTo((Muzzles[a], Muzzles[a])));
            AtFoot(s, f, "aimed " + a);
            Force(s, 1, a == 2 ? 0 : 2);
            AtFoot(s, f, "aimed " + a + " after the stance changed");
        }

        // The explicit target path.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void S7_TargetShot_OriginIsTheStanceMuzzle(int a)
        {
            SimSession s = World(Exact(), new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) }, Map());
            if (a != 0) Force(s, 1, a);
            CombatSystem combat = CombatOf(s);
            StableId shooter = SimOf(s).Entities.StableOf(Unit(s, 1)), target = SimOf(s).Entities.StableOf(Unit(s, 2));
            Run(s, 1, SimSystemPhase.Requests, 7, c => combat.Submit(c, FireIntent.AtTarget(shooter, target)));
            FireRecord f = Fires(s, 1)[0];
            Assert.That((f.Origin.Y.Raw, f.MuzzleRaw), Is.EqualTo((Muzzles[a], Muzzles[a])));
            AtFoot(s, f, "target " + a);
            Force(s, 1, a == 2 ? 0 : 2);
            AtFoot(s, f, "target " + a + " after the stance changed");
        }

        // A projectile keeps the foot it left from: the shooter goes prone after the shot, and the foot is the same in flight,
        // after a snapshot and restore, and in the damage at landing (the full-damage falloff from 0 m).
        [TestCase(0)]
        [TestCase(1)]
        public void S7_ProjectileKeepsItsLaunchFoot(int a)
        {
            CombatPack pack = Exact(speed: 6000);
            UnitSpawn[] units = { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) };
            SimSession s = World(pack, units, Map());
            if (a != 0) Force(s, 1, a);
            s.StepOnce();
            ProjectileSystem flying = CombatOf(s).Projectiles;
            Assert.That(flying.Count, Is.EqualTo(1));
            Assert.That(flying.InFlight(0).Origin.Y.Raw, Is.EqualTo(Muzzles[a]));
            Force(s, 1, a == 0 ? 2 : 0);
            SimVec3 foot = flying.InFlight(0).LaunchFoot;
            Assert.That((foot.X.Raw, foot.Y.Raw, foot.Z.Raw), Is.EqualTo((10 * M + M / 2, 0L, 8 * M)));
            byte[] save = SimOf(s).SaveRetainedSnapshot();
            SimSession b = World(pack, units, Map());
            Assert.DoesNotThrow(() => SimOf(b).RestoreRetainedSnapshot(save));
            Assert.That(CombatOf(b).Projectiles.Count, Is.EqualTo(1));
            Assert.That(CombatOf(b).Projectiles.InFlight(0).LaunchFoot, Is.EqualTo(foot));
            Assert.That(SimOf(b).SaveRetainedSnapshot(), Is.EqualTo(save));
        }
    }
}
