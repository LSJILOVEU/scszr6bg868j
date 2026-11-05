using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableActivationTests
    {
        [Test]
        public void MissingChunkBlocksTheBoundaryWithoutChangingClockOrHash()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            sim.Activations.Plan(1,1);
            sim.Activations.Plan(1,2);
            sim.Activations.Deliver(2,sim.StableContent.Digest);
            ulong hash = sim.StateHashStable();
            Assert.That(sim.TryStepStable(default),Is.False);
            Assert.That(sim.Clock.Tick,Is.Zero);
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
            sim.Activations.Deliver(1,sim.StableContent.Digest);
            Assert.That(sim.TryStepStable(default),Is.True);
            Assert.That(sim.Clock.Tick,Is.EqualTo(1));
        }

        [TestCase(0),TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5)]
        public void ChunkAndCreationPermutationsHaveIdenticalThreeHundredTickBehavior(int pattern)
        {
            Simulation baseline = Build(0);
            Simulation changed = Build(pattern);
            if (pattern != 0)
            {
                bool different = false;
                for (ulong i = 0; i < 16; i++)
                {
                    var link = new EntityLink(StableEntityKey.Placed(i/4,i).Id);
                    different |= baseline.Entities.ResolveLogical(link).Id != changed.Entities.ResolveLogical(link).Id;
                }
                Assert.That(different,Is.True,"The permutation must really change slots.");
            }
            var hitsA = new SpatialHit[32];
            var hitsB = new SpatialHit[32];
            var requests = new[] { SpatialQueries.Sphere(default,SimFix.FromInt(100)),
                SpatialQueries.Cylinder(default,SimFix.FromInt(100),SimFix.FromInt(5)),
                SpatialQueries.Sector(default,SimFix.FromInt(100),SimVec3.Metres(1,0,0),500,SimFix.FromInt(5)),
                SpatialQueries.Capsule(default,SimVec3.Metres(100,0,0),SimFix.FromInt(2)),
                SpatialQueries.Ray(default,SimVec3.Metres(1,0,0),SimFix.FromInt(100)) };
            for (int tick = 0; tick < 300; tick++)
            {
                Assert.That(baseline.TryStepStable(default),Is.True);
                Assert.That(changed.TryStepStable(default),Is.True);
                Assert.That(changed.StateHashStable(),Is.EqualTo(baseline.StateHashStable()),"pattern="+pattern+" tick="+tick);
                foreach (SpatialRequest request in requests)
                {
                    var x = baseline.SpatialQuery.AllCapped(request,512,hitsA);
                    var y = changed.SpatialQuery.AllCapped(request,512,hitsB);
                    Assert.That(x.TotalHits,Is.EqualTo(y.TotalHits));
                    Assert.That(x.Written,Is.EqualTo(y.Written));
                    Assert.That(baseline.SpatialQuery.Any(request),Is.EqualTo(changed.SpatialQuery.Any(request)));
                    bool foundA = baseline.SpatialQuery.Nearest(request,out SpatialHit nearA);
                    bool foundB = changed.SpatialQuery.Nearest(request,out SpatialHit nearB);
                    Assert.That(foundA,Is.EqualTo(foundB));
                    if (foundA)
                        Assert.That(baseline.Entities.StableOf(nearA.Entity),Is.EqualTo(changed.Entities.StableOf(nearB.Entity)));
                    for (int j = 0; j < x.Written; j++)
                    {
                        Assert.That(baseline.Entities.StableOf(hitsA[j].Entity),Is.EqualTo(changed.Entities.StableOf(hitsB[j].Entity)));
                        Assert.That(hitsA[j].Distance,Is.EqualTo(hitsB[j].Distance));
                    }
                }
                for (ulong i = 0; i < 16; i++)
                {
                    var link = new EntityLink(StableEntityKey.Placed(i/4,i).Id);
                    var a = baseline.Entities.Resolve(link);
                    var b = changed.Entities.Resolve(link);
                    Assert.That(changed.Entities.Read(changed.Entities.Resolve<Position>(),b).Value,
                        Is.EqualTo(baseline.Entities.Read(baseline.Entities.Resolve<Position>(),a).Value));
                }
            }
        }
        private static Simulation Build(int pattern)
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:32),identityMode:IdentityMode.StableWorld);
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            var move = sim.Entities.Register<Movement>("move",new MovementSerializer());
            var colliders = sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            var tiers = sim.Entities.Register<SimTier>("tier",new SimTierSerializer());
            var order = new int[16];
            for (int i = 0; i < 16; i++)
                order[i] = i;
            if (pattern == 1 || pattern == 3)
                Array.Reverse(order);
            if (pattern == 2)
            {
                var random = new Random(913);
                for (int i = 15; i > 0; i--)
                {
                    int j = random.Next(i+1);
                    int value = order[i];
                    order[i] = order[j];
                    order[j] = value;
                }
            }
            if (pattern == 4)
                for (int i = 0; i < 16; i++)
                    order[i] = (i%4)*4+i/4;
            if (pattern == 5)
                for (int i = 0; i < 16; i++)
                    order[i] = i < 8 ? 2*i+1 : 2*(i-8);
            for (ulong i = 0; i < 4; i++)
                sim.Activations.Plan(1,i);
            int batches = pattern == 3 || pattern == 5 ? 2 : 1;
            for (int batch = 0; batch < batches; batch++)
            {
                for (int j = batch*16/batches; j < (batch+1)*16/batches; j++)
                {
                    ulong i = (ulong)order[j];
                    EntityId entity = sim.Entities.Create(StableEntityKey.Placed(i/4,i));
                    sim.Entities.Set(positions,entity,new Position(SimVec3.Metres((long)i,0,0)));
                    sim.Entities.Set(move,entity,new Movement(MoveMode.Flying,SimVec3.Metres(3,0,0)));
                    sim.Entities.Set(tiers,entity,i%3 == 0 ? SimTier.Reduced : SimTier.Full);
                    sim.Entities.Set(colliders,entity,new Collider(SimFix.FromInt(1),SimFix.FromInt(2),1,isStatic:i%2==0));
                    sim.Entities.MakeCold(entity);
                }
                if (batch+1 < batches)
                    Assert.That(sim.TryStepStable(default),Is.False,"Partial delivery cannot advance activation.");
            }
            for (int i = 0; i < 4; i++)
                sim.Activations.Deliver((ulong)(pattern%2 == 0 ? i : 3-i),sim.StableContent.Digest);
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4));
            sim.RegisterTiered("move",new MovementSystem(sim.Entities,positions,move,new NavProviderRegistry(),1),tiers,7);
            return sim;
        }
    }
}
