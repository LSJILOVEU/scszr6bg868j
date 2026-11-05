using System;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class M0c2S4LifeTests
    {
        [Test] public void LethalAndHealingUseFinalValueAndOneTransition()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1|2);
            Assert.That(M0c2S4Fixture.Health(s),Is.EqualTo(20));Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Active));Assert.That(M0c2S4Fixture.Life(s).EnteredTick,Is.Zero);
            M0c2S4Fixture.Step(s,1);Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Downed));
            Assert.That(M0c2S4Fixture.Life(s).EnteredTick,Is.EqualTo(2));
        }
        [Test] public void DownedKeepsStatusesCooldownAndCancelsOnlyCast()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1|8,M0c2S4Fixture.Cast());
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Downed));Assert.That(s.GameplayCastCount,Is.Zero);
            Assert.That(s.GameplayCooldownReady(M0c2S4Fixture.Actor.Id,1),Is.EqualTo(21));Assert.That(M0c2S4Fixture.Statuses(s,M0c2S4Fixture.Actor.Id),Is.EqualTo(1));
            M0c2S4Fixture.Step(s,4);Assert.That(M0c2S4Fixture.Health(s),Is.EqualTo(-14));
            Assert.That(M0c2S4Fixture.Life(s).EnteredTick,Is.EqualTo(1));Assert.That(M0c2S4Fixture.Life(s).DownedDueTick,Is.EqualTo(5));
            Assert.That(s.GameplayStatusAt(0).PulseOrdinal,Is.EqualTo(1));
        }
        [Test] public void RecoveryKeepsCompleteStatusAndAllRescueBitsAreRequired()
        {
            var s=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(rescue:3));M0c2S4Fixture.Step(s,1|8|16);
            M0c2S4Fixture.Step(s,2);Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Downed));
            M0c2S4Fixture.Step(s,32);Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Active));
            Assert.That(s.GameplayStatusCount,Is.EqualTo(3));Assert.That(s.GameplayStatusAt(0).StartTick,Is.EqualTo(1));Assert.That(s.GameplayStatusAt(0).PulseOrdinal,Is.EqualTo(2));
        }
        [Test] public void RecoveryAtExpiryWinsButTerminationBeatsRecovery()
        {
            var s=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(duration:1));M0c2S4Fixture.Step(s,1);M0c2S4Fixture.Step(s,2);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Active));
            M0c2S4Fixture.Step(s,64|256);Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Dead));
        }
        [Test] public void DeadCleansOwnedRowsAndTasksExactlyOnce()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1|8,M0c2S4Fixture.Cast());M0c2S4Fixture.Step(s,64);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Dead));Assert.That(s.GameplayCastCount,Is.Zero);
            Assert.That(s.GameplayCooldownReady(M0c2S4Fixture.Actor.Id,1),Is.Zero);Assert.That(s.GameplayStatusCount,Is.Zero);Assert.That(s.Scheduler.PendingCount,Is.Zero);
            long entered=M0c2S4Fixture.Life(s).EnteredTick;M0c2S4Fixture.Step(s,1|256);
            Assert.That(M0c2S4Fixture.Life(s).EnteredTick,Is.EqualTo(entered));Assert.That(M0c2S4Fixture.Health(s),Is.EqualTo(-13));
        }
        [Test] public void DownedAdmissionDefaultsToRejectedWithExplicitDataException()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1);M0c2S4Fixture.Step(s,0,M0c2S4Fixture.Cast());
            Assert.That(s.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.BlockedState));
            M0c2S4Fixture.Step(s,0,M0c2S4Fixture.Cast(2));Assert.That(s.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
        }
        [Test] public void ZeroTerminationMaskDoesNotTriggerAndPreventDownedIsSeparate()
        {
            var s=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(terminate:0));M0c2S4Fixture.Step(s,64);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Active));M0c2S4Fixture.Step(s,1);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Downed));
            var t=M0c2S4Fixture.Create();M0c2S4Fixture.Step(t,128);Assert.That(M0c2S4Fixture.Life(t).State,Is.EqualTo(GameplayLifeState.Active));
            M0c2S4Fixture.Step(t,1);Assert.That(M0c2S4Fixture.Life(t).State,Is.EqualTo(GameplayLifeState.Dead));
        }
        [TestCase(false),TestCase(true)] public void WideOracleCoversLifeTimeline(bool b)
        {
            var s=M0c2S4Fixture.Create(b);var oracle=new LifeOracle();int[] flags={1,4,2,1,0,0,0,0,256};
            for(int i=0;i<flags.Length;i++)
            {int f=flags[i];oracle.Tick(i+1,(f&1)!=0?110:(f&4)!=0?1:0,(f&2)!=0?30:(f&256)!=0?200:0,0,!b,4,0,8);
                M0c2S4Fixture.Step(s,f);Assert.That((int)M0c2S4Fixture.Life(s).State,Is.EqualTo(oracle.State),"state at "+(i+1));
                Assert.That(M0c2S4Fixture.Health(s),Is.EqualTo((long)oracle.Health),"health at "+(i+1));Assert.That(M0c2S4Fixture.Life(s).EnteredTick,Is.EqualTo((long)oracle.Entered));}
        }
        [TestCase(0),TestCase(-1)] public void InvalidDownedDurationIsRejected(int duration)
            =>Assert.Throws<SimBoundaryException>(()=>M0c2S4Fixture.Schema(duration:duration));
    }
}
