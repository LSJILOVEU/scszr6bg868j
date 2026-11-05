using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class TriggerTests
    {
        [Test]
        public void ContentRegistersCountAndDeathConditionsWithoutEngineChanges()
        {
            var sim = Fixtures.Empty(73);
            var containers = sim.Entities.Register<Container>("container", new ContainerSerializer());
            EntityId target = sim.Entities.Create();
            EntityId victim = sim.Entities.Create();
            sim.Entities.Set(containers, target, new Container(10));
            var registry = new TriggerRegistry();
            var action = new ProbeAction(containers);
            registry.RegisterCondition(77, new ProbeCondition(4));
            registry.RegisterCondition(78, new ProbeCondition(2));
            registry.RegisterAction(91, action);
            var triggers = new TriggerSystem(registry, 1,
                new TriggerDefinition(1, 77, 91, target: target, conditionArgument: 2, actionArgument: 3),
                new TriggerDefinition(2, 78, 91, victim, target, actionArgument: 4));
            sim.Register("content-triggers", triggers);
            sim.Step(default);
            Assert.That(action.Calls, Is.Zero);
            sim.Entities.Destroy(victim);
            sim.Step(default);
            Assert.That(action.Calls, Is.EqualTo(2));
            Assert.That(sim.Entities.Get(containers, target).Count, Is.EqualTo(7));
            Assert.That(triggers.Get(0).FiredTick, Is.EqualTo(2));
            Assert.That(triggers.Get(1).FiredTick, Is.EqualTo(2));
            byte[] snapshot = sim.SaveSnapshot();
            sim.Step(default);
            sim.RestoreSnapshot(snapshot);
            sim.Step(default);
            Assert.That(action.Calls, Is.EqualTo(2));
        }

        [Test]
        public void MissingDuplicateFrozenAndMismatchedOperationRegistrationsFailClearly()
        {
            var f = new ExtensionFixture();
            var missingCondition = new TriggerRegistry();
            missingCondition.RegisterAction(1, f.Action);
            Assert.That(Assert.Throws<InvalidOperationException>(() => new TriggerSystem(missingCondition, 1,
                new TriggerDefinition(1, 77, 1))).Message, Does.Contain("condition registration: 77"));
            var missingAction = new TriggerRegistry();
            missingAction.RegisterCondition(1, new ProbeCondition(1));
            Assert.That(Assert.Throws<InvalidOperationException>(() => new TriggerSystem(missingAction, 1,
                new TriggerDefinition(1, 1, 78))).Message, Does.Contain("action registration: 78"));
            var registry = new TriggerRegistry();
            registry.RegisterCondition(1, new ProbeCondition(1));
            registry.RegisterAction(1, f.Action);
            Assert.Throws<InvalidOperationException>(() => registry.RegisterCondition(1, new ProbeCondition(2)));
            Assert.Throws<InvalidOperationException>(() => registry.RegisterAction(1, f.Action));
            new TriggerSystem(registry, 1, new TriggerDefinition(1, 1, 1));
            Assert.Throws<InvalidOperationException>(() => registry.RegisterCondition(2, new ProbeCondition(1)));
            Assert.Throws<ArgumentException>(() => new TriggerRegistry().RegisterCondition(0, new ProbeCondition(1)));
            Assert.Throws<ArgumentException>(() => new TriggerRegistry().RegisterAction(1, null));
            f.P.Sim.Step(default);
            var incompatible = new ExtensionFixture(102);
            ulong before = incompatible.P.Sim.StateHash();
            Assert.That(Assert.Throws<SimSnapshotException>(() => incompatible.P.Sim.RestoreSnapshot(f.P.Sim.SaveSnapshot())).Message,
                Does.Contain("manifest mismatch"));
            Assert.That(incompatible.P.Sim.StateHash(), Is.EqualTo(before));
        }

        [Test]
        public void TriggerConfigurationAndStateAreHashedAndExplicitlySerialized()
        {
            var d = new TriggerDefinition(1, 2, 3, new EntityId(4, 5), new EntityId(6, 7), 8, 9);
            var variants = new[]
            {
                new TriggerDefinition(2, 2, 3, d.Subject, d.Target, 8, 9),
                new TriggerDefinition(1, 4, 3, d.Subject, d.Target, 8, 9),
                new TriggerDefinition(1, 2, 4, d.Subject, d.Target, 8, 9),
                new TriggerDefinition(1, 2, 3, new EntityId(4, 6), d.Target, 8, 9),
                new TriggerDefinition(1, 2, 3, d.Subject, new EntityId(6, 8), 8, 9),
                new TriggerDefinition(1, 2, 3, d.Subject, d.Target, 9, 9),
                new TriggerDefinition(1, 2, 3, d.Subject, d.Target, 8, 10)
            };
            foreach (TriggerDefinition variant in variants)
                Assert.That(Fixtures.Hash(variant), Is.Not.EqualTo(Fixtures.Hash(d)));
            Assert.That(Fixtures.Hash(new Trigger(d, true, 2)), Is.Not.EqualTo(Fixtures.Hash(new Trigger(d))));
            Assert.That(Fixtures.Hash(new Trigger(d, true, 2)), Is.Not.EqualTo(Fixtures.Hash(new Trigger(d, true, 3))));
            var f = new ExtensionFixture();
            f.P.Sim.Step(default);
            var writer = new SimStateWriter();
            f.Triggers.SaveState(writer);
            var restored = new ExtensionFixture();
            var reader = new SimStateReader(writer.ToArray());
            restored.Triggers.LoadState(reader);
            reader.End();
            Assert.That(restored.Triggers.Get(1).Definition, Is.EqualTo(f.Triggers.Get(1).Definition));
            Assert.That(restored.Triggers.Get(0).FiredTick, Is.EqualTo(1));
            Assert.That(Fixtures.Hash(restored.Triggers), Is.EqualTo(Fixtures.Hash(f.Triggers)));
        }
    }
}
