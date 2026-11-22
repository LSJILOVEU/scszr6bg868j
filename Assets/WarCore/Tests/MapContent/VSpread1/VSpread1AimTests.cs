using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;
using static WarCore.Tests.VSpread1.VSpread1Worlds;

namespace WarCore.Tests.VSpread1
{
    // VSPREAD-1 V8 (automatic fire aims at the middle of the body), V9 (the player's point snaps to an enemy), V10 (the hole
    // of STANCE-1: aiming at a crouched or prone enemy hits) and V12 (half cover).
    public sealed class VSpread1AimTests
    {
        private const long Stand = 57344, Crouch = 32768, Prone = 16384, StandEye = 98304;

        private static UnitSpawn[] Pair() => new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) };
        private static Box Wall(long x0, long x1) => new Box(x0, 6000, x1, 10000, M);

        // Where the built-in decision's line from unit 1 at unit 2 ends.
        private static SimVec3 AutoEnd(SimSession s)
        {
            CombatSystem combat = VSpread1Worlds.CombatOf(s);
            EntityId a = VSpread1Worlds.Unit(s, 1), b = VSpread1Worlds.Unit(s, 2);
            var fire = (DefaultAutoFire)combat.BuiltInAutoFire;
            Assert.That(fire.AimAt(combat, a, new Position(combat.Point(a)), b, out SimVec3 direction), Is.EqualTo(AutoFireAction.Fire));
            return combat.MuzzlePoint(a) + direction;
        }

        private static void AtFoot(SimSession s, SimVec3 end, long lift, string label)
        {
            SimVec3 foot = VSpread1Worlds.CombatOf(s).Point(VSpread1Worlds.Unit(s, 2));
            Assert.That(end.X.Raw, Is.EqualTo(foot.X.Raw), label);
            Assert.That(end.Z.Raw, Is.EqualTo(foot.Z.Raw), label);
            Assert.That(end.Y.Raw, Is.EqualTo(foot.Y.Raw + lift), label);
        }

        // ---- V8 ----

        [TestCase(0, Stand)]
        [TestCase(1, Crouch)]
        [TestCase(2, Prone)]
        public void V8_AutoFireAimsAtHalfTheStanceHeight(int level, long lift)
        {
            SimSession s = World(VPack(Shipped()), Pair(), Map());
            if (level != 0) Force(s, 2, level);
            AtFoot(s, AutoEnd(s), lift, "level " + level);
        }

        // A target asked to lie down is still standing until the change is due.
        [Test]
        public void V8_AChangingTargetCountsAtItsOldStance()
        {
            SimSession s = World(VPack(Shipped()), Pair(), Map());
            Assert.That(s.TryStance(2, 2), Is.True);
            Assert.That(s.StanceOf(2).IsChanging, Is.True);
            AtFoot(s, AutoEnd(s), Stand, "standing until due");
        }

        // A 1 m wall right in front of the target cuts the line to its middle: back to the eye.
        [Test]
        public void V8_ACutMiddleFallsBackToTheEye()
        {
            SimSession s = World(VPack(Shipped()), Pair(), Map(Wall(19000, 20000)));
            AtFoot(s, AutoEnd(s), StandEye, "behind a 1 m wall");
            Assert.That(VSpread1Worlds.CombatOf(s).ShotEnd(VSpread1Worlds.Unit(s, 1), VSpread1Worlds.Unit(s, 2)).Y.Raw, Is.EqualTo(StandEye));
        }

        // Aim eye, the section off, and a pack without stances.
        [Test]
        public void V8_EyeOffAndNoStances()
        {
            foreach (CombatPack pack in new[] { VPack(Shipped(VerticalAim.Eye)), VPack(null) })
            {
                SimSession s = World(pack, Pair(), Map());
                Force(s, 2, 1);
                CombatSystem combat = VSpread1Worlds.CombatOf(s);
                SimVec3 old = combat.ShotEnd(VSpread1Worlds.Unit(s, 2)), end = AutoEnd(s);
                Assert.That((end.X.Raw, end.Y.Raw, end.Z.Raw), Is.EqualTo((old.X.Raw, old.Y.Raw, old.Z.Raw)));
                AtFoot(s, end, 49152, "the crouched eye");
            }
            SimSession flat = World(VPack(new VerticalSpread(1000, null, VerticalAim.Center), stanced: false), Pair(), Map());
            AtFoot(flat, AutoEnd(flat), Stand, "no stances: half the body");
        }

        // ---- V9: the player's point ----

        private static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        // Player 1 at (10.5, 8) m; enemy 2 at (20.5, 8) m; more units as given.
        private static (SimSession S, CombatSystem Combat, EntityId A) Snapper(CombatPack pack, params UnitSpawn[] more)
        {
            var units = new List<UnitSpawn>(Pair());
            units.AddRange(more);
            SimSession s = World(pack, units.ToArray(), Map(), open: Player(1));
            return (s, VSpread1Worlds.CombatOf(s), VSpread1Worlds.Unit(s, 1));
        }

        private static void Snaps(CombatSystem combat, EntityId a, SimVec3 point, EntityId enemy, string label)
        {
            SimVec3 end = combat.AimedEnd(a, point), foot = combat.Point(enemy);
            Assert.That((end.X.Raw, end.Y.Raw, end.Z.Raw), Is.EqualTo((foot.X.Raw, foot.Y.Raw + Stand, foot.Z.Raw)), label);
        }

        private static void Lifted(CombatSystem combat, EntityId a, SimVec3 point, string label)
        {
            SimVec3 end = combat.AimedEnd(a, point);
            Assert.That((end.X.Raw, end.Y.Raw, end.Z.Raw), Is.EqualTo((point.X.Raw, point.Y.Raw + StandEye, point.Z.Raw)), label);
        }

        [Test]
        public void V9_TheSnapEdge()
        {
            var (s, combat, a) = Snapper(VPack(Shipped()));
            EntityId b = VSpread1Worlds.Unit(s, 2);
            SimVec3 foot = combat.Point(b);
            Snaps(combat, a, Raw(foot.X.Raw - 39321, 0, foot.Z.Raw), b, "39321 raw snaps");
            Lifted(combat, a, Raw(foot.X.Raw - 39322, 0, foot.Z.Raw), "39322 raw does not");
            Snaps(combat, a, Raw(foot.X.Raw, 0, foot.Z.Raw + 39321), b, "along z");
            Snaps(combat, a, foot, b, "on the foot");
        }

        [Test]
        public void V9_NearestThenSmallerStableId()
        {
            var (s, combat, a) = Snapper(VPack(Shipped()), Soldier(3, 3, 20500, 9000, 2, -1));
            EntityId b = VSpread1Worlds.Unit(s, 2), c = VSpread1Worlds.Unit(s, 3);
            long x = combat.Point(b).X.Raw, z = combat.Point(b).Z.Raw;
            Assert.That(combat.Point(c).Z.Raw - z, Is.EqualTo(M));
            Snaps(combat, a, Raw(x, 0, z + M / 2 - 1), b, "nearer to 2");
            Snaps(combat, a, Raw(x, 0, z + M / 2 + 1), c, "nearer to 3");
            StableId sb = VSpread1Worlds.SimOf(s).Entities.StableOf(b), sc = VSpread1Worlds.SimOf(s).Entities.StableOf(c);
            Snaps(combat, a, Raw(x, 0, z + M / 2), sb.CompareTo(sc) < 0 ? b : c, "a tie goes to the smaller StableId");
        }

        [Test]
        public void V9_TeammatesTheDeadAndOtherShootersDoNotSnap()
        {
            // A teammate stands right on the point, the enemy 0.5 m away: the enemy.
            var (s, combat, a) = Snapper(VPack(Shipped()), Soldier(3, 1, 20500, 8500, 1, -1), Soldier(4, 4, 12000, 12000, 1, -1));
            EntityId b = VSpread1Worlds.Unit(s, 2), friend = VSpread1Worlds.Unit(s, 4);
            SimVec3 onFriend = combat.Point(VSpread1Worlds.Unit(s, 3));
            Snaps(combat, a, onFriend, b, "the teammate on the point is passed over");
            Lifted(combat, a, combat.Point(friend), "a lone teammate's foot");
            // A shooter that is not the player never snaps.
            Lifted(combat, friend, combat.Point(b), "not the player");
            // A dead enemy does not snap.
            Kill(s, 1, 2);
            s.StepOnce();
            Assert.That(combat.IsDead(b), Is.True);
            Lifted(combat, a, combat.Point(b), "dead");
        }

        [Test]
        public void V9_EyeAndOffNeverSnap()
        {
            foreach (CombatPack pack in new[] { VPack(Shipped(VerticalAim.Eye)), VPack(null) })
            {
                var (s, combat, a) = Snapper(pack);
                Lifted(combat, a, combat.Point(VSpread1Worlds.Unit(s, 2)), "eye or off");
            }
        }

        // The shot and the AI read port end at the same point: a crouched teammate 1 m in front of the enemy is under the level
        // eye line but on the line to the enemy's middle.
        private static (FireOutcome Outcome, bool Blocks) SameEnd(CombatPack pack)
        {
            var (s, combat, a) = Snapper(pack, Soldier(3, 3, 19500, 8000, 1, -1));
            Force(s, 3, 1);
            SimVec3 point = combat.Point(VSpread1Worlds.Unit(s, 2));
            StableId shooter = VSpread1Worlds.SimOf(s).Entities.StableOf(a);
            Run(s, 2, SimSystemPhase.Requests, 16, c => combat.Submit(c, FireIntent.Aimed(shooter, point)));
            // One step first: the spatial index lowers the crouched capsule in the step.
            s.StepOnce();
            bool blocks = combat.AllyBlocks(a, point, pack.Weapons[0].FalloffEnd);
            List<FireRecord> fires = Fires(s, 1);
            Assert.That(fires.Count, Is.EqualTo(1));
            return (fires[0].Outcome, blocks);
        }

        [Test]
        public void V9_TheShotAndTheReadPortShareTheEnd()
        {
            var center = SameEnd(VPack(Shipped(), NoSpread()));
            Assert.That(center.Outcome, Is.EqualTo(FireOutcome.FriendBlocked));
            Assert.That(center.Blocks, Is.True);
            var eye = SameEnd(VPack(Shipped(VerticalAim.Eye), NoSpread()));
            Assert.That(eye.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(eye.Blocks, Is.False);
        }

        // ---- V10: the player hits a crouched or prone enemy it points at ----

        private static FireRecord PlayerAt(CombatPack pack, int level)
        {
            var (s, combat, a) = Snapper(pack);
            Force(s, 2, level);
            SimVec3 point = combat.Point(VSpread1Worlds.Unit(s, 2));
            StableId shooter = VSpread1Worlds.SimOf(s).Entities.StableOf(a);
            Run(s, 1, SimSystemPhase.Requests, 16, c => combat.Submit(c, FireIntent.Aimed(shooter, point)));
            List<FireRecord> fires = Fires(s, 1);
            Assert.That(fires.Count, Is.EqualTo(1));
            return fires[0];
        }

        [TestCase(1)]
        [TestCase(2)]
        public void V10_PointingAtALowEnemyHits(int level)
        {
            Assert.That(PlayerAt(VPack(Shipped(), NoSpread()), level).Outcome, Is.EqualTo(FireOutcome.Hit));
            FireRecord old = PlayerAt(VPack(null, NoSpread()), level);
            Assert.That(old.Outcome, Is.EqualTo(FireOutcome.Open), "without the section the level line flies over");
            Assert.That(old.End.Y.Raw, Is.EqualTo(StandEye));
        }

        // ---- V12: half cover ----

        // The target stands behind a 1 m wall 24 m from the shooter (the middle is cut, so the line ends at the eye): of 1000
        // shots some hit the wall and some hit the target over it.
        [Test]
        public void V12_HalfCoverStopsSomeShots()
        {
            SimSession s = World(VPack(Shipped()), new[] { Soldier(1, 1, 5500, 8000, 1), Soldier(2, 2, 30500, 8000, 2, -1) }, Map(Wall(29000, 30000)));
            CombatSystem combat = VSpread1Worlds.CombatOf(s);
            Assert.That(combat.ShotEnd(VSpread1Worlds.Unit(s, 1), VSpread1Worlds.Unit(s, 2)).Y.Raw, Is.EqualTo(StandEye));
            int shots = 0, wall = 0, hit = 0;
            while (shots < 1000 && s.Tick < 1500)
            {
                s.StepOnce();
                foreach (FireRecord f in combat.Fires)
                {
                    if (shots == 1000) break;
                    shots++;
                    if (f.Outcome == FireOutcome.WallBlocked) wall++;
                    else if (f.Outcome == FireOutcome.Hit) hit++;
                }
            }
            Assert.That(shots, Is.EqualTo(1000));
            Assert.That(wall, Is.GreaterThan(0));
            Assert.That(hit, Is.GreaterThan(0));
        }
    }
}
