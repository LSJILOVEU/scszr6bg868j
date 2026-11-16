using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt011CombatPackTests
    {
        [Test]
        public void Constructor_RejectsAllyCheckMissRawAboveOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatPack(Array.Empty<WeaponDef>(),
                new ZeroResolver(), new NearestVisibleEnemy(), 100 * SimFix.Scale, allyCheckMissRaw: 65537));
        }

        [Test]
        public void Constructor_RejectsAllyHoldMilliBelowMinimum()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatPack(Array.Empty<WeaponDef>(),
                new ZeroResolver(), new NearestVisibleEnemy(), 100 * SimFix.Scale, allyHoldMilli: 999));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void WithSelector_PreservesDistinctFriendlyFireFlags(bool friendlyFire, bool locked)
        {
            var original = new CombatPack(Array.Empty<WeaponDef>(), new ZeroResolver(), new NearestVisibleEnemy(),
                100 * SimFix.Scale, friendlyFire: friendlyFire, friendlyFireLocked: locked);
            var selector = new NearestVisibleEnemy();
            CombatPack copy = original.WithSelector(selector);

            Assert.That(copy.Selector, Is.SameAs(selector));
            Assert.That((copy.FriendlyFire, copy.FriendlyFireLocked), Is.EqualTo((friendlyFire, locked)));
        }
    }
}
