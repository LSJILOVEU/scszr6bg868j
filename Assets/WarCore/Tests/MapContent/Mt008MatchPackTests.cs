using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt008MatchPackTests
    {
        [Test]
        public void ControlPointDef_RejectsInitialOwnerAboveTwo()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ControlPointDef(1, default, SimFix.FromInt(1), initialOwner: 3));
        }

        [Test]
        public void SpawnAnchor_RejectsNeutralTeam()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnAnchor(0, default));
        }
    }
}
