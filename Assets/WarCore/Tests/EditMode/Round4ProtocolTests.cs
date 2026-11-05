using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Protocol")]
    public sealed class Round4ProtocolTests
    {
        [Test]
        public void RetainedV4AndSnapshotV6MatchIndependentCanonicalBytes()
        {
            var sim = Simulation.CreateRetainedWorld(123);
            sim.Entities.Create(StableEntityKey.Placed(0,0));
            Assert.That(sim.StateHashRetained(),Is.EqualTo(0x9772974EA0955AD1UL));
            byte[] snapshot = sim.SaveRetainedSnapshot();
            Assert.That(snapshot.Length,Is.EqualTo(843));
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Assert.That(BitConverter.ToString(sha.ComputeHash(snapshot)).Replace("-","").ToLowerInvariant(),
                    Is.EqualTo("74d63fbf3939c52f26a696e16cbea69ad5b66376dd8885909d3de9176a6f1b0f"));
            TestContext.WriteLine("Retained independent vectors: state-v4=0x9772974EA0955AD1; snapshot-v6-bytes=843; sha256=74d63fbf3939c52f26a696e16cbea69ad5b66376dd8885909d3de9176a6f1b0f");
        }

        internal static Simulation World(long retention = 2, int timers = 4, int capacity = 16)
            => Simulation.CreateRetainedWorld(41,new SimConfig(entityCapacity:capacity,timerCapacity:timers),new RetainedWorldOptions(retention));

        [Test]
        public void RetainedDefaultsAreFiniteAndProtocolEntrypointsCannotBeMixed()
        {
            var sim = Simulation.CreateRetainedWorld(1);
            Assert.That(sim.Entities.RetiredRetentionTicks,Is.EqualTo(32));
            Assert.That(sim.Entities.IdentityCapacity,Is.EqualTo(4*sim.Entities.Capacity));
            Assert.Throws<SimBoundaryException>(() => sim.StateHashStable());
            Assert.Throws<SimBoundaryException>(() => sim.SaveStableSnapshot());
            Assert.Throws<SimBoundaryException>(() => new Simulation(1,identityMode:IdentityMode.StableWorld).StateHashRetained());
            var reader = new SimStateReader(sim.SaveRetainedSnapshot(),sim.Entities);
            reader.Int32();
            Assert.That(reader.Int32(),Is.EqualTo(6));
        }

        [Test]
        public void DeadSpawnerValuesSurviveSnapshotAndAReusedPhysicalSlot()
        {
            var sim = World(0);
            EntityId source = sim.Entities.Create(StableEntityKey.Placed(7,0));
            EntityLink link = sim.Entities.Link(source);
            var values = new SpawnValues(SimVec3.Metres(4,5,6),SimVec3.Metres(0,0,1),17);
            sim.Spawns.Enqueue(link,Round4DeathTests.Key(),values);
            sim.Entities.Destroy(source);
            sim.CompleteSpatialBoundary();
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.Zero);
            EntityId replacement = sim.Entities.Create(StableEntityKey.Placed(8,0));
            Assert.That(replacement.Id,Is.EqualTo(source.Id));
            ulong before = sim.StateHashRetained();
            byte[] data = sim.SaveRetainedSnapshot();
            sim.RestoreRetainedSnapshot(data,true);
            Assert.That(sim.StateHashRetained(),Is.EqualTo(before));
            sim.Step(default);
            EntityId child = sim.Entities.Resolve(new EntityLink(StableEntityKey.Spawned(link.Id,0,9).Id));
            SpawnCapture capture = sim.Entities.Read(sim.Entities.Resolve<SpawnCapture>(),child);
            Assert.That(capture.Spawner,Is.EqualTo(link));
            Assert.That(capture.Values.Position,Is.EqualTo(SimVec3.Metres(4,5,6)));
            Assert.That(capture.Values.Facing,Is.EqualTo(SimVec3.Metres(0,0,1)));
            Assert.That(capture.Values.Ownership,Is.EqualTo(17));
            Assert.That(capture.Kind,Is.EqualTo(9));
            Assert.That(capture.Chunk,Is.EqualTo(7));
            sim.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
            Assert.That(sim.Entities.Read(sim.Entities.Resolve<SpawnCapture>(),sim.Entities.Resolve(new EntityLink(StableEntityKey.Spawned(link.Id,0,9).Id))).Spawner,Is.EqualTo(link));
        }

        [Test]
        public void RetiredSpawnSequenceMatchesCommitBeforeDeathAfterSlotReuse()
        {
            var a = World(10);
            var b = World(10);
            var sourceKey = StableEntityKey.Placed(7,0);
            var source = new EntityLink(sourceKey.Id);
            var values = new SpawnValues(SimVec3.Metres(4,5,6));
            foreach (Simulation sim in new[] { a,b })
            {
                sim.Entities.Create(sourceKey);
                sim.Spawns.Enqueue(source,Round4DeathTests.Key(0),values);
                sim.Spawns.Commit(); // First child consumes sequence zero while the source lives.
                sim.Spawns.Enqueue(source,Round4DeathTests.Key(1),values);
            }
            a.Spawns.Commit();
            foreach (Simulation sim in new[] { a,b })
            {
                EntityId old = sim.Entities.Resolve(source);
                sim.Entities.Destroy(old);
                sim.CompleteSpatialBoundary();
                EntityId replacement = sim.Entities.Create(StableEntityKey.Placed(8,0));
                Assert.That(replacement.Id,Is.EqualTo(old.Id));
            }
            b.Spawns.Commit(); // Uses the retired counter, not the replacement slot's zero.
            Assert.That(b.Entities.TryResolve(StableEntityKey.Spawned(source.Id,1,9).Id,out _),Is.True);
            Assert.That(b.StateHashRetained(),Is.EqualTo(a.StateHashRetained()));
            b.RestoreRetainedSnapshot(b.SaveRetainedSnapshot(),true);
            Assert.That(b.StateHashRetained(),Is.EqualTo(a.StateHashRetained()));
        }

        [Test]
        public void ReclamationKeepsExactlyOneBucketPerRemainingIdentity()
        {
            var sim = World(0);
            EntityId first = sim.Entities.Create(StableEntityKey.Placed(1,1));
            var remaining = StableEntityKey.Placed(1,2);
            sim.Entities.Create(remaining);
            sim.Entities.Destroy(first);
            sim.CompleteSpatialBoundary();
            // Inspect the bounded index before a corrupt duplicate can make the next lookup loop forever.
            var field = typeof(EntityStore).GetField("identityBuckets",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var buckets = (int[])field.GetValue(sim.Entities);
            int occupied = 0;
            foreach (int index in buckets)
                if (index != 0)
                {
                    occupied++;
                    Assert.That(index,Is.InRange(1,sim.Entities.RegisteredIdentityCount));
                }
            Assert.That(occupied,Is.EqualTo(sim.Entities.RegisteredIdentityCount));
            Assert.That(sim.Entities.TryResolve(remaining.Id,out _),Is.True);
        }

        [Test]
        public void ReclamationInvalidatesTheCachedIdentityOrderBeforeSnapshot()
        {
            var sim = World(1);
            var first = StableEntityKey.Placed(1,1);
            var second = StableEntityKey.Placed(1,2);
            // Retire the larger key stored at index zero; the smaller key moves from index one.
            var larger = first.Id.CompareTo(second.Id) > 0 ? first : second;
            var smaller = first.Id.CompareTo(second.Id) > 0 ? second : first;
            EntityId dead = sim.Entities.Create(larger);
            sim.Entities.Create(smaller);
            sim.Entities.Destroy(dead);
            sim.SaveRetainedSnapshot(); // Cache the order after Destroy, before reclamation.
            sim.Step(default);
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(1));
            var restored = World(1);
            restored.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot());
            Assert.That(restored.Entities.TryResolve(smaller.Id,out _),Is.True);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
        }

        [Test]
        public void ATaskOutlivesTheRetiredOriginWithoutRetainingItsCursor()
        {
            var sim = World(1,1);
            EntityId source = sim.Entities.Create(StableEntityKey.World(1,0,0));
            EntityLink link = sim.Entities.Link(source);
            TaskKey key = sim.Scheduler.ScheduleFor(3,0,link.Id,1,new StableSimEvent(new EventTypeId(100),link));
            sim.Entities.Destroy(source);
            sim.Step(default);
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.Zero);
            ulong expected = sim.StateHashRetained();
            sim.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
            Assert.That(sim.StateHashRetained(),Is.EqualTo(expected));
            Assert.Throws<SimBoundaryException>(() => sim.Scheduler.ScheduleFor(4,0,link.Id,1,new StableSimEvent(new EventTypeId(100))));
            sim.Step(default);
            sim.Step(default);
            Assert.That(sim.Scheduler.TryDequeueStable(out StableScheduledTask task),Is.True);
            Assert.That(task.Key,Is.EqualTo(key));
            Assert.That(task.Event.Source,Is.EqualTo(link));
            Assert.DoesNotThrow(() => sim.StateHashRetained());
        }

        [Test]
        public void IdentityExhaustionIsExplicitAndDoesNotGrowOrConsumeState()
        {
            var sim = Simulation.CreateRetainedWorld(1,new SimConfig(entityCapacity:4),new RetainedWorldOptions(4,4));
            for (ulong i = 0; i < 4; i++)
                sim.Entities.Destroy(sim.Entities.Create(StableEntityKey.World(1,0,i)));
            sim.CompleteSpatialBoundary();
            ulong hash = sim.StateHashRetained();
            var error = Assert.Throws<SimBoundaryException>(() => sim.Entities.Create(StableEntityKey.World(1,1,0)));
            Assert.That(error.Message,Does.Contain("Identity capacity"));
            Assert.That(sim.StateHashRetained(),Is.EqualTo(hash));
            Assert.That(sim.Entities.IdentityCapacity,Is.EqualTo(4));
        }

        [Test]
        public void RetiredInputTargetHasAnObservableCountAndDiagnosticStableId()
        {
            var sim = World(0);
            EntityId target = sim.Entities.Create(StableEntityKey.World(1,0,0));
            EntityLink link = sim.Entities.Link(target);
            sim.Entities.Destroy(target);
            sim.CompleteSpatialBoundary();
            var input = new StableInputFrame(new InputFrame(default,default,
                squad:new SquadCommand(SquadCommandKind.Follow,1,default,default)),link);
            sim.StepStable(input);
            Assert.That(sim.Context.Input.Squad.Value.TargetEntity.Generation,Is.Zero);
            Assert.That(sim.RequestDiagnostics.Count(StableRequestKind.InputTarget),Is.EqualTo(1));
            StableSimEvent notice = sim.ReadStableEvent(1,0);
            Assert.That(notice.Type.Value,Is.EqualTo(RequestDiagnostics.EventType));
            Assert.That(notice.Tag,Is.EqualTo((int)StableRequestKind.InputTarget));
            Assert.That(notice.Source,Is.EqualTo(link));
            ulong hash = sim.StateHashRetained();
            sim.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
            Assert.That(sim.StateHashRetained(),Is.EqualTo(hash));
            Assert.That(sim.RequestDiagnostics.Total,Is.EqualTo(1));
        }

        [Test]
        public void TaskKeyFieldOrderAndRestoreEpochHaveIndependentExamples()
        {
            var a = new TaskKey(4,0,new StableId(99),7,2);
            var b = new TaskKey(4,1,new StableId(0),0,1);
            Assert.That(a.CompareTo(b),Is.LessThan(0));
            a = new TaskKey(4,0,new StableId(99),1,9);
            b = new TaskKey(4,0,new StableId(99),2,1);
            Assert.That(a.CompareTo(b),Is.LessThan(0));
            var sim = World();
            EntityId old = sim.Entities.Create(StableEntityKey.World(1,0,0));
            byte[] snapshot = sim.SaveRetainedSnapshot();
            for (int i = 0; i < 7; i++)
            {
                sim.Entities.Destroy(old);
                sim.CompleteSpatialBoundary();
                old = sim.Entities.Create(StableEntityKey.World(2,i,0));
            }
            sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(sim.Entities.IsAlive(old),Is.False);
            EntityId restored = sim.Entities.Resolve(new EntityLink(StableEntityKey.World(1,0,0).Id));
            Assert.That(restored.Generation,Is.GreaterThan(old.Generation));
        }
    }
}
