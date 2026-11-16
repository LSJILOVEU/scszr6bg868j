using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt227TriggersTests
    {
        [TestCase(0, 1, 1)]
        [TestCase(-1, 1, 1)]
        [TestCase(1, 0, 1)]
        [TestCase(1, -1, 1)]
        [TestCase(1, 1, 0)]
        [TestCase(1, 1, -1)]
        public void DefinitionRejectsNonPositiveIds(int id, int conditionType, int actionType)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TriggerDefinition(id, conditionType, actionType));
        }

        [Test]
        public void StableSerializationRejectsDefinitionsWithoutCapturedLinks()
        {
            var registry = new TriggerRegistry();
            registry.RegisterCondition(1, new StableSystemTests.Condition());
            registry.RegisterAction(1, new StableSystemTests.Action());
            var triggers = new TriggerSystem(registry, 1, new TriggerDefinition(1, 1, 1));
            var writer = new SimStateWriter(new EntityStore(1, IdentityMode.StableWorld));

            Assert.Throws<SimBoundaryException>(() => triggers.SaveState(writer));
        }

        [Test]
        public void CountMatchesEmptyAndPopulatedDefinitions()
        {
            var empty = new TriggerSystem(new TriggerRegistry(), 1);
            Assert.That(empty.Count, Is.EqualTo(0));

            var f = new ExtensionFixture();
            Assert.That(f.Triggers.Count, Is.EqualTo(3));
        }
    }
}
