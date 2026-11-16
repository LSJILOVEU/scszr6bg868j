using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt108TriggersTests
    {
        [TestCase(0, 1, 1)]
        [TestCase(1, 0, 1)]
        [TestCase(1, 1, 0)]
        public void DefinitionRejectsZeroIds(int id, int conditionType, int actionType)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TriggerDefinition(id, conditionType, actionType));
        }

        [Test]
        public void SystemRejectsDuplicateDefinitionIds()
        {
            var f = new ExtensionFixture();
            var registry = new TriggerRegistry();
            registry.RegisterCondition(1, new ProbeCondition(1));
            registry.RegisterAction(1, f.Action);
            var definition = new TriggerDefinition(1, 1, 1);
            Assert.Throws<InvalidOperationException>(() => new TriggerSystem(registry, 1, definition, definition));
        }
    }
}
