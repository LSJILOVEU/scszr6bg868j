using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Protocol")]
    public sealed class Mt218SpawnCaptureTests
    {
        [Test]
        public void WorldCaptureHashIncludesPresenceOwnershipAndKind()
        {
            var sim = Round4ProtocolTests.World();
            var key = new StableRequestKey(1,0,default,1,0,9);
            var values = new SpawnValues(SimVec3.Metres(4,5,6),SimVec3.Metres(0,0,1),17);
            sim.Spawns.EnqueueWorld(0,key,values);
            sim.Spawns.Commit();
            EntityId child = sim.Entities.Resolve(new EntityLink(StableEntityKey.World(1,0,0).Id));
            SpawnCapture capture = sim.Entities.Read(sim.Entities.Resolve<SpawnCapture>(),child);
            var hash = new StateHash();
            capture.WriteState(hash);
            // FNV-1a: absent spawner, zero id, Q16.16 vectors, ownership 17, kind 9, chunk 0.
            Assert.That(hash.Value,Is.EqualTo(0x8ECDC6651E0991B1UL));
        }
    }
}
