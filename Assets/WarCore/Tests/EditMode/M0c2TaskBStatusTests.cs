using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
using static WarCore.Tests.M0c2TaskBStatusFixture;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2TaskBStatus")]
    public sealed class M0c2TaskBStatusTests
    {
        [TestCase(1,"apply"),TestCase(2,"apply"),TestCase(1,"absorb"),TestCase(2,"absorb"),
         TestCase(1,"submit"),TestCase(2,"submit"),TestCase(1,"dispel"),TestCase(2,"dispel")]
        public void ForgedStatusProducerIsRejectedBeforeAnUnrestorableRowCanBeSaved(int kind,string entry)
        {
            var script=new ProducerScript((ulong)kind,entry);var sim=Create(script);sim.Step(default);
            byte[] bytes=sim.SaveRetainedSnapshot();ulong hash=sim.StateHashRetained();
            var restored=Create(new ProducerScript((ulong)kind,entry));
            Assert.DoesNotThrow(()=>restored.RestoreRetainedSnapshot(bytes,true),"A completed tick must save a restorable state");
            Assert.That(restored.StateHashRetained(),Is.EqualTo(hash));
            Assert.That(script.Rejection,Is.EqualTo("M0C_STATUS_PRODUCER_ADMISSION"),"Public status admission must reject reserved producer kinds");
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(0));
        }
        [TestCase("apply"),TestCase("absorb"),TestCase("submit")]
        public void ScriptProducerZeroStillAppliesAndRoundTrips(string entry)
        {
            var script=new ProducerScript(0,entry);var sim=Create(script);sim.Step(default);
            Assert.That(script.Rejection,Is.Null);Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            var restored=Create(new ProducerScript(0,entry));restored.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
        }
        [TestCase(GameplayStackPolicy.Stack,0),TestCase(GameplayStackPolicy.Stack,1),TestCase(GameplayStackPolicy.Stack,4),
         TestCase(GameplayStackPolicy.Refresh,0),TestCase(GameplayStackPolicy.Refresh,1),TestCase(GameplayStackPolicy.Refresh,4)]
        public void RepeatedSameTickStatusApplicationsHaveDistinctNoticeKeys(GameplayStackPolicy policy,int unrelated)
        {
            var sim=Create(new NoticeScript(unrelated),policy);sim.Step(default);var notices=Staged(sim);
            Assert.That(sim.Events.Read(1).Length,Is.EqualTo(3+unrelated));
            for(int i=0;i<3+unrelated;i++)for(int j=i+1;j<3+unrelated;j++)
                Assert.That(notices[i].CompareTo(notices[j]),Is.Not.EqualTo(0),"Each applied target/command needs a distinct phase-21 notice key");
            Assert.That(sim.Events.Read(1)[0].Tag,Is.EqualTo(1));
            Assert.That(sim.Events.Read(1)[1].Tag,Is.EqualTo(2));Assert.That(sim.Events.Read(1)[2].Tag,Is.EqualTo(2));
        }
        [Test] public void StatusNoticeV2MatchesIndependentKeysEventBytesAndHash()
        {
            var cases=VectorJson.Load("m0c2-s4-notice-v2.json")["cases"];
            for(int n=0;n<cases.Count;n++)
            {
                var row=cases[n];var policy=row["stack"].Boolean?GameplayStackPolicy.Stack:GameplayStackPolicy.Refresh;
                var sim=Create(new NoticeScript((int)row["unrelated"].Integer),policy);sim.Step(default);var notices=Staged(sim);
                var writer=new SimStateWriter(sim.Entities,materialize:true);
                for(int i=0;i<sim.Events.Read(1).Length;i++)
                {
                    var notice=notices[i];var key=notice.Key;
                    writer.Int32(notice.Phase);writer.UInt64(notice.Target.Value);writer.UInt64(key.Source.Value);writer.UInt64(key.CastToken);
                    writer.UInt64(key.ProducerKind);writer.UInt64(key.Producer.Value);writer.UInt64(key.ProducerToken);
                    writer.UInt64(key.ActionOrdinal);writer.UInt64(key.PulseOrdinal);writer.Int32(notice.Kind);
                    writer.StableEvent(((SimEventBuffer)sim.Events).ReadStable(1,i));
                }
                byte[] bytes=writer.ToArray();var hash=new StateHash();foreach(byte value in bytes)hash.Add(value);
                Assert.That(BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant(),Is.EqualTo(row["bytes"].Text),row["name"].Text+" bytes");
                Assert.That(hash.Value.ToString("X16"),Is.EqualTo(row["hash"].Text),row["name"].Text+" hash");
            }
        }
        [Test] public void StatusRemovalKeepsTheStoredRowProducerIdentity()
        {
            var sim=Create(new NoticeScript(0));sim.Step(default);sim.Step(default);
            var key=new GameplayApplyKey(Actor.Id,1,1,Actor.Id,0,1);
            Assert.That(Staged(sim)[0].Key.CompareTo(key),Is.EqualTo(0));
            Assert.That(Staged(sim)[0].Phase,Is.EqualTo(20));Assert.That(Staged(sim)[0].Event.Tag,Is.EqualTo(0));
        }
        private static GameplayNotice[] Staged(Simulation sim)
            =>(GameplayNotice[])typeof(GameplayState).GetField("orderedNotices",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(sim.Entities.Gameplay);
        internal sealed class ProducerScript:IRetainedGameplaySystem
        {
            private readonly ulong kind;private readonly string entry;internal string Rejection;
            internal ProducerScript(ulong kind,string entry){this.kind=kind;this.entry=entry;}
            public int Order=>0;public string StateTypeId=>"test.task-b.status";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                if(c.Clock.Tick!=1)return;
                var target=c.Owner.GameplayTarget(Actor.Id);var key=new GameplayApplyKey(Actor.Id,1,kind,Actor.Id,999,1);
                try
                {
                    if(entry=="apply")c.ApplyStatus(target,key,1);
                    else if(entry=="absorb")c.GrantAbsorb(target,key,1,5);
                    else if(entry=="dispel")c.Dispel(target,key,1);
                    else {Span<GameplayAction> one=stackalloc GameplayAction[1];one[0]=GameplayAction.ApplyStatus(target,key,1);c.SubmitGameplay(target,ReadOnlySpan<GameplayCost>.Empty,one);}
                }
                catch(SimBoundaryException e){Rejection=e.Message;}
            }
            public void SaveState(SimStateWriter w){}public void LoadState(SimStateReader r){}public void WriteState(StateHash h){}
        }
    }
}
