using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt164ViewPointTests
    {
        [Test]
        public void PointKeepsOwnerSeparateFromCapturingTeam()
        {
            var point = new ViewPoint(1, 10f, 20f, 3f, owner: 1, capturing: 2, progress: 0.5f);

            Assert.That(point.Owner, Is.EqualTo(1));
            Assert.That(point.Capturing, Is.EqualTo(2));
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void MatchDefaultsFinishedStateFromWinner(int winner, bool expected)
        {
            var match = new ViewMatch(winner, 0, 0, 1, 1, 0, 0);

            Assert.That(match.IsFinished, Is.EqualTo(expected));
        }
    }
}
