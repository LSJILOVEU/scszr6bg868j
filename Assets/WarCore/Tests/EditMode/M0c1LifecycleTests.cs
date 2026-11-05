using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c1"), Category("M0c1Mutation")]
    public sealed class M0c1LifecycleTests
    {
        private static StableEntityKey Parent => M0c1Fixture.Parent;
        private static EntityLink Link => new EntityLink(Parent.Id);
        [TestCase(0,false),TestCase(1,false),TestCase(32,false)]
        [TestCase(0,true),TestCase(1,true),TestCase(32,true)]
        public void RecreateAfterRetentionMustNotCollideWithLivingChild(int retention, bool restore)
        {
            var sim=M0c1Fixture.Create(out var system,retention);
            sim.Entities.Create(Parent); system.Spawn=true; sim.Step(default);
            sim.Entities.Destroy(sim.Entities.Resolve(Link));
            for(int i=0;i<=retention;i++) sim.Step(default);
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(1));
            Assert.That(sim.Placed.IsDestroyed(Parent),Is.True);
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Entities.Create(Parent)).Message,Does.Contain("M0C_PLACED_DESTROYED"));
            if(restore)
            {
                byte[] bytes=sim.SaveRetainedSnapshot(); ulong hash=sim.StateHashRetained();
                sim=M0c1Fixture.Create(out system,retention); sim.RestoreRetainedSnapshot(bytes,true);
                Assert.That(sim.StateHashRetained(),Is.EqualTo(hash));
            }
            system.Restore=true; system.Recreate=true; system.Spawn=true; Assert.DoesNotThrow(()=>sim.Step(default));
            Assert.That(sim.Placed.Generation(Parent),Is.EqualTo(1));
            Assert.That(sim.Spawns.Counter(Link),Is.EqualTo((1UL<<48)+1));
            Assert.That(sim.Entities.TryResolve(StableEntityKey.Spawned(Parent.Id,0,9).Id,out _),Is.True);
            Assert.That(sim.Entities.TryResolve(StableEntityKey.Spawned(Parent.Id,1UL<<48,9).Id,out _),Is.True);
            Assert.That(sim.Entities.TaskCounter(Parent.Id,false),Is.EqualTo((1UL<<48)+1));
            TestContext.WriteLine("B1 green|R="+retention+"; restore="+restore+"; generation=1; living_children=2");
        }
        [TestCase(0,false),TestCase(1,false),TestCase(32,false)]
        [TestCase(0,true),TestCase(1,true),TestCase(32,true)]
        public void ColdUnloadPreservesCursorsAndBits(int retention,bool restore)
        {
            var sim=M0c1Fixture.Create(out var system,retention);
            sim.Entities.Create(Parent); system.Spawn=true; sim.Step(default);
            sim.Entities.MakeCold(sim.Entities.Resolve(Link));
            for(int i=0;i<=retention;i++)sim.Step(default);
            if(restore) {byte[] b=sim.SaveRetainedSnapshot(); sim=M0c1Fixture.Create(out system,retention);sim.RestoreRetainedSnapshot(b,true);}
            Assert.That(sim.Placed.IsDestroyed(Parent),Is.False);
            sim.Entities.Reactivate(Parent.Id); system.Spawn=true;sim.Step(default);
            Assert.That(sim.Placed.Generation(Parent),Is.Zero);
            Assert.That(sim.Spawns.Counter(Link),Is.EqualTo(2));
            Assert.That(sim.Entities.TaskCounter(Parent.Id,false),Is.EqualTo(2));
        }
        [TestCase(0),TestCase(1),TestCase(32)]
        public void TooEarlyFailsUntilRecordActuallyReclaimed(int retention)
        {
            var sim=M0c1Fixture.Create(out var system,retention);
            sim.Entities.Create(Parent);system.Destroy=true;
            // Destroy inside this tick, after warmup reclamation.
            system.Restore=true;
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Step(default)).Message,Does.Contain("M0C_RESTORE_TOO_EARLY"));
            Assert.That(sim.Placed.Generation(Parent),Is.Zero);
            Assert.That(sim.Placed.IsDestroyed(Parent),Is.True);
        }
        [Test]
        public void ThreeGenerationsKeepOldChildrenAndTasks()
        {
            var sim=M0c1Fixture.Create(out var sys,0);sim.Entities.Create(Parent);
            for(ushort gen=0;gen<3;gen++)
            {
                sys.Spawn=true;sim.Step(default);
                Assert.That(sim.Spawns.Counter(Link),Is.EqualTo(((ulong)gen<<48)+1));
                Assert.That(sim.Entities.TaskCounter(Parent.Id,false),Is.EqualTo(((ulong)gen<<48)+1));
                sim.Entities.Destroy(sim.Entities.Resolve(Link));sim.Step(default);
                sys.Restore=true;sys.Recreate=true;sim.Step(default);
            }
            Assert.That(sim.Placed.Generation(Parent),Is.EqualTo(3));
            for(ulong gen=0;gen<3;gen++)Assert.That(sim.Entities.TryResolve(StableEntityKey.Spawned(Parent.Id,gen<<48,9).Id,out _),Is.True);
            byte[] b=sim.SaveRetainedSnapshot();ulong hash=sim.StateHashRetained();
            var other=M0c1Fixture.Create(out _,0);other.RestoreRetainedSnapshot(b,true);
            Assert.That(other.StateHashRetained(),Is.EqualTo(hash));
        }
        [Test]
        public void GenerationLimitFailsWithoutWrapOrBitChange()
        {
            var empty=M0c1Fixture.Create(out _);
            var initial=new InitialPlacedState(empty.Placed.Catalog.Digest,new[]{0},new[]{new PlacedGeneration(0,ushort.MaxValue)});
            var sim=M0c1Fixture.Create(out var sys,initial:initial);sys.Restore=true;
            ulong digest=sim.Placed.GenerationDigest;
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Step(default)).Message,Does.Contain("M0C_RESTORE_GEN_EXHAUSTED"));
            Assert.That(sim.Placed.Generation(Parent),Is.EqualTo(ushort.MaxValue));
            Assert.That(sim.Placed.GenerationDigest,Is.EqualTo(digest));Assert.That(sim.Placed.IsDestroyed(Parent),Is.True);
        }
        [TestCase(0),TestCase(1),TestCase(65535)]
        public void CursorLimitFailsBeforeCrossingGeneration(int gen)
        {
            var empty=M0c1Fixture.Create(out _);
            var initial=new InitialPlacedState(empty.Placed.Catalog.Digest,new int[0],gen==0 ? null:new[]{new PlacedGeneration(0,(ushort)gen)});
            var sim=M0c1Fixture.Create(out _,initial:initial);EntityId e=sim.Entities.Create(Parent);
            ulong end=((ulong)gen<<48)|((1UL<<48)-1);
            sim.Entities.SetSpawnSequence(e,end-1);sim.Spawns.Enqueue(Link,new StableRequestKey(1,0,Parent.Id,0,0,9));sim.Step(default);
            Assert.That(sim.Spawns.Counter(Link),Is.EqualTo(end));
            sim.Spawns.Enqueue(Link,new StableRequestKey(1,0,Parent.Id,0,0,9));
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Spawns.Commit()).Message,Is.EqualTo("Spawn counter exhausted."));
            sim.Entities.SetTaskCounter(Parent.Id,end-1);
            var key=sim.Scheduler.ScheduleFor(2000,0,Parent.Id,9,new StableSimEvent(new EventTypeId(100)));
            Assert.That(key.Counter,Is.EqualTo(end-1));
            Assert.Throws<SimBoundaryException>(()=>sim.Scheduler.ScheduleFor(2000,0,Parent.Id,9,new StableSimEvent(new EventTypeId(100))));
            Assert.That(sim.Entities.TaskCounter(Parent.Id,false),Is.EqualTo(end));
        }
        [Test]
        public void PermanentRestoreAndOffPhaseRestoreFail()
        {
            var sim=M0c1Fixture.Create(out var system,0,0);sim.Entities.Destroy(sim.Entities.Create(Parent));sim.Step(default);
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Context.RestorePlaced(Parent)).Message,Does.Contain("M0C_RESTORE_PHASE"));
            system.Restore=true;
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Step(default)).Message,Does.Contain("M0C_RESTORE_POLICY"));
            Assert.That(sim.Placed.IsDestroyed(Parent),Is.True);
        }
        [Test]
        public void RestoreRequiresDestroyedAndDestroyRejectsStaleHandleBeforeMarking()
        {
            var sim=M0c1Fixture.Create(out var system,0);
            EntityId entity=sim.Entities.Create(Parent);sim.Entities.MakeCold(entity);sim.Entities.Reactivate(Parent.Id);
            Assert.Throws<InvalidOperationException>(()=>sim.Entities.Destroy(entity));
            Assert.That(sim.Placed.DestroyedCount,Is.Zero);
            var empty=M0c1Fixture.Create(out system);system.Restore=true;
            Assert.That(Assert.Throws<SimBoundaryException>(()=>empty.Step(default)).Message,Does.Contain("M0C_RESTORE_NOT_DESTROYED"));
        }
        [TestCase(0),TestCase(1),TestCase(32)]
        public void RetirementBoundaryPreservesBitAtRMinusOneRAndRPlusOne(int retention)
        {
            var sim=M0c1Fixture.Create(out var system,retention);sim.CompleteWarmup();
            sim.Entities.Destroy(sim.Entities.Create(Parent));
            for(int tick=1;tick<=retention+1;tick++)
            {
                sim.Step(default);Assert.That(sim.Placed.IsDestroyed(Parent),Is.True);
                Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(tick<retention?1:0));
            }
            system.Restore=true;system.Recreate=true;Assert.DoesNotThrow(()=>sim.Step(default));
            Assert.That(sim.Spawns.Counter(Link),Is.EqualTo(1UL<<48));
        }
        [Test]
        public void ExplicitTasksCannotReuseOrSelectAnotherGeneration()
        {
            var empty=M0c1Fixture.Create(out _);
            var initial=new InitialPlacedState(empty.Placed.Catalog.Digest,new int[0],new[]{new PlacedGeneration(0,1)});
            var sim=M0c1Fixture.Create(out _,initial:initial);sim.Entities.Create(Parent);
            foreach(ulong ordinal in new[]{0UL,2UL<<48})
                Assert.Throws<SimBoundaryException>(()=>sim.Scheduler.ScheduleStable(new TaskKey(2000,0,Parent.Id,ordinal,9),new StableSimEvent(new EventTypeId(100))));
            var issued=sim.Scheduler.ScheduleFor(2000,0,Parent.Id,9,new StableSimEvent(new EventTypeId(100)));sim.Scheduler.CancelStable(issued);
            Assert.Throws<SimBoundaryException>(()=>sim.Scheduler.ScheduleStable(issued,new StableSimEvent(new EventTypeId(100))));
            Assert.That(sim.Entities.TaskCounter(Parent.Id,false),Is.EqualTo((1UL<<48)+1));
        }
    }
}
