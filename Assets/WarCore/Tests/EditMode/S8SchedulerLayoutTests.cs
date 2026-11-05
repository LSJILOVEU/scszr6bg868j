using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S8Layout")]
    public sealed class S8SchedulerLayoutTests
    {
        private static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        [Test]
        public void HeapCarriesOnlyTickIdAndSlotAndCancellationHasNoIdDictionary()
        {
            Type heap = typeof(SimScheduler).GetField("heap", BindingFlags.Instance | BindingFlags.NonPublic).FieldType.GetElementType();
            Assert.That(Marshal.SizeOf(typeof(ScheduledTask)), Is.GreaterThan(24), "Payload is the positive size control.");
            Assert.That(Fields(heap).Select(field => field.FieldType), Is.EquivalentTo(new[] { typeof(long), typeof(long), typeof(int) }));
            Assert.That(Marshal.SizeOf(heap), Is.LessThanOrEqualTo(24));
            Assert.That(Fields(typeof(SimScheduler)).Any(field => field.FieldType == typeof(Dictionary<long, int>)), Is.False);
        }

        private static object Schedule(SimScheduler scheduler, long tick, int type, int tag)
        {
            MethodInfo method = typeof(SimScheduler).GetMethod("ScheduleWithHandle");
            Assert.That(method, Is.Not.Null, "SimScheduler.ScheduleWithHandle");
            return method.Invoke(scheduler, new object[] { tick, new SimEvent(new EventTypeId(type), tag: tag) });
        }

        private static bool Cancel(SimScheduler scheduler, object handle)
        {
            MethodInfo method = typeof(SimScheduler).GetMethod("Cancel", new[] { handle.GetType() });
            Assert.That(method, Is.Not.Null, "Cancel(slot, generation handle)");
            return (bool)method.Invoke(scheduler, new[] { handle });
        }

        [Test]
        public void StaleSlotGenerationsCannotCancelReusedPendingOrDueTasks()
        {
            var scheduler = new SimScheduler(1);
            object old = Schedule(scheduler, 1, 41, 1);
            Assert.That(Cancel(scheduler, old), Is.True);
            object next = Schedule(scheduler, 1, 42, 2);
            Assert.That(next, Is.Not.EqualTo(old));
            Assert.That(Cancel(scheduler, old), Is.False);
            scheduler.Advance(1);
            Assert.That(Cancel(scheduler, old), Is.False);
            Assert.That(Cancel(scheduler, next), Is.True);
            Assert.That(Cancel(scheduler, next), Is.False);
            object last = Schedule(scheduler, 2, 42, 3);
            scheduler.Advance(2);
            Assert.That(scheduler.TryDequeue(new EventTypeId(42), out ScheduledTask task), Is.True);
            Assert.That(task.Id, Is.EqualTo(3));
            Assert.That(task.Event.Tag, Is.EqualTo(3));
            Assert.That(Cancel(scheduler, last), Is.False);
        }

        [Test]
        public void MixedLegacyIdsAndHandlesPreserveTypedAndGlobalFifoAcrossReserve()
        {
            var scheduler = new SimScheduler(2);
            long legacy = scheduler.Schedule(1, new SimEvent(new EventTypeId(41), tag: 1));
            object canceled = Schedule(scheduler, 1, 42, 2);
            object later = Schedule(scheduler, 2, 41, 3);
            scheduler.Advance(1);
            scheduler.Reserve(16);
            Assert.That(Cancel(scheduler, canceled), Is.True);
            Assert.That(scheduler.Cancel(legacy), Is.True);
            Assert.That(scheduler.DueCount, Is.Zero);
            Assert.That(scheduler.TryGetPending(3, out ScheduledTask pending), Is.True);
            Assert.That(pending.Event.Tag, Is.EqualTo(3));
            scheduler.Advance(2);
            Assert.That(scheduler.TryDequeue(new EventTypeId(41), out ScheduledTask task), Is.True);
            Assert.That(task.Id, Is.EqualTo(3));
            Assert.That(Cancel(scheduler, later), Is.False);
        }
    }
}
