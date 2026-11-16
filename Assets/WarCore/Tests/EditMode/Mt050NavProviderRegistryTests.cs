using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt050NavProviderRegistryTests : INavProvider
    {
        public string StateTypeId => "test.zero-version";
        public int StateVersion => 0;
        public SimVec3 Advance(SimVec3 position, SimVec3 displacement, SimFix halfHeight) => position;
        public void WriteState(StateHash hash) { }
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }

        [Test]
        public void RegistryStateVersionIsOne()
        {
            Assert.That(new NavProviderRegistry().StateVersion, Is.EqualTo(1));
        }

        [Test]
        public void RegistrationRejectsZeroProviderVersion()
        {
            var registry = new NavProviderRegistry();
            Assert.Throws<ArgumentException>(() => registry.Register(MoveMode.Ground, this));
        }
    }
}
