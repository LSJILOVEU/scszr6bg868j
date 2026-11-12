using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt151BridgeCommandTests
    {
        [Test]
        public void MoveTo_ZeroTick_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.MoveTo(0, 1, 0, 0));
        }
    }
}
