using System;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class M0c2S4NoticeTests
    {
        [Test] public void NoticeComparatorUsesPhaseTargetFullApplyKeyThenKind()
        {
            var target=M0c2S4Fixture.Actor.Id;var source=M0c2S4Fixture.Other.Id;
            var key=new GameplayApplyKey(source,2,1,target,7,9,11);
            var item=new GameplayNotice(30,target,key,3,default);
            Assert.That(item.CompareTo(new GameplayNotice(31,target,key,0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,new StableId(target.Value+1),key,0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,new GameplayApplyKey(new StableId(source.Value+1),0,0,target,0,0),0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,new GameplayApplyKey(source,3,0,target,0,0),0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,new GameplayApplyKey(source,2,2,target,0,0),0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,new GameplayApplyKey(source,2,1,new StableId(target.Value+1),0,0),0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,new GameplayApplyKey(source,2,1,target,8,0),0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,new GameplayApplyKey(source,2,1,target,7,10),0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,new GameplayApplyKey(source,2,1,target,7,9,12),0,default)),Is.LessThan(0));
            Assert.That(item.CompareTo(new GameplayNotice(30,target,key,4,default)),Is.LessThan(0));
        }
        [Test] public void CentralOutletOrdersFailureStatusDamageFinalFactAndLife()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1|8,M0c2S4Fixture.Cast(999));
            int[] expected={1900211,1900212,1900201,1900203,1900210};var events=s.Events.Read(1);
            Assert.That(events.Length,Is.EqualTo(expected.Length));for(int i=0;i<events.Length;i++)Assert.That(events[i].Type.Value,Is.EqualTo(expected[i]));
        }
        [Test] public void FullOutputWindowDropsOnlyNewNoticesAndNeverChangesCombat()
        {
            var full=M0c2S4Fixture.Create();var tiny=M0c2S4Fixture.Create(eventLimit:1);
            M0c2S4Fixture.Step(full,1|8);M0c2S4Fixture.Step(tiny,1|8);
            Assert.That(M0c2S4Fixture.Health(tiny),Is.EqualTo(M0c2S4Fixture.Health(full)));Assert.That(M0c2S4Fixture.Life(tiny).State,Is.EqualTo(GameplayLifeState.Downed));
            Assert.That(tiny.Events.Read(1)[0].Type.Value,Is.EqualTo(1900212));Assert.That(tiny.Events.EventsDroppedThisTick,Is.EqualTo(3));
            Assert.That(tiny.Events.Read(1)[1].Type.Value,Is.EqualTo(EventTypeRegistry.EventsDropped));
            var restored=M0c2S4Fixture.Create(eventLimit:1);restored.RestoreRetainedSnapshot(tiny.SaveRetainedSnapshot(),true);
            M0c2S4Fixture.Step(tiny);M0c2S4Fixture.Step(restored);Assert.That(tiny.StateHashRetained(),Is.EqualTo(restored.StateHashRetained()));
        }
        [Test] public void DeadNotificationAndCleanupHappenOnlyOnce()
        {
            var s=M0c2S4Fixture.Create(true);M0c2S4Fixture.Step(s,1);int count=0;
            foreach(var e in s.Events.Read(1))if(e.Type.Equals(GameplayEvents.Life))count++;
            Assert.That(count,Is.EqualTo(1));M0c2S4Fixture.Step(s,1|256|64);
            foreach(var e in s.Events.Read(2))Assert.That(e.Type,Is.Not.EqualTo(GameplayEvents.Life));
        }
    }
}
