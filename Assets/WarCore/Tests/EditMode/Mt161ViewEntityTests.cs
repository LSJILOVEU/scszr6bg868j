using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt161ViewEntityTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ConstructorPreservesAlive(bool alive)
        {
            var entity = new ViewEntity(1, 0, 0, 0, 0, alive: alive);
            Assert.That(entity.Alive, Is.EqualTo(alive));
        }

        [Test]
        public void ConstructorPreservesSquadWithDifferentTeam()
        {
            var entity = new ViewEntity(1, 0, 0, 0, 0, squad: 7, team: 2);
            Assert.That(entity.Squad, Is.EqualTo(7));
        }
    }
}
