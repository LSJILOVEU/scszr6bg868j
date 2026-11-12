using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3")]
    public sealed class Mt180GameplayAbilityDataTests
    {
        [Test]
        public void ActionsRetainEveryOrdinalWhenSorted()
        {
            var ability=new GameplayAbilityDef(1,"action-order",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),new[]{
                M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1),
                M0c2S3Fixture.Action(3,GameplayPrimitive.Damage,1),
                M0c2S3Fixture.Action(2,GameplayPrimitive.Damage,1)});
            Assert.That(ability.ActionCount,Is.EqualTo(3));
            Assert.That(new[]{ability.ActionAt(0).Ordinal,ability.ActionAt(1).Ordinal,ability.ActionAt(2).Ordinal},
                Is.EqualTo(new[]{1u,2u,3u}));
        }

        [Test]
        public void CostsRetainEveryResourceWhenSorted()
        {
            var ability=new GameplayAbilityDef(1,"cost-order",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new GameplayAbilityAction[0],new[]{new GameplayCost(1,11),new GameplayCost(3,33),new GameplayCost(2,22)});
            Assert.That(ability.CostCount,Is.EqualTo(3));
            Assert.That(new[]{ability.CostAt(0).ResourceKey,ability.CostAt(1).ResourceKey,ability.CostAt(2).ResourceKey},
                Is.EqualTo(new[]{1UL,2UL,3UL}));
        }
    }
}
