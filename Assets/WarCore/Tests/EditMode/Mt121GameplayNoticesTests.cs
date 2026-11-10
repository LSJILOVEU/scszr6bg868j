using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class Mt121GameplayNoticesTests
    {
        [Test] public void MalformedFrameNoticePrecedesDueCastFailure()
        {
            var ability=new GameplayAbilityDef(1,"delayed",1,1,
                new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer),
                new GameplayAbilityAction[0],windupTicks:1);
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"health",0,100,100,isLife:true)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(1,new[]{1UL}),new GameplayArchetypeDef(2,new[]{1UL})},
                new GameplayCapacities(8,8,8,16,0,0,1,1,1),"notice-order",
                abilities:new[]{ability},inputAuthority:new GameplayInputAuthority(1,new[]{M0c2S3Fixture.Actor.Id}),
                lifeRules:new[]{new GameplayLifeRuleDef(1,1,false,0),new GameplayLifeRuleDef(2,1,false,0)});
            var sim=M0c2S3Fixture.CreateWith(schema);
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit));
            Assert.That(sim.GameplayCastCount,Is.EqualTo(1));
            sim.Entities.Destroy(sim.Entities.Resolve(new EntityLink(M0c2S3Fixture.Target.Id)));
            sim.Step(new InputFrame(default,default,gameplayTick:2,skillCommandCount:1));
            Assert.That(sim.Events.Read(2).Length,Is.EqualTo(3));
            Assert.That(sim.Events.Read(2)[0].Type,Is.EqualTo(GameplayEvents.AbilityPhase));
            var events=(SimEventBuffer)sim.Events;
            var first=events.ReadStable(2,1);var second=events.ReadStable(2,2);
            Assert.That(first.Type,Is.EqualTo(GameplayEvents.AbilityFailure));
            Assert.That(first.Tag,Is.EqualTo((int)GameplayAbilityFailure.InvalidInput));
            Assert.That(first.Target,Is.EqualTo(default(EntityLink)));
            Assert.That(second.Type,Is.EqualTo(GameplayEvents.AbilityFailure));
            Assert.That(second.Tag,Is.EqualTo((int)GameplayAbilityFailure.TargetGone));
            Assert.That(second.Target,Is.EqualTo(new EntityLink(M0c2S3Fixture.Actor.Id)));
        }
        [TestCase(true),TestCase(false)] public void UnknownNoticeLinkIsRejectedBeforePublication(bool source)
        {
            var sim=new Simulation(1,identityMode:IdentityMode.StableWorld);
            var missing=new EntityLink(StableEntityKey.Placed(1,1).Id);
            var events=(SimEventBuffer)sim.Events;events.BeginTick(1);
            var item=new StableSimEvent(GameplayEvents.AbilityFailure,source:source?missing:default,target:source?default:missing);
            Assert.Throws<SimBoundaryException>(()=>sim.PublishGameplayNotice(item));
            Assert.That(sim.Events.Read(1).Length,Is.Zero);
            events.EndTick();
        }
    }
}
