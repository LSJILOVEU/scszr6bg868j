using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // NT-1 A1: team range 1..8 for anchors, 0..8 for initial owners, and the pack's team count.
    public sealed class Nt1MatchPackTests
    {
        [Test]
        public void Nt1SpawnAnchor_AcceptsOneAndEight_RejectsZeroAndNine()
        {
            Assert.That(new SpawnAnchor(1, default).Team, Is.EqualTo(1));
            Assert.That(new SpawnAnchor(8, default).Team, Is.EqualTo(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnAnchor(0, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnAnchor(9, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnAnchor(-1, default));
        }

        [Test]
        public void Nt1ControlPointDef_AcceptsOwnerZeroAndEight_RejectsMinusOneAndNine()
        {
            Assert.That(new ControlPointDef(1, default, SimFix.FromInt(1), 0).InitialOwner, Is.EqualTo(0));
            Assert.That(new ControlPointDef(1, default, SimFix.FromInt(1), 8).InitialOwner, Is.EqualTo(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ControlPointDef(1, default, SimFix.FromInt(1), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ControlPointDef(1, default, SimFix.FromInt(1), 9));
        }

        [Test]
        public void Nt1MaxTeams_IsEight() => Assert.That(MatchPack.MaxTeams, Is.EqualTo(8));

        private static ControlPointDef Point(int id, int owner = 0) => new ControlPointDef(id, M6World.P(10 * id), SimFix.FromInt(2), owner);
        private static SpawnAnchor Base(int team) => new SpawnAnchor(team, M6World.P(5 * team));

        [Test]
        public void Nt1TeamCount_TwoTeams_IsTwo() =>
            Assert.That(M6World.Pack(new[] { Point(1) }, bases: new[] { Base(1), Base(2) }).TeamCount, Is.EqualTo(2));

        [Test]
        public void Nt1TeamCount_OneTeamOnly_IsStillTwo() =>
            Assert.That(M6World.Pack(new[] { Point(1, 1) }, bases: new[] { Base(1) }).TeamCount, Is.EqualTo(2));

        [Test]
        public void Nt1TeamCount_AnchorsUpToFive_IsFive() =>
            Assert.That(M6World.Pack(new[] { Point(1) }, bases: new[] { Base(1), Base(2), Base(5) }).TeamCount, Is.EqualTo(5));

        [Test]
        public void Nt1TeamCount_InitialOwnerSevenAnchorsUpToThree_IsSeven() =>
            Assert.That(M6World.Pack(new[] { Point(1, 7), Point(2, 1) }, bases: new[] { Base(3), Base(1) }).TeamCount, Is.EqualTo(7));

        [Test]
        public void Nt1TeamCount_AnchorEight_IsEight() =>
            Assert.That(M6World.Pack(new[] { Point(1, 2) }, bases: new[] { Base(8), Base(1) }).TeamCount, Is.EqualTo(8));

        // The team count adds nothing to the digest: the anchor teams and initial owners are already in it.
        [Test]
        public void Nt1TeamCount_DigestStillSeesEveryAnchorTeam()
        {
            ulong three = M6World.Pack(new[] { Point(1) }, bases: new[] { Base(1), Base(3) }).PolicyDigest;
            ulong two = M6World.Pack(new[] { Point(1) }, bases: new[] { Base(1), Base(2) }).PolicyDigest;
            Assert.That(three, Is.Not.EqualTo(two));
        }
    }
}
