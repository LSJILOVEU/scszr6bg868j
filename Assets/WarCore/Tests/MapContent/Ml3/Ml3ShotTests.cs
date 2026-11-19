using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-3 acceptance 2, 3 and 11 and the shot-line rows of the mutation table (P1, P4, P6, P7, P8, P12, D3).
    public sealed class Ml3ShotTests
    {
        private const long M = Ml3World.M;
        private static Ml3World.Unit U(long x, long y, long z, int team, int weapon = 0) => new Ml3World.Unit(x, y, z, team, weapon);
        // A wall 1 m thick across the line x = 5 m (z -2..2 m), from the ground to top.
        private static Ml3World.Wall[] Across(long top) => new[] { new Ml3World.Wall(9 * M / 2, -2 * M, 11 * M / 2, 2 * M, 0, top) };

        // Shooter (team 1) at the origin, an unarmed target (team 2) 10 m along +X; both feet on y = 0.
        private static Ml3World Pair(CombatBody? body, long wallTop = 0, long targetY = 0, long[] heights = null) =>
            new Ml3World(Ml3World.Pack(body), new[] { U(0, 0, 0, 1), U(10 * M, targetY, 0, 2, -1) }, wallTop == 0 ? null : Across(wallTop), heights: heights);

        private static void AimAtTargetFoot(Ml3World w) => w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));

        // Acceptance 2: flat, two units shoot along y = 1.5 m and hit (the target's capsule face at x = 9.75 m).
        [Test]
        public void FlatShotFliesAtEyeHeightAndHits()
        {
            Ml3World w = Pair(Ml3World.Rwr);
            FireRecord f = w.StepFires().Single();
            Assert.That((f.Outcome, f.Origin.Y.Raw, f.End.Y.Raw), Is.EqualTo((FireOutcome.Hit, 98304L, 98304L)));
            Assert.That(f.End.X.Raw, Is.EqualTo(39 * M / 4));
        }

        // Acceptance 2: a 1.0 m wall between them: seen and hit, auto fire and an aimed shot alike.
        [Test]
        public void OneMetreWallIsSeenOverAndShotOver()
        {
            Ml3World w = Pair(Ml3World.Rwr, wallTop: M);
            Assert.That(w.Combat.Visible(w.U(0), w.U(1)), Is.True);
            FireRecord auto = w.StepFires().Single();
            Assert.That((auto.Outcome, auto.Origin.Y.Raw, auto.End.Y.Raw, auto.End.X.Raw), Is.EqualTo((FireOutcome.Hit, 98304L, 98304L, 39 * M / 4)));
            Ml3World aimed = Pair(Ml3World.Rwr, wallTop: M);
            AimAtTargetFoot(aimed);
            FireRecord a = aimed.StepFires().Single();
            Assert.That((a.Outcome, a.Origin.Y.Raw, a.End.Y.Raw, a.End.X.Raw), Is.EqualTo((FireOutcome.Hit, 98304L, 98304L, 39 * M / 4)));
        }

        // The same 1.0 m wall without a body hides the target (legacy eye 0.5 m): no auto fire.
        [Test]
        public void OneMetreWallHidesTheTargetWithoutABody()
        {
            Ml3World w = Pair(null, wallTop: M);
            Assert.That(w.Combat.Visible(w.U(0), w.U(1)), Is.False);
            Assert.That(w.StepFires(), Is.Empty);
        }

        // Acceptance 2: a 2.0 m wall: not seen, no auto fire; an aimed shot stops on the wall face (x = 4.5 m) at y = 1.5 m.
        [Test]
        public void TwoMetreWallHidesAndStops()
        {
            Ml3World w = Pair(Ml3World.Rwr, wallTop: 2 * M);
            Assert.That(w.Combat.Visible(w.U(0), w.U(1)), Is.False);
            Assert.That(w.StepFires(), Is.Empty);
            Ml3World aimed = Pair(Ml3World.Rwr, wallTop: 2 * M);
            AimAtTargetFoot(aimed);
            FireRecord a = aimed.StepFires().Single();
            Assert.That((a.Outcome, a.Origin.Y.Raw, a.End.Y.Raw, a.End.X.Raw), Is.EqualTo((FireOutcome.WallBlocked, 98304L, 98304L, 9 * M / 2)));
        }

        // Acceptance 3: an explicit target shot records foot + 1.5 m to the target's foot + 1.5 m (target standing 2 m up),
        // lifted once.
        [Test]
        public void ExplicitTargetShotRecordsMuzzleToTargetEye()
        {
            Ml3World w = Pair(Ml3World.Rwr, targetY: 2 * M);
            w.Produce = c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1)));
            FireRecord f = w.StepFires().Single();
            Assert.That(f.Origin, Is.EqualTo(Ml3World.Raw(0, 98304, 0)));
            Assert.That(f.End, Is.EqualTo(Ml3World.Raw(10 * M, 2 * M + 98304, 0)));
        }

        // Without a body the explicit target shot keeps its 0.5 m record (not the muzzle 0) at both ends.
        [Test]
        public void ExplicitTargetShotKeepsTheLegacyHalfMetreWithoutABody()
        {
            Ml3World w = Pair(null);
            w.Produce = c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1)));
            FireRecord f = w.StepFires().Single();
            Assert.That((f.Origin, f.End), Is.EqualTo((Ml3World.Raw(0, M / 2, 0), Ml3World.Raw(10 * M, M / 2, 0))));
        }

        // P4/P6/P8 with muzzle 1.25 m and eye 1.5 m: every path starts at the muzzle; auto and aimed lines end at the eye
        // (the line rises 0.25 m over 10 m and meets the target's face at x = 9.75 m, y = 1.25 + 0.25 * 0.975 = 1.49375 m);
        // the explicit target shot records the target's eye.
        [Test]
        public void MuzzleBelowTheEyeStartsLowAndRises()
        {
            CombatBody body = Ml3World.Body(114688, 98304, 81920);
            const long Rise = 97894; // 1.49375 m in raw, within the line's rounding
            Ml3World auto = Pair(body);
            FireRecord a = auto.StepFires().Single();
            Assert.That((a.Outcome, a.Origin.Y.Raw), Is.EqualTo((FireOutcome.Hit, 81920L)));
            Assert.That(a.End.Y.Raw, Is.InRange(Rise - 64, Rise + 64));
            Ml3World aimed = Pair(body);
            AimAtTargetFoot(aimed);
            FireRecord b = aimed.StepFires().Single();
            Assert.That((b.Outcome, b.Origin.Y.Raw), Is.EqualTo((FireOutcome.Hit, 81920L)));
            Assert.That(b.End.Y.Raw, Is.InRange(Rise - 64, Rise + 64));
            Ml3World target = Pair(body);
            target.Produce = c => target.Combat.Submit(c, FireIntent.AtTarget(target.Id(0), target.Id(1)));
            FireRecord t = target.StepFires().Single();
            Assert.That((t.Origin.Y.Raw, t.End.Y.Raw), Is.EqualTo((81920L, 98304L)));
        }

        // The public points: eye, muzzle and shot end, new mode and legacy.
        [Test]
        public void EyeMuzzleAndShotEndPoints()
        {
            Ml3World w = Pair(Ml3World.Body(114688, 98304, 81920), targetY: M);
            Assert.That((w.Combat.EyePoint(w.U(1)), w.Combat.MuzzlePoint(w.U(1)), w.Combat.ShotEnd(w.U(1))),
                Is.EqualTo((Ml3World.Raw(10 * M, M + 98304, 0), Ml3World.Raw(10 * M, M + 81920, 0), Ml3World.Raw(10 * M, M + 98304, 0))));
            Ml3World old = Pair(null, targetY: M);
            Assert.That((old.Combat.EyePoint(old.U(1)), old.Combat.MuzzlePoint(old.U(1)), old.Combat.ShotEnd(old.U(1))),
                Is.EqualTo((CombatSystem.AimPoint(old.Point(1)), old.Point(1), old.Point(1))));
        }

        // Acceptance 11 (P7), worked by hand: flat ground with a 1 m ridge on the grid line x = 10 m (points x = 10 are 1 m,
        // all others 0). Shooter at (2.5, 0, 2.5), target at (17.5, 0, 2.5), an aimed shot at the target's foot.
        // Legacy: the shot runs at y = 0, its ground test 0.5 m higher; at x = 10 the ground (1 m) is above 0.5 m, so the shot
        // is a wall hit at the sample (10, 0.5, 2.5) lowered back by 0.5 m: End = (10, 0, 2.5).
        // New mode: the shot runs at y = 1.5 m from muzzle to eye with no extra lift; the ridge (1 m) stays under it: a hit
        // on the target's face at x = 17.25 m.
        [Test]
        public void RidgeStopsTheLegacyShotAndNotTheNewOne()
        {
            var points = new int[21 * 6];
            for (int j = 0; j <= 5; j++) points[j * 21 + 10] = (int)M;
            var ground = new HeightField(20, 5, default, default, points);
            Ml3World.Unit[] units = { U(5 * M / 2, 0, 5 * M / 2, 1), U(35 * M / 2, 0, 5 * M / 2, 2, -1) };

            var old = new Ml3World(Ml3World.Pack(null), units, ground: ground);
            AimAtTargetFoot(old);
            FireRecord o = old.StepFires().Single();
            Assert.That((o.Outcome, o.End), Is.EqualTo((FireOutcome.WallBlocked, Ml3World.Raw(10 * M, 0, 5 * M / 2))));

            var now = new Ml3World(Ml3World.Pack(Ml3World.Rwr), units, ground: ground);
            AimAtTargetFoot(now);
            FireRecord n = now.StepFires().Single();
            Assert.That((n.Outcome, n.End), Is.EqualTo((FireOutcome.Hit, Ml3World.Raw(69 * M / 4, 98304, 5 * M / 2))));
            Assert.That(now.Combat.Visible(now.U(0), now.U(1)), Is.True);
            Assert.That(old.Combat.Visible(old.U(0), old.U(1)), Is.False);
        }

        // New mode with the ground in the way: a 1.75 m ridge stops the shot where the line goes under it, recorded as is.
        // The legacy extra 0.5 m lift would carry the ground test (2.0 m) over this ridge.
        [Test]
        public void HigherRidgeStopsTheNewShotWithoutLowering()
        {
            var points = new int[21 * 6];
            for (int j = 0; j <= 5; j++) points[j * 21 + 10] = (int)(7 * M / 4);
            var ground = new HeightField(20, 5, default, default, points);
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr), new[] { U(5 * M / 2, 0, 5 * M / 2, 1), U(35 * M / 2, 0, 5 * M / 2, 2, -1) }, ground: ground);
            AimAtTargetFoot(w);
            FireRecord f = w.StepFires().Single();
            // Ground on x 9..10 rises 0 -> 1.75 m: it reaches 1.5 m at x = 9.86; the first blocked sample is the grid line x = 10.
            Assert.That((f.Outcome, f.End), Is.EqualTo((FireOutcome.WallBlocked, Ml3World.Raw(10 * M, 98304, 5 * M / 2))));
        }

        // P12: a swing over a 1.0 m wall lands in new mode (wall line eye to eye at 1.5 m) and not in legacy (0.5 m).
        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public void SwingOverALowWall(bool bodied, int landed)
        {
            var spec = new MeleeSpec(3, 1, 3, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(5), 1, SimFix.FromInt(2));
            var pack = Ml3World.Pack(bodied ? Ml3World.Rwr : (CombatBody?)null, new LethalResolver(), new[] { spec });
            var wall = new[] { new Ml3World.Wall(M / 2, -2 * M, 3 * M / 2, 2 * M, 0, M) };
            var w = new Ml3World(pack, new[] { U(0, 0, 0, 1, -1), U(2 * M, 0, 0, 2, -1) }, wall);
            w.Combat.SetMelee(w.U(0), 0);
            // The swing is aimed by an intent (the wall hides the target from the legacy eye, so no automatic start).
            w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); };
            int count = 0;
            for (int t = 0; t < 6; t++) { w.Step(); count += w.Combat.Strikes.ToArray().Count(s => s.Kind == StrikeKind.Landed); }
            Assert.That(count, Is.EqualTo(landed));
        }
    }
}
