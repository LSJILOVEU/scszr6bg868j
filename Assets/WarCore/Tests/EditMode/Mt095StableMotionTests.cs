using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt095StableMotionTests
    {
        [Test]
        public void DuplicateFirstRequestReportsMotionKeyError()
        {
            var sim = Round4DeathTests.Scene();
            EntityLink target = Round4DeathTests.Link(0);
            StableRequestKey key = Round4DeathTests.Key();
            sim.RequestPosition(target,SimVec3.Metres(1,0,0),key);
            var error = Assert.Throws<SimBoundaryException>(() => sim.RequestPosition(target,default,key));
            Assert.That(error.Message,Is.EqualTo("Duplicate stable motion request key."));
        }

        [Test]
        public void LastDetachSkipsRestoredEmptyHostAttachWithLiveZeroIdentity()
        {
            var sim = Round4ProtocolTests.World();
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            EntityId passenger = sim.Entities.Create(StableEntityKey.Placed(7,0));
            EntityLink target = sim.Entities.Link(passenger);
            SimVec3 position = SimVec3.Metres(3,0,0);
            sim.Entities.Set(positions,passenger,new Position(position));
            sim.Entities.Set(colliders,passenger,new Collider(SimFix.Ratio(1,4),SimFix.FromInt(2)));
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4));
            sim.Detach(target,Round4DeathTests.Key(0));

            byte[] snapshot = sim.SaveRetainedSnapshot();
            var diagnostics = new SimStateWriter(sim.Entities,materialize:true);
            sim.RequestDiagnostics.Save(diagnostics);
            // The final motion flag precedes the diagnostics section; its host is already empty.
            int flag = snapshot.Length-diagnostics.ToArray().Length-1;
            Assert.That(snapshot[flag],Is.EqualTo(0));
            snapshot[flag] = 1;
            sim.RestoreRetainedSnapshot(snapshot,true);
            sim.Entities.CreateIdentity(StableEntityKey.Placed(8,0),new StableId(0));
            sim.Detach(target,Round4DeathTests.Key(1));

            Assert.DoesNotThrow(() => sim.Step(default));
            passenger = sim.Entities.Resolve(target);
            Assert.That(sim.Entities.TryGet(sim.Entities.Resolve<SpatialAttachment>(),passenger,out _),Is.False);
            Assert.That(sim.Entities.Read(positions,passenger).Value,Is.EqualTo(position));
        }
    }
}
