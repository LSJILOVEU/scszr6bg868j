using NUnit.Framework;
using WarCore.Interim;

namespace WarCore.Tests
{
    public sealed class Mt032SquadStandingTests
    {
        [Test]
        public void Constructor_RejectsNegativeKind()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SquadStanding((StandingKind)(-1)));
        }
    }
}
