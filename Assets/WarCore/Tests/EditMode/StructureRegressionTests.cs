using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("S37")]
    public sealed class StructureRegressionTests
    {
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void IncompleteStructureDoesNotFaultSimOrBlockValidCollapse(bool missingPosition, bool missingCollider)
        {
            var sim = new Simulation(1, config: new SimConfig(2, 4, 4, 4));
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var healths = sim.Entities.Register<StructureHealth>("structure", new StructureHealthSerializer());
            var system = new StructureSystem(sim.Entities, healths, positions, colliders, 7, 1);
            sim.Register("structure", system);
            EntityId broken = sim.Entities.Create();
            EntityId valid = sim.Entities.Create();
            foreach (EntityId entity in sim.Entities)
            {
                sim.Entities.Set(healths, entity, new StructureHealth(SimFix.FromInt(1), SimFix.FromInt(2)));
                if (!entity.Equals(broken) || !missingPosition)
                    sim.Entities.Set(positions, entity, new Position());
                if (!entity.Equals(broken) || !missingCollider)
                    sim.Entities.Set(colliders, entity, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
            }
            Assert.DoesNotThrow(() => sim.Step(default));
            Assert.That(sim.Entities.Get(healths, broken).Collapsed, Is.False);
            Assert.That(sim.Entities.Get(healths, valid).Collapsed, Is.True);
            Assert.That(sim.Events.Read(1).Length, Is.EqualTo(1));
            PropertyInfo diagnostic = typeof(StructureSystem).GetProperty("AssemblyDiagnostic");
            Assert.That(diagnostic, Is.Not.Null);
            string message = (string)diagnostic.GetValue(system);
            Assert.That(message, Does.Contain("entity=0").And.Contain("Position/Collider"));
            for (int i = 0; i < 10; i++)
            {
                sim.Step(default);
                sim.StateHash();
            }
            Assert.That(diagnostic.GetValue(system), Is.SameAs(message));
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero needs the direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            AllocationSample before = meter.Read();
            for (int i = 0; i < 300; i++)
            {
                sim.Step(default);
                sim.StateHash();
            }
            AllocationAssertions.Zero(meter.Difference(before));
            sim.Entities.Set(positions, broken, new Position());
            sim.Entities.Set(colliders, broken, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
            sim.Step(default);
            Assert.That(sim.Entities.Get(healths, broken).Collapsed, Is.True);
            Assert.That(sim.Events.Read(sim.Clock.Tick).Length, Is.EqualTo(1));
            Assert.That(diagnostic.GetValue(system), Is.SameAs(message));
        }
        private static Simulation MissingScene(out StructureSystem system)
        {
            var sim = new Simulation(1);
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var healths = sim.Entities.Register<StructureHealth>("structure", new StructureHealthSerializer());
            system = new StructureSystem(sim.Entities, healths, positions, colliders, 7, 1);
            sim.Register("structure", system);
            EntityId entity = sim.Entities.Create();
            sim.Entities.Set(healths, entity, new StructureHealth(default, SimFix.FromInt(1)));
            return sim;
        }

        [Test]
        public void RestoringAWarmedSceneStillRunsFreshAssemblyDiagnostics()
        {
            Simulation source = MissingScene(out _);
            source.CompleteWarmup();
            Simulation target = MissingScene(out StructureSystem system);
            target.RestoreSnapshot(source.SaveSnapshot());
            Assert.That(system.AssemblyDiagnostic, Is.Not.Null);
            Assert.That(system.AssemblyDiagnostic, Does.Contain("entity=0"));
            string first = system.AssemblyDiagnostic;
            target.Step(default);
            Assert.That(system.AssemblyDiagnostic, Is.SameAs(first));
        }
    }
}
