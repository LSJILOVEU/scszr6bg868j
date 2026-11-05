using System;
using WarCore.Sim;

namespace WarCore.Control
{
    // Retained-world adapter: the existing Sim system owns Current and its stable link.
    public sealed class PlayerControl : IControlTarget, IRetainedGameplaySystem
    {
        public const string SwitchEventName = "control.switch";
        public const int SwitchEventId = 1563519857;
        private readonly ControlTargetSystem inner;
        public EntityId Current => inner.Current;
        public EntityLink CurrentLink => inner.CurrentLink;
        public int Order => inner.Order;
        public string StateTypeId => inner.StateTypeId;
        public int StateVersion => inner.StateVersion;

        public PlayerControl(EntityStore entities, EntityId initial)
        {
            var registry = new EventTypeRegistry();
            registry.Register("events-dropped");
            registry.Register("world-change");
            EventTypeId type = registry.Register(SwitchEventName);
            // GameplayEvents reserves 1900201..1900213; request diagnostics uses 2147483646.
            // Lock the registered ID as well as the name, so a hash change cannot silently change this event.
            if (type.Value != SwitchEventId || (type.Value >= 1900201 && type.Value <= 1900213)
                || type.Value == 2147483646)
                throw new InvalidOperationException("Control switch event registration conflicts with the event contract.");
            inner = new ControlTargetSystem(entities, initial, new EmptyInputHandler(), type, 7);
        }

        public void Switch(SimContext context, EntityId target) => inner.Switch(context, target);
        public void Step(SimContext context) => inner.Step(context);
        public void SaveState(SimStateWriter writer) => inner.SaveState(writer);
        public void LoadState(SimStateReader reader) => inner.LoadState(reader);
        public void WriteState(StateHash hash) => inner.WriteState(hash);

        // Movement is handled by Crowd. This handler must still opt into the stable snapshot schema.
        private sealed class EmptyInputHandler : IControlInputHandler, IStableStateSerializer
        {
            public string StateTypeId => "control.player-input";
            public int StateVersion => 1;
            public void Apply(SimContext context, EntityId target, in InputFrame input) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
    }
}
