using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class Mt120GameplayLifeDataTests
    {
        [Test] public void NoticeStagingCapacityAcceptsPacketLimit()
        {
            GameplaySchema schema=null;
            Assert.DoesNotThrow(()=>schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"life",0,100,100,isLife:true)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new[]{new GameplayArchetypeDef(1,new[]{1UL})},
                new GameplayCapacities(1,1,524286,1),"life",lifeRules:new[]{new GameplayLifeRuleDef(1,1,false,0)}));
            Assert.That(schema.NoticeStagingCapacity,Is.EqualTo(1048575));
        }
    }
}
