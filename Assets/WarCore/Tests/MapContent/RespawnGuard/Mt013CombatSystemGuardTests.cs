using System;
using NUnit.Framework;
using WarCore.Battle;

namespace WarCore.Tests
{
    public sealed class Mt013CombatSystemGuardTests
    {
        [Test]
        public void NullGuard_ReportsValueParameter()
        {
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(10));
            var e = Assert.Throws<ArgumentNullException>(() => w.Combat.SetDamageGuard(null));
            Assert.That(e.ParamName, Is.EqualTo("value"));
        }

        [Test]
        public void SecondBinding_ReportsGuardAlreadySet()
        {
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(10));
            var e = Assert.Throws<InvalidOperationException>(() => w.Combat.SetDamageGuard(w.System<RespawnSystem>()));
            Assert.That(e.Message, Is.EqualTo("A damage guard is already set."));
        }
    }
}
