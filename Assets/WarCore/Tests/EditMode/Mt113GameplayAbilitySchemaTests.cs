using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S3")]
    public sealed class Mt113GameplayAbilitySchemaTests
    {
        [Test]
        public void ThirtyTwoSourceAttributesAreAccepted()
        {
            var resources=new GameplayResourceDef[32];
            var keys=new ulong[32];
            for(int i=0;i<resources.Length;i++)
            {
                keys[i]=(ulong)(i+1);
                resources[i]=new GameplayResourceDef(keys[i],"attribute-"+i,0,100,i+1);
            }
            var ability=new GameplayAbilityDef(1,"attribute-boundary",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,1)});
            GameplaySchema schema=null;
            Assert.DoesNotThrow(()=>schema=new GameplaySchema(resources,new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(1,keys)},new GameplayCapacities(1,32,1,32,casts:1,inputCommands:1,targetsPerCast:1),
                "critical",abilities:new[]{ability},inputAuthority:new GameplayInputAuthority(1,new[]{M0c2S3Fixture.Actor.Id})));
            Assert.That(schema.ArchetypeAt(0).ResourceCount,Is.EqualTo(32));
            Assert.That(schema.ArchetypeAt(0).ResourceKeyAt(31),Is.EqualTo(32UL));
        }

        [Test]
        public void ThirtyOneDirectActionsAndOnePeriodicActionAreAccepted()
        {
            var actions=new GameplayAbilityAction[31];
            actions[0]=M0c2S3Fixture.Action(1,GameplayPrimitive.ApplyStatus,1,-1);
            for(int i=1;i<actions.Length;i++)
                actions[i]=M0c2S3Fixture.Action((uint)(i+1),GameplayPrimitive.ChangeResource,1,1);
            var ability=new GameplayAbilityDef(1,"action-boundary",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),actions);
            GameplaySchema schema=null;
            Assert.DoesNotThrow(()=>schema=M0c2S3Fixture.Schema(abilities:new[]{ability}));
            Assert.That(schema.AbilityAt(0).ActionCount,Is.EqualTo(31));
            Assert.That(schema.StatusAt(schema.FindStatus(1)).PeriodicActionCount,Is.EqualTo(1));
        }
    }
}
