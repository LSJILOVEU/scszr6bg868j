using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt110GameplayAbilityDataTests
    {
        [Test]
        public void ActionCapacityIncludes65535()
        {
            var actions=new GameplayAbilityAction[65535];
            for(int i=0;i<actions.Length;i++)actions[i]=M0c2S3Fixture.Action((uint)i,GameplayPrimitive.Damage,1);
            GameplayAbilityDef definition=null;
            Assert.DoesNotThrow(()=>definition=new GameplayAbilityDef(1,"capacity",1,1,
                new GameplayTargetSpec(GameplayTargetMode.Self),actions));
            Assert.That(definition.ActionCount,Is.EqualTo(65535));
        }
        [Test]
        public void CostCapacityIncludes65535()
        {
            var costs=new GameplayCost[65535];
            for(int i=0;i<costs.Length;i++)costs[i]=new GameplayCost((ulong)i,1);
            GameplayAbilityDef definition=null;
            Assert.DoesNotThrow(()=>definition=new GameplayAbilityDef(1,"capacity",1,1,
                new GameplayTargetSpec(GameplayTargetMode.Self),new GameplayAbilityAction[0],costs));
            Assert.That(definition.CostCount,Is.EqualTo(65535));
        }
        [Test]
        public void ReversedActionsSortByOrdinal()
        {
            var first=M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,11);
            var second=M0c2S3Fixture.Action(2,GameplayPrimitive.Damage,1,7);
            var definition=new GameplayAbilityDef(1,"ordered",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{second,first});
            Assert.That(definition.ActionAt(0),Is.EqualTo(first));
            Assert.That(definition.ActionAt(1),Is.EqualTo(second));
        }
        [Test]
        public void ReversedCostsSortByResourceKey()
        {
            var first=new GameplayCost(1,3);
            var second=new GameplayCost(2,7);
            var definition=new GameplayAbilityDef(1,"ordered",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new GameplayAbilityAction[0],new[]{second,first});
            Assert.That(definition.CostAt(0),Is.EqualTo(first));
            Assert.That(definition.CostAt(1),Is.EqualTo(second));
        }
    }
}
