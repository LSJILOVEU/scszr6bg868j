using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt010SightLimitedTests
    {
        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorRejectsNonPositiveSight(long raw)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SightLimited(new NearestVisibleEnemy(), SimFix.FromRaw(raw)));
        }

        [Test]
        public void ConstructorAcceptsSmallestPositiveSight()
        {
            SightLimited selector = null;
            Assert.DoesNotThrow(() => selector = new SightLimited(new NearestVisibleEnemy(), SimFix.FromRaw(1)));
            Assert.That(selector.Sight.Raw, Is.EqualTo(1));
        }
    }
}
