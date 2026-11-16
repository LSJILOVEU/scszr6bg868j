using System;
using NUnit.Framework;
using WarCore.Combat;

namespace WarCore.Tests
{
    public sealed class Mt021WeaponDefTests
    {
        [Test]
        public void SpreadSpecRejectsNegativeGrowth()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpreadSpec(0, -1, 0, 1000));
        }

        [Test]
        public void SpreadSpecRejectsNegativeRecovery()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpreadSpec(0, 0, -1, 1000));
        }
    }
}
