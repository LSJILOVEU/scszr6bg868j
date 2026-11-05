using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class ExtensionContractTests
    {
        [Test]
        public void ControlSwitchRoutesTheWholeInputFrameToTheNewEntityAndEmitsOnce()
        {
            var f = new ExtensionFixture();
            f.P.Sim.Step(new InputFrame(SimVec3.Metres(1, 0, 0), default, skillSlot: 1));
            Assert.That(f.Input.LastTarget, Is.EqualTo(f.P.Unit));
            var input = new InputFrame(SimVec3.Metres(2, 3, 4), SimVec3.Metres(-1, 2, 0), true, true, true, 2,
                new SquadCommand(SquadCommandKind.Follow, 7, default, f.Second));
            f.P.Sim.Step(input);
            Assert.That(f.Control.Current, Is.EqualTo(f.Second));
            Assert.That(f.Input.LastInput, Is.EqualTo(input));
            Assert.That(f.Input.LastTarget, Is.EqualTo(f.Second));
            Assert.That(f.P.Sim.Entities.Get(f.Masses, f.P.Unit).Value, Is.EqualTo(1));
            Assert.That(f.P.Sim.Entities.Get(f.Masses, f.Second).Value, Is.EqualTo(2));
            Assert.That(Count(f.P.Sim.Events, 2, f.SwitchType.Value), Is.EqualTo(1));
            SimEvent change = f.P.Sim.Events.Read(2)[0];
            Assert.That(change.Source, Is.EqualTo(f.P.Unit));
            Assert.That(change.Target, Is.EqualTo(f.Second));
            f.P.Sim.Step(input);
            Assert.That(Count(f.P.Sim.Events, 3, f.SwitchType.Value), Is.Zero);
            ulong before = f.P.Sim.StateHash();
            Assert.Throws<InvalidOperationException>(() => f.Control.Switch(f.P.Sim.Context, f.P.Unit));
            Assert.That(f.P.Sim.StateHash(), Is.EqualTo(before));
            f.P.Sim.Entities.Destroy(f.Second);
            f.P.Sim.Step(default);
            Assert.That(f.Input.Calls, Is.EqualTo(3));
        }

        internal static int Count(ISimEventReader events, long tick, int type)
        {
            int count = 0;
            foreach (SimEvent item in events.Read(tick))
                if (item.Type.Value == type)
                    count++;
            return count;
        }

        [Test]
        public void CollapseIsStrictlyBelowThresholdLatchedBeforeTriggersAndNotRepeatedAfterRestore()
        {
            var f = new ExtensionFixture();
            f.P.Sim.Entities.Set(f.Healths, f.Second, new StructureHealth(SimFix.FromInt(5), SimFix.FromInt(5)));
            f.P.Sim.Step(default);
            Assert.That(f.P.Sim.Entities.Get(f.Healths, f.Second).Collapsed, Is.False);
            Assert.That(Count(f.P.Sim.Events, 1, EventTypeRegistry.WorldChange), Is.Zero);
            f.P.Sim.Entities.Set(f.Healths, f.Second, f.P.Sim.Entities.Get(f.Healths, f.Second).WithHealth(SimFix.FromInt(4)));
            f.P.Sim.Step(default);
            Assert.That(f.P.Sim.Entities.Get(f.Healths, f.Second).Collapsed, Is.True);
            Assert.That(f.Triggers.Get(0).Fired, Is.True);
            Assert.That(f.Triggers.Get(0).FiredTick, Is.EqualTo(2));
            Assert.That(f.P.Sim.Entities.Get(f.Containers, f.P.Unit).Count, Is.EqualTo(3));
            Assert.That(Count(f.P.Sim.Events, 2, EventTypeRegistry.WorldChange), Is.EqualTo(1));
            SimEvent item = f.P.Sim.Events.Read(2)[0];
            Assert.That(item.Position, Is.EqualTo(f.P.Sim.Entities.Get(f.P.Positions, f.Second).Value));
            Assert.That(item.Aux, Is.EqualTo(new SimVec3(SimFix.Ratio(1, 4), SimFix.Ratio(3, 2), SimFix.Ratio(1, 4))));
            Assert.That(item.Tag, Is.EqualTo(71));
            byte[] snapshot = f.P.Sim.SaveSnapshot();
            var restored = new ExtensionFixture();
            restored.P.Sim.RestoreSnapshot(snapshot);
            for (int tick = 3; tick <= 7; tick++)
            {
                restored.P.Sim.Step(default);
                Assert.That(Count(restored.P.Sim.Events, tick, EventTypeRegistry.WorldChange), Is.Zero);
            }

            Assert.That(restored.Action.Calls, Is.EqualTo(2));
        }

        [Test]
        public void DroppedCollapseNotificationCannotPreventAuthoritativeTriggerOrCauseARetry()
        {
            var f = new ExtensionFixture();
            f.Commands.Flood = true;
            f.P.Sim.Step(default);
            Assert.That(f.P.Sim.Events.EventsDroppedTotal, Is.GreaterThan(0));
            Assert.That(Count(f.P.Sim.Events, 1, EventTypeRegistry.WorldChange), Is.Zero);
            Assert.That(f.P.Sim.Entities.Get(f.Healths, f.Second).Collapsed, Is.True);
            Assert.That(f.Triggers.Get(0).Fired, Is.True);
            Assert.That(f.Action.Calls, Is.EqualTo(1));
            f.Commands.Flood = false;
            f.P.Sim.Step(default);
            Assert.That(Count(f.P.Sim.Events, 2, EventTypeRegistry.WorldChange), Is.Zero);
            Assert.That(f.Action.Calls, Is.EqualTo(1));
        }

        [Test]
        public void PresentationSubscriberOwnStateAndCampaignBytesNeverChangeMatchHashOrSnapshot()
        {
            var f = new ExtensionFixture();
            f.P.Sim.Step(default);
            var view = new EventView();
            var campaign = new TestCampaign();
            byte[] snapshot = f.P.Sim.SaveSnapshot();
            ulong hash = f.P.Sim.StateHash();
            view.Read(f.P.Sim.Events, 1);
            campaign.LoadCampaign(new byte[] { 9, 2, 7 });
            view.Counter += 100;
            Assert.That(view.Counter, Is.EqualTo(101));
            Assert.That(f.P.Sim.StateHash(), Is.EqualTo(hash));
            Assert.That(f.P.Sim.SaveSnapshot(), Is.EqualTo(snapshot));
            f.P.Sim.RestoreSnapshot(snapshot);
            Assert.That(campaign.SaveCampaign(), Is.EqualTo(new byte[] { 9, 2, 7 }));
            Assert.That(view.Counter, Is.EqualTo(101));
        }

        private sealed class EventView
        {
            internal int Counter;
            internal void Read(ISimEventReader reader, long tick)
            {
                foreach (SimEvent item in reader.Read(tick))
                    if (item.Type.Value == EventTypeRegistry.WorldChange)
                        Counter++;
            }
        }

        private sealed class TestCampaign : ICampaignLayer
        {
            private byte[] bytes = new byte[0];
            public byte[] SaveCampaign() => (byte[])bytes.Clone();
            public void LoadCampaign(byte[] data)
            {
                bytes = (byte[])data.Clone();
            }
        }

        [Test]
        public void VisibilityAndIntelContractsKeepFactionAndObservationTimeExplicit()
        {
            IVisibilityProvider visibility = new TestPerception();
            IFactionIntel intel = new TestPerception();
            var entity = new EntityId(1, 1);
            Assert.That(visibility.IsVisible(3, entity, 7), Is.True);
            Assert.That(visibility.IsVisible(4, entity, 7), Is.False);
            Assert.That(intel.TryGet(3, entity, 8, out _), Is.False);
            Assert.That(intel.TryGet(3, entity, 9, out ObservedIntel observed), Is.True);
            Assert.That(observed.ObservedTick, Is.EqualTo(7));
            Assert.That(observed.Entity, Is.EqualTo(entity));
            Assert.That(observed.Confidence, Is.EqualTo(SimFix.Ratio(1, 2)));
        }

        private sealed class TestPerception : IVisibilityProvider, IFactionIntel
        {
            public bool IsVisible(int faction, EntityId target, long tick) => faction == 3 && tick >= 7;
            public bool TryGet(int faction, EntityId target, long tick, out ObservedIntel observation)
            {
                observation = default;
                if (faction != 3 || tick < 9)
                    return false;
                observation = new ObservedIntel(target, SimVec3.Metres(2, 3, 4), 7, SimFix.Ratio(1, 2));
                return true;
            }
        }

        [Test]
        public void SoundUsesFixedTransportAndRestoresPositionRadiusFactionAndTag()
        {
            var sim = Fixtures.Empty(1);
            EventTypeId type = new EventTypeRegistry().Register("sound");
            var sound = new SoundEvent(SimVec3.Metres(1, 2, 3), SimFix.FromInt(40), 3, 77, new EntityId(4, 9));
            sim.Scheduler.Schedule(1, sound.ToEvent(type));
            sim.Step(default);
            byte[] snapshot = sim.SaveSnapshot();
            sim.Step(default);
            sim.RestoreSnapshot(snapshot);
            SoundEvent restored = SoundEvent.FromEvent(sim.Events.Read(1)[0], type);
            Assert.That(restored.Position, Is.EqualTo(sound.Position));
            Assert.That(restored.LoudnessRadius, Is.EqualTo(sound.LoudnessRadius));
            Assert.That(restored.SourceFaction, Is.EqualTo(3));
            Assert.That(restored.TypeTag, Is.EqualTo(77));
            Assert.That(restored.Source, Is.EqualTo(sound.Source));
            Assert.Throws<ArgumentException>(() => SoundEvent.FromEvent(sound.ToEvent(type), new EventTypeId(23)));
            Assert.Throws<ArgumentException>(() => SoundEvent.FromEvent(new SimEvent(type, aux: new SimVec3(SimFix.FromRaw(long.MaxValue), default, default)), type));
        }

        [Test]
        public void WarmControlTriggersAndStructureChecksAllocateNothing()
        {
            var windows = new AllocationWindow();
            do
            {
                var f = new ExtensionFixture();
                for (int i = 0; i < 16; i++)
                    f.P.Sim.Step(default);
                var meter = new AllocationMeter();
                windows.Begin(meter);
                for (int i = 0; i < 256; i++)
                    f.P.Sim.Step(new InputFrame(default, default, skillSlot: i % 3));
                AllocationSample allocated = windows.End();
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                Assert.That(f.P.Sim.Events.EventsDroppedTotal, Is.Zero);
                TestContext.WriteLine("Extension warm control/trigger/structure Step: {0}; EventsDropped=0.", allocated.Describe());
            }
            while (windows.NeedsRetry);
        }
    }
}
