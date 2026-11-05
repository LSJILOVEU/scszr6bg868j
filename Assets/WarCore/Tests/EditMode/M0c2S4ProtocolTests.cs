using System;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class M0c2S4ProtocolTests
    {
        [TestCase(false),TestCase(true)] public void EveryLifeBoundaryRestoresWithIdenticalHash(bool b)
        {
            var s=M0c2S4Fixture.Create(b);var bytes=new byte[301][];var hashes=new ulong[601];bytes[0]=s.SaveRetainedSnapshot();hashes[0]=s.StateHashRetained();
            for(int tick=1;tick<=600;tick++){hashes[tick]=M0c2S4Fixture.Replay(s);if(tick<=300)bytes[tick]=s.SaveRetainedSnapshot();}
            for(int checkpoint=0;checkpoint<=300;checkpoint++)
            {var restored=M0c2S4Fixture.Create(b,true);restored.RestoreRetainedSnapshot(bytes[checkpoint],true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(hashes[checkpoint]),"restore "+checkpoint);
                for(int step=1;step<=300;step++)Assert.That(M0c2S4Fixture.Replay(restored),Is.EqualTo(hashes[checkpoint+step]),"checkpoint "+checkpoint+" step "+step);}
            TestContext.WriteLine("S4 A/B={0}: 301 checkpoints x 300 continued ticks; includes downed/recovery/death",b);
        }
        [TestCase("state"),TestCase("entered"),TestCase("due"),TestCase("missing-task"),TestCase("task-origin"),TestCase("task-kind"),TestCase("cursor"),TestCase("active-with-task"),TestCase("pending-notice"),TestCase("negative-drops")]
        public void ForgedLifeRowsRejectEvenWithRecomputedChecksums(string fault)
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1|8);var doc=new M0c2S2ProtocolTests.Document(s);
            byte[] row=doc.Tables[2].Find(x=>M0c2S2ProtocolTests.Word(x,1)==M0c2S4Fixture.Actor.Id.Value);
            switch(fault)
            {
                case "state":M0c2S2ProtocolTests.Put(row,33,3);break;
                case "entered":M0c2S2ProtocolTests.Put(row,41,2);break;
                case "due":M0c2S2ProtocolTests.Put(row,49,6);break;
                case "missing-task":row[57]=0;break;
                case "task-origin":M0c2S2ProtocolTests.Put(row,74,M0c2S4Fixture.Other.Id.Value);break;
                case "task-kind":M0c2S2ProtocolTests.Put(row,90,ulong.MaxValue);break;
                case "cursor":M0c2S2ProtocolTests.Put(doc.Tables[0][0],0,0);break;
                case "active-with-task":M0c2S2ProtocolTests.Put(row,33,0);break;
                case "pending-notice":M0c2S2ProtocolTests.Put(doc.Tables[8][0],256,1);break;
                case "negative-drops":M0c2S2ProtocolTests.Put(doc.Tables[8][0],248,ulong.MaxValue);break;
            }
            var restored=M0c2S4Fixture.Create();Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(doc.Encode(),true),fault);
            Assert.Throws<InvalidOperationException>(()=>restored.Step(default));
        }
        [Test] public void SlotAndInputOrderDoNotAffectLifeOrNotices()
        {
            var a=M0c2S4Fixture.Create();var b=M0c2S4Fixture.Create(reverse:true);
            M0c2S4Fixture.Step(a,8);M0c2S4Fixture.Step(b,8);
            M0c2S4Fixture.Step(a,1,M0c2S4Fixture.Cast(actor:M0c2S4Fixture.Other),M0c2S4Fixture.Cast());
            M0c2S4Fixture.Step(b,1,M0c2S4Fixture.Cast(),M0c2S4Fixture.Cast(actor:M0c2S4Fixture.Other));
            Assert.That(a.StateHashRetained(),Is.EqualTo(b.StateHashRetained()));
            for(int i=0;i<8;i++){M0c2S4Fixture.Step(a);M0c2S4Fixture.Step(b);Assert.That(a.StateHashRetained(),Is.EqualTo(b.StateHashRetained()));}
        }
        [Test,Category("S4RestoreInvariant")] public void LostDownedRowLeavesOrphanSchedulerTaskAndIsRejected()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1);var doc=new M0c2S2ProtocolTests.Document(s);
            int at=doc.Tables[2].FindIndex(x=>M0c2S2ProtocolTests.Word(x,1)==M0c2S4Fixture.Actor.Id.Value);var row=doc.Tables[2][at];
            Array.Resize(ref row,58);M0c2S2ProtocolTests.Put(row,33,2);M0c2S2ProtocolTests.Put(row,49,0);row[57]=0;doc.Tables[2][at]=row;
            Assert.Throws<SimSnapshotException>(()=>M0c2S4Fixture.Create().RestoreRetainedSnapshot(doc.Encode(),true));
            // Forge an otherwise coherent Downed row that illegally retains a default-disallowed cast.
            var casting=M0c2S4Fixture.Create();M0c2S4Fixture.Step(casting,0,M0c2S4Fixture.Cast());
            TaskKey key=casting.Scheduler.ScheduleFor(5,0,M0c2S4Fixture.Actor.Id,0,new StableSimEvent(GameplayEvents.DownedExpiry,target:new EntityLink(M0c2S4Fixture.Actor.Id)));
            var forged=new M0c2S2ProtocolTests.Document(casting);int owner=forged.Tables[2].FindIndex(x=>M0c2S2ProtocolTests.Word(x,1)==M0c2S4Fixture.Actor.Id.Value);
            byte[] active=forged.Tables[2][owner];Array.Resize(ref active,98);M0c2S2ProtocolTests.Put(active,33,1);M0c2S2ProtocolTests.Put(active,41,1);M0c2S2ProtocolTests.Put(active,49,5);active[57]=1;
            M0c2S2ProtocolTests.Put(active,58,5);M0c2S2ProtocolTests.Put(active,66,0);M0c2S2ProtocolTests.Put(active,74,key.Origin.Value);M0c2S2ProtocolTests.Put(active,82,key.Counter);M0c2S2ProtocolTests.Put(active,90,0);
            forged.Tables[2][owner]=active;M0c2S2ProtocolTests.Put(forged.Tables[0][0],0,1);
            byte[] resource=forged.Tables[3].Find(x=>M0c2S2ProtocolTests.Word(x,1)==M0c2S4Fixture.Actor.Id.Value);M0c2S2ProtocolTests.Put(resource,25,0);
            Assert.Throws<SimSnapshotException>(()=>M0c2S4Fixture.Create().RestoreRetainedSnapshot(forged.Encode(),true),"Downed must not retain a disallowed cast");
        }
        [Test] public void LifeRuleChangeRejectsOldSnapshot()
        {
            var s=M0c2S4Fixture.Create();M0c2S4Fixture.Step(s,1);
            Assert.Throws<SimSnapshotException>(()=>M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(duration:5)).RestoreRetainedSnapshot(s.SaveRetainedSnapshot(),true));
        }
    }
}
