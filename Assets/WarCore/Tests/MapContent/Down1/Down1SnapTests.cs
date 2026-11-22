using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D13 (VSPREAD-1 V9, last sentence): with aim "center", stances and combat.downed, the player's point snaps to a
    // downed enemy as to any living one, targetable false or not (targetable only steers the AI's choice); the line ends at
    // the foot lifted by half the prone height.
    public sealed class Down1SnapTests
    {
        private const long Stand = 57344, Prone = 16384, StandEye = 98304, Snap = 39321;

        private static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        // Player 1 (team 1) at (10.5, 8) m; enemy 2 (team 2) at (20.5, 8) m, downed on tick 1 when asked (prone from tick 6).
        private static Down1Session World(string targetable, bool down)
        {
            CombatPack pack = Down1ContentTests.Load(Down1ContentTests.Downed(targetable: targetable), "rwr-vspread").BuildCombat();
            Assert.That(pack.VerticalSpread.Aim, Is.EqualTo(VerticalAim.Center));
            var w = new Down1Session(pack, new[] { Soldier(1, 1, 10500, 8000, 1), Soldier(2, 2, 20500, 8000, 2, -1) }, open: Player(1));
            if (down) w.DownAt(1, 2);
            w.StepTo(8);
            return w;
        }

        private static (long X, long Y, long Z) End(Down1Session w, long dx, long dz)
        {
            SimVec3 foot = w.Combat.Point(w.Unit(2));
            SimVec3 end = w.Combat.AimedEnd(w.Unit(1), Raw(foot.X.Raw + dx, 0, foot.Z.Raw + dz));
            return (end.X.Raw, end.Y.Raw, end.Z.Raw);
        }

        private static (long X, long Y, long Z) Foot(Down1Session w, long dx, long dz, long lift)
        {
            SimVec3 foot = w.Combat.Point(w.Unit(2));
            return (foot.X.Raw + dx, foot.Y.Raw + lift, foot.Z.Raw + dz);
        }

        [TestCase("false")]
        [TestCase("true")]
        public void D13_ThePointSnapsToADownedEnemy(string targetable)
        {
            Down1Session w = World(targetable, true);
            Assert.That(w.Downed(2), Is.True);
            Assert.That(w.S.StanceOf(2), Is.EqualTo(new StanceView(2, -1, 0)));
            Assert.That(w.Combat.Targetable(w.Unit(2)), Is.EqualTo(targetable == "true"));
            Assert.That(End(w, 0, 0), Is.EqualTo(Foot(w, 0, 0, Prone)), "on the foot");
            Assert.That(End(w, -Snap, 0), Is.EqualTo(Foot(w, 0, 0, Prone)), "at the snap edge");
            Assert.That(End(w, 0, Snap), Is.EqualTo(Foot(w, 0, 0, Prone)), "along z");
            Assert.That(End(w, -Snap - 1, 0), Is.EqualTo(Foot(w, -Snap - 1, 0, StandEye)), "past the edge: the point at eye height");
        }

        // The same world without the downing: the enemy stands, the end is half the standing height.
        [Test]
        public void D13_AStandingEnemySnapsAtHalfItsHeight()
        {
            Down1Session w = World("false", false);
            Assert.That(w.Downed(2), Is.False);
            Assert.That(End(w, 0, 0), Is.EqualTo(Foot(w, 0, 0, Stand)));
        }
    }
}
