using System;
using System.IO;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableGuardTests
    {
        [Test]
        public void ProductCreationSitesRequireExplicitStableKeys()
        {
            string root = Path.Combine(ArchitectureTests.AssetRoot(),"WarCore");
            foreach (string file in Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories))
            {
                string relative = file.Substring(root.Length).Replace('\\','/');
                if (relative.StartsWith("/Tests/") || relative.StartsWith("/Verification/"))
                    continue;
                Assert.That(StableIdentitySourceRules.Violations(File.ReadAllText(file),file),Is.Empty,file);
            }
        }
        [Test]
        public void CreationGuardDetectsDefaultCallsAndDefaultWorldConstruction()
        {
            Assert.That(StableIdentitySourceRules.Violations("void Spawn(EntityStore e) { e.Create(); }","Content/Spawn.cs"),Is.Not.Empty);
            Assert.That(StableIdentitySourceRules.Violations("var world = new EntityStore(100);","Content/Spawn.cs"),Is.Not.Empty);
            Assert.That(StableIdentitySourceRules.Violations("e.Create(default);","Content/Spawn.cs"),Is.Not.Empty);
            Assert.That(StableIdentitySourceRules.Violations("e.Create(StableEntityKey.Placed(1,2));","Content/Spawn.cs"),Is.Empty);
            Assert.That(StableIdentitySourceRules.Violations("var x = new EntityStore(8);","Summons/Factory.cs"),Is.Not.Empty);
            Assert.That(StableIdentitySourceRules.Violations("x.Entities.Create();","Projectiles/Factory.cs"),Is.Not.Empty);
            Assert.That(StableIdentitySourceRules.Violations("x.Create(default(StableEntityKey));","StatusEffects/Factory.cs"),Is.Not.Empty);
        }
        [Test]
        public void RawEntityEventReferencesCannotEnterStableWorld()
        {
            var sim = new WarCore.Sim.Sim(1,identityMode:IdentityMode.StableWorld);
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            ((SimEventBuffer)sim.Events).BeginTick(1);
            sim.Context.Begin(default);
            Assert.Throws<SimBoundaryException>(() => sim.Context.Emit(new SimEvent(new EventTypeId(100),source:entity)));
            Assert.That(sim.Events.Read(1).Length,Is.Zero);
            sim.Context.End();
            ((SimEventBuffer)sim.Events).EndTick();
        }
        [Test]
        public void LegacyReferenceEncodingRejectsStableWriters()
        {
            var entities = new EntityStore(4,IdentityMode.StableWorld);
            EntityId entity = entities.Create(StableEntityKey.Placed(1,1));
            var writer = new SimStateWriter(entities);
            Assert.Throws<SimBoundaryException>(() => SnapshotValues.Entity(writer,entity));
        }
        [Test]
        public void StableWorldRejectsLegacySnapshotsAndUnversionedComponentSchemas()
        {
            var sim = StableSnapshotTests.Scene(false);
            Assert.Throws<SimBoundaryException>(() => sim.SaveSnapshot());
            Assert.Throws<SimBoundaryException>(() => sim.StateHash());
            var component = sim.Entities.Register<Container>("unversioned",new LegacyContainerSerializer());
            EntityId entity = sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1,0).Id));
            sim.Entities.Set(component,entity,new Container(1));
            Assert.Throws<SimBoundaryException>(() => sim.StateHashStable());
        }
        private sealed class LegacyContainerSerializer : IComponentSerializer<Container>
        {
            public int TypeId => 909;
            public int Version => 1;
            public void Write(SimStateWriter writer,in Container value) => writer.Int32(value.Capacity);
            public Container Read(SimStateReader reader) => new Container(reader.Int32());
        }
    }
}
