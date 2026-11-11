using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("Mt127GameplayStatusAdmission")]
    public sealed class Mt127GameplayStatusAdmissionTests
    {
        [Test] public void ZeroMaximumDispelPreservesTheMatchingStatus()
        {
            var sim=M0c2S2Fixture.CreateWith(out var script,new[]{new S2Tick(new[]{S2Op.Apply(5)}),
                new S2Tick(new[]{S2Op.Dispel(8,maximum:0)})});
            M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            ulong token=sim.GameplayStatusAt(0).Key.Token;
            Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.None));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayStatusAt(0).Key.Definition,Is.EqualTo(5));
            Assert.That(sim.GameplayStatusAt(0).Key.Token,Is.EqualTo(token));
        }
        [Test] public void LastAvailableEffectTokenCanCreateAStatus()
        {
            var sim=M0c2S2Fixture.CreateWith(out var script,new[]{new S2Tick(new[]{S2Op.Apply(5)})});
            var cursor=typeof(GameplayState).GetField("effectNext",BindingFlags.Instance|BindingFlags.NonPublic);
            cursor.SetValue(sim.Entities.Gameplay,ulong.MaxValue-1);
            Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.None));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayStatusAt(0).Key.Token,Is.EqualTo(ulong.MaxValue-1));
            Assert.That(cursor.GetValue(sim.Entities.Gameplay),Is.EqualTo(ulong.MaxValue));
        }
        [Test] public void FullCommandBufferRejectsDirectStatusAdmissionWithBoundaryException()
        {
            var sim=M0c2S2Fixture.CreateWith(out _,new S2Tick[0]);sim.Step(default);
            var state=sim.Entities.Gameplay;var source=M0c2S2Fixture.Low;
            var target=sim.GameplayTarget(M0c2S2Fixture.Target.Id);
            for(uint ordinal=0;ordinal<128;ordinal++)
            {
                var key=new GameplayApplyKey(source,1,0,source,1,ordinal);
                Assert.That(state.AdmitStatus(GameplayAction.Dispel(target,key,8).Command,ordinal),Is.EqualTo(GameplayFailure.None));
            }
            var overflow=new GameplayApplyKey(source,1,0,source,1,128);
            var error=Assert.Throws<SimBoundaryException>(()=>state.AdmitStatus(GameplayAction.Dispel(target,overflow,8).Command,128));
            Assert.That(error.Message,Is.EqualTo("M0C_RELEASE_CAPACITY"));
        }
    }
}
