using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt201NavProviderRegistryTests
    {
        [Test]
        public void StateTypeIdIdentifiesNavigation()
        {
            Assert.That(new NavProviderRegistry().StateTypeId, Is.EqualTo("warcore.navigation"));
        }

        [Test]
        public void StateVersionIsOne()
        {
            Assert.That(new NavProviderRegistry().StateVersion, Is.EqualTo(1));
        }

        [Test]
        public void NullProviderReportsProviderParameter()
        {
            var nav = new NavProviderRegistry();
            var error = Assert.Throws<ArgumentException>(() => nav.Register(MoveMode.Ground, null));
            Assert.That(error.ParamName, Is.EqualTo("provider"));
        }
    }
}
