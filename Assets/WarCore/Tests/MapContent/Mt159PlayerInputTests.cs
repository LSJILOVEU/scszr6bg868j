using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt159PlayerInputTests
    {
        [Test]
        public void Constructor_PreservesIndependentMovementComponents()
        {
            var input = new PlayerInput(500, -250);
            Assert.That(input.MoveXMilli, Is.EqualTo(500));
            Assert.That(input.MoveZMilli, Is.EqualTo(-250));
        }
    }
}
