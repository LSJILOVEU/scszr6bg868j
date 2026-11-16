using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3")]
    public sealed class Mt181GameplayAbilitySchemaTests
    {
        [TestCase((GameplayTargetMode)3,0,1)]
        [TestCase(GameplayTargetMode.Query,-1,1)]
        [TestCase(GameplayTargetMode.Query,0,9)]
        public void InvalidTargetSpecIsRejected(GameplayTargetMode mode,long range,int maximum)
        {
            var target=new GameplayTargetSpec(mode,selection:GameplayTargetSelection.AllCapped,rangeRaw:range,maximumTargets:maximum);
            var ability=new GameplayAbilityDef(1,"target",1,1,target,
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,1)});
            var error=Assert.Throws<SimBoundaryException>(()=>M0c2S3Fixture.Schema(abilities:new[]{ability}));
            Assert.That(error.Message,Is.EqualTo("M0C_TARGET_SPEC"));
        }

        [Test]
        public void DamageAmountAttributeWithNegativeLowerBoundIsRejected()
        {
            var basis=M0c2S3Fixture.Schema();
            var resources=new[]{basis.ResourceAt(0),basis.ResourceAt(1),new GameplayResourceDef(3,"reserve",-1,100,2)};
            var ability=new GameplayAbilityDef(1,"attribute",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{new GameplayAbilityAction(1,GameplayActionTrigger.OnRelease,GameplayPrimitive.Damage,1,amountAttributeKey:3)});
            var error=Assert.Throws<SimBoundaryException>(()=>new GameplaySchema(resources,
                new[]{basis.DamageTypeAt(0)},new[]{basis.PipelineAt(0)},new[]{basis.ArchetypeAt(0)},
                basis.Capacities,basis.CriticalStream,new[]{basis.StatusAt(0),basis.StatusAt(1),basis.StatusAt(2)},
                new[]{ability},basis.InputAuthority));
            Assert.That(error.Message,Is.EqualTo("M0C_ABILITY_ATTRIBUTE_RANGE"));
        }

        [TestCase(GameplayActionTrigger.ActivePulse,0)]
        [TestCase(GameplayActionTrigger.OnRelease,1)]
        public void PulsePeriodMustMatchActionTrigger(GameplayActionTrigger trigger,int period)
        {
            var ability=new GameplayAbilityDef(1,"period",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,1,trigger)},activeTicks:1,pulsePeriodTicks:period);
            var error=Assert.Throws<SimBoundaryException>(()=>M0c2S3Fixture.Schema(abilities:new[]{ability}));
            Assert.That(error.Message,Is.EqualTo("M0C_ABILITY_PERIOD"));
        }
    }
}
