using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableHardBoundaryTests
    {
        [Test]
        public void StableTextRejectsInvalidUnicodeBeforeWritingItsHeader()
        {
            var writer = new SimStateWriter(new EntityStore(1,IdentityMode.StableWorld),materialize:true);
            Assert.Throws<SimBoundaryException>(() => writer.String("\uD800"));
            Assert.That(writer.Length,Is.Zero);
            Assert.That(writer.ToArray(),Is.Empty);
        }
        [Test]
        public void StableTextChecksItsWholeByteBudgetBeforeWritingItsHeader()
        {
            var writer = new SimStateWriter(new EntityStore(1,IdentityMode.StableWorld),materialize:true);
            long before = int.MaxValue-8L;
            // A near-full sink exercises the actual byte boundary without allocating a multi-gigabyte fixture.
            typeof(SimStateWriter).GetField("<Length>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(writer,before);
            Assert.Throws<SimBoundaryException>(() => writer.String("a"));
            Assert.That(writer.Length,Is.EqualTo(before));
            Assert.That(writer.ToArray(),Is.Empty);
        }
        [Test]
        public void ContentNamesRejectInvalidUnicodeAndStableTextPreservesValidBytes()
        {
            Assert.Throws<SimBoundaryException>(() => new StableContentKey("\uD800",1));
            var store = new EntityStore(1,IdentityMode.StableWorld);
            var writer = new SimStateWriter(store,materialize:true);
            string value = new string('x',63)+"\uD83D\uDE80\u00E9";
            writer.String(value);
            var reader = new SimStateReader(writer.ToArray(),store);
            Assert.That(reader.String(),Is.EqualTo(value));
            reader.End();
        }
        [Test]
        public void UnknownWeakLinksFailBeforeInputOrTaskMutation()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var unknown = new EntityLink(new StableId(123));
            ulong before = sim.StateHashStable();
            Assert.Throws<SimBoundaryException>(() => sim.Scheduler.ScheduleFor(1,1,new StableId(1),1,
                new StableSimEvent(new EventTypeId(100),source:unknown)));
            Assert.That(sim.StateHashStable(),Is.EqualTo(before));
            var frame = new StableInputFrame(new InputFrame(default,default,squad:new SquadCommand(SquadCommandKind.Follow,1,default,default)),unknown);
            Assert.Throws<SimBoundaryException>(() => sim.StepStable(frame));
            Assert.That(sim.Clock.Tick,Is.Zero);
            Assert.That(sim.StateHashStable(),Is.EqualTo(before));
        }
        [Test]
        public void IdentityCapacityGuardRunsBeforeAnyLargeAllocation()
        {
            Assert.Throws<SimBoundaryException>(() => EntityStore.ValidateIdentityCapacity(int.MaxValue,int.MaxValue));
            Assert.Throws<SimBoundaryException>(() => EntityStore.ValidateIdentityCapacity(2,1));
            EntityStore.ValidateIdentityCapacity(10000,10000);
        }
        [Test]
        public void MandatorySpawnCounterCannotBeOmittedFromStableEntityRecords()
        {
            var store = new EntityStore(2,IdentityMode.StableWorld);
            store.Create(StableEntityKey.Placed(1,1));
            var writer = new SimStateWriter(store,materialize:true);
            store.SaveStableEntities(writer);
            byte[] data = writer.ToArray();
            var reader = new SimStateReader(data,store);
            Assert.That(reader.Int32(),Is.EqualTo(1));
            for (int i = 0; i < 8; i++)
                reader.UInt64();
            reader.String();
            reader.UInt64();
            reader.Int32();
            int at = data.Length-reader.Remaining;
            var missing = new byte[at+8];
            Array.Copy(data,missing,at);
            Assert.Throws<SimSnapshotException>(() => store.LoadStableEntities(new SimStateReader(missing,store),false));
        }
        [Test]
        public void FutureChunkRemainsColdUntilItsDeclaredActivationBoundary()
        {
            var sim = StableSnapshotTests.Scene(false);
            var link = new EntityLink(StableEntityKey.Placed(1,0).Id);
            sim.Entities.MakeCold(sim.Entities.Resolve(link));
            sim.Activations.Plan(3,1);
            sim.Activations.Deliver(1,sim.StableContent.Digest);
            sim.Step(default);
            Assert.That(sim.Entities.TryResolve(link.Id,out _),Is.False);
            sim.Step(default);
            Assert.That(sim.Entities.TryResolve(link.Id,out _),Is.False);
            sim.Step(default);
            Assert.That(sim.Entities.TryResolve(link.Id,out _),Is.True);
        }
        [Test]
        public void DuplicateIdentityInSnapshotRejectsBeforeReplacingTheEntityMap()
        {
            var sim = StableSnapshotTests.Scene(false);
            byte[] snapshot = sim.SaveStableSnapshot();
            var ids = new ulong[16];
            for (int i = 0; i < 16; i++)
                ids[i] = StableEntityKey.Placed(1,(ulong)i).Id.Value;
            Array.Sort(ids);
            byte[] needle = new byte[16];
            for (int b = 0; b < 8; b++)
                needle[b] = unchecked((byte)(ids[0]>>(8*b)));
            needle[8] = 1;
            int at = Find(snapshot,needle);
            Assert.That(at,Is.GreaterThan(0));
            Array.Copy(snapshot,at,snapshot,at+64,8);
            EntityId old = sim.Entities.Resolve(new EntityLink(new StableId(ids[0])));
            Assert.Throws<SimSnapshotException>(() => sim.RestoreStableSnapshot(snapshot,true));
            Assert.That(sim.Entities.IsAlive(old),Is.True);
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(16));
        }
        private static int Find(byte[] data,byte[] needle)
        {
            for (int i = 0; i <= data.Length-needle.Length; i++)
            {
                int at = 0;
                while (at < needle.Length && data[i+at] == needle[at])
                    at++;
                if (at == needle.Length)
                    return i;
            }
            return -1;
        }
        [Test]
        public void GenerationExhaustionRejectsColdDestroyAndRestoreBeforeMutation()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            byte[] data = sim.SaveStableSnapshot();
            var field = typeof(EntityStore).GetField("generations",BindingFlags.NonPublic|BindingFlags.Instance);
            ((ulong[])field.GetValue(sim.Entities))[entity.Id] = ulong.MaxValue;
            entity = new EntityId(entity.Id,ulong.MaxValue);
            Assert.Throws<SimBoundaryException>(() => sim.Entities.MakeCold(entity));
            Assert.Throws<SimBoundaryException>(() => sim.Entities.Destroy(entity));
            Assert.Throws<SimBoundaryException>(() => sim.RestoreStableSnapshot(data));
            Assert.That(sim.Entities.IsAlive(entity),Is.True);
            Assert.That(sim.Entities.LifeOf(StableEntityKey.Placed(1,1).Id),Is.EqualTo(IdentityLife.Active));
        }
        [Test]
        public void CanonicalIntegerAndUtf8ReadersRejectInvalidBytesExplicitly()
        {
            var store = new EntityStore(1,IdentityMode.StableWorld);
            var wide = new SimStateReader(new byte[] {0,0,0,128,0,0,0,0},store);
            Assert.Throws<SimSnapshotException>(() => wide.Int32());
            var text = new SimStateReader(new byte[] {1,0,0,0,0,0,0,0,255},store);
            Assert.Throws<SimSnapshotException>(() => text.String());
        }
    }
}
