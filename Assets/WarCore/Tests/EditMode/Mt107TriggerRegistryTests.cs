using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt107TriggerRegistryTests : ITriggerAction
    {
        public string StateTypeId => "test.zero-version-action";
        public int StateVersion => 0;
        public void Execute(SimContext context, in TriggerDefinition definition) { }
        public void WriteState(StateHash hash) { }
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }

        [Test]
        public void RegistryStateVersionIsOne()
        {
            Assert.That(new TriggerRegistry().StateVersion, Is.EqualTo(1));
        }

        [Test]
        public void RegisterActionRejectsZeroStateVersion()
        {
            Assert.Throws<ArgumentException>(() => new TriggerRegistry().RegisterAction(1, this));
        }
    }
}
