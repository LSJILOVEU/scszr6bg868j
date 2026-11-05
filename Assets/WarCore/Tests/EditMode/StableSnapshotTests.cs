using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableSnapshotTests
    {
        internal static Simulation Scene(bool reverse)
        {
            var sim = new Simulation(123, config:new SimConfig(entityCapacity:32), identityMode:IdentityMode.StableWorld, identityCapacity:64);
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            var movements = sim.Entities.Register<Movement>("movement",new MovementSerializer());
            var colliders = sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            var tiers = sim.Entities.Register<SimTier>("tier",new SimTierSerializer());
            for (ulong i = 0; i < 16; i++)
            {
                ulong key = reverse ? 15-i : i;
                EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,key));
                sim.Entities.Set(positions,entity,new Position(SimVec3.Metres((long)key,0,0)));
                sim.Entities.Set(movements,entity,new Movement(MoveMode.Flying,SimVec3.Metres(3,0,0)));
                sim.Entities.Set(colliders,entity,new Collider(SimFix.FromInt(1),SimFix.FromInt(2)));
                sim.Entities.Set(tiers,entity,key % 2 == 0 ? SimTier.Reduced : SimTier.Full);
            }
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4));
            sim.RegisterTiered("move",new MovementSystem(sim.Entities,positions,movements,new NavProviderRegistry(),1),tiers,7);
            return sim;
        }

        [Test]
        public void ReorderedWorldsAndRemappedRestoreMatchForThreeHundredTicks()
        {
            Simulation a=Scene(false);
            Simulation b=Scene(true);
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()),"initial");
            EntityId old=b.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1,0).Id));
            for (int tick=0; tick<23; tick++)
            {
                a.Step(default);
                b.Step(default);
                Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()),"pre-restore="+tick);
            }
            byte[] snapshot=a.SaveStableSnapshot();
            b.RestoreStableSnapshot(snapshot,reverseAllocation:true);
            Assert.That(b.Entities.IsAlive(old),Is.False,"pre-restore handles must expire");
            EntityId remapped=b.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1,0).Id));
            EntityId online=a.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1,0).Id));
            Assert.That(remapped.Id,Is.Not.EqualTo(online.Id));
            Assert.That(b.StateHashStable(),Is.EqualTo(a.StateHashStable()),"restored");
            for (int tick=0; tick<300; tick++)
            {
                a.Step(default);
                b.Step(default);
                Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()),"continued="+tick);
            }
        }
    }
}
