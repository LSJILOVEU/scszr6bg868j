using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt048InputFrameTests
    {
        [TestCase(1, 0, 0)]
        [TestCase(-1, 0, 0)]
        [TestCase(0, 1, 0)]
        [TestCase(0, -1, 0)]
        [TestCase(0, 0, 1)]
        [TestCase(0, 0, -1)]
        public void SquadCommandRejectsTargetOutsideWorld(int x, int y, int z)
        {
            long outside = (long)SimVec3.WorldLimitMetres * SimFix.Scale + 1;
            var target = new SimVec3(SimFix.FromRaw(x * outside), SimFix.FromRaw(y * outside), SimFix.FromRaw(z * outside));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SquadCommand(SquadCommandKind.MoveTo, 1, target, default));
        }
    }
}
