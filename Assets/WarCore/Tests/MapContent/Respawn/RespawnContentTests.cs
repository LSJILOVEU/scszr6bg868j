using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;
using static WarCore.Tests.RespawnDigestLockTests;

namespace WarCore.Tests
{
    public sealed class RespawnContentTests
    {
        // Off (field missing or 0) keeps the digest recorded before the field existed; a positive delay changes it.
        [Test]
        public void OffKeepsTheRecordedDigest_PositiveChangesIt()
        {
            Assert.That(Load(", \"respawnTicks\": 0").BuildMatch(Points(), Bases()).PolicyDigest, Is.EqualTo(Digest));
            Assert.That(Load("").Match.RespawnTicks, Is.EqualTo(0));
            Assert.That(new MatchPack(Points(), Bases(), 2, 1, false, 300, InterruptMode.Reset, 1, 90, 30, 9000,
                SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2), 0).PolicyDigest, Is.EqualTo(Digest));
            MatchPack on = Load(", \"respawnTicks\": 300").BuildMatch(Points(), Bases());
            Assert.That(on.RespawnTicks, Is.EqualTo(300));
            Assert.That(on.PolicyDigest, Is.Not.EqualTo(Digest));
            Assert.That(Load(", \"respawnTicks\": 300").Match.RespawnTicks, Is.EqualTo(300));
        }

        [TestCase("-1")]
        [TestCase("36001")]
        public void OutOfRangeDelayIsAContentError(string value)
        {
            var e = Assert.Throws<ContentException>(() => Load(", \"respawnTicks\": " + value));
            Assert.That(e.Message, Is.EqualTo("content pack 'respawn-lock' layer 'respawn-lock' match field 'respawnTicks': out of range [0, 36000]"));
        }

        [Test]
        public void ContentAcceptsTheRangeEnds()
        {
            Assert.That(Load(", \"respawnTicks\": 36000").Match.RespawnTicks, Is.EqualTo(36000));
            Assert.That(Load(", \"respawnTicks\": 0").Match.RespawnTicks, Is.EqualTo(0));
        }

        [TestCase(-1)]
        [TestCase(36001)]
        public void ConstructorRejectsOutOfRangeDelay(int value)
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => new MatchPack(Points(), Bases(), 2, 1, false, 300, InterruptMode.Reset,
                1, 90, 30, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2), value));
            Assert.That(e.ParamName, Is.EqualTo("respawnTicks"));
        }
    }
}
