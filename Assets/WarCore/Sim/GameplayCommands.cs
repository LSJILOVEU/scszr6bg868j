using System;
namespace WarCore.Sim
{
    public readonly struct GameplayStamp
    {
        public readonly StableId Id;
        public readonly ushort Generation;
        public GameplayStamp(StableId id, ushort generation = 0)
        {
            Id = id;
            Generation = generation;
        }
    }
    // The key is a logical release identity. No physical slot or enqueue index is admitted.
    public readonly struct GameplayApplyKey : IComparable<GameplayApplyKey>
    {
        public readonly StableId Source, Producer;
        public readonly ulong CastToken, ProducerKind, ProducerToken;
        public readonly uint ActionOrdinal, PulseOrdinal;
        public GameplayApplyKey(StableId source, ulong castToken, ulong producerKind, StableId producer,
            ulong producerToken, uint actionOrdinal, uint pulseOrdinal = 0)
        {
            if (producerKind > 2)
                throw new SimBoundaryException("M0C_PRODUCER_KIND");
            Source = source;
            CastToken = castToken;
            ProducerKind = producerKind;
            Producer = producer;
            ProducerToken = producerToken;
            ActionOrdinal = actionOrdinal;
            PulseOrdinal = pulseOrdinal;
        }
        public int CompareTo(GameplayApplyKey b)
        {
            int c = Source.CompareTo(b.Source);
            if (c != 0)
                return c;
            c = CastToken.CompareTo(b.CastToken);
            if (c != 0)
                return c;
            c = ProducerKind.CompareTo(b.ProducerKind);
            if (c != 0)
                return c;
            c = Producer.CompareTo(b.Producer);
            if (c != 0)
                return c;
            c = ProducerToken.CompareTo(b.ProducerToken);
            if (c != 0)
                return c;
            c = ActionOrdinal.CompareTo(b.ActionOrdinal);
            return c != 0 ? c : PulseOrdinal.CompareTo(b.PulseOrdinal);
        }
    }
    public readonly struct GameplayCost
    {
        public readonly ulong ResourceKey;
        public readonly long AmountRaw;
        public GameplayCost(ulong resourceKey, long amountRaw)
        {
            ResourceKey = resourceKey;
            AmountRaw = amountRaw;
        }
    }
    public enum GameplayFailure
    {
        None, TargetGone, TargetLifeMismatch, InsufficientResource, CommandCapacity, StatusCapacity, Immune
    }
    internal readonly struct GameplayCommand : IComparable<GameplayCommand>
    {
        internal readonly bool Damage;
        internal readonly int Kind;
        internal readonly int Maximum;
        internal readonly GameplaySourceSnapshot Snapshot;
        internal readonly GameplayStamp Target;
        internal readonly GameplayApplyKey Key;
        internal readonly ulong Definition;
        internal readonly long Amount;
        internal GameplayCommand(bool damage, GameplayStamp target, GameplayApplyKey key, ulong definition, long amount)
        {
            Damage = damage;
            Kind = damage ? 0 : 1;
            Maximum = 0;
            Snapshot = default;
            Target = target;
            Key = key;
            Definition = definition;
            Amount = amount;
        }
        internal GameplayCommand(int kind, GameplayStamp target, GameplayApplyKey key, ulong definition, long amount, int maximum, GameplaySourceSnapshot snapshot)
        {
            Kind = kind;
            Damage = false;
            Target = target;
            Key = key;
            Definition = definition;
            Amount = amount;
            Maximum = maximum;
            Snapshot = snapshot;
        }
        internal bool SameApplication(GameplayCommand b) => Target.Id.Equals(b.Target.Id) && Key.CompareTo(b.Key) == 0;
        public int CompareTo(GameplayCommand b)
        {
            // Tick is shared by the bounded tick queue; Damage=Q2.0, ChangeResource=Q2.1.
            int c = (Damage ? 0 : 1).CompareTo(b.Damage ? 0 : 1);
            if (c != 0)
                return c;
            c = Target.Id.CompareTo(b.Target.Id);
            return c != 0 ? c : Key.CompareTo(b.Key);
        }
    }
    public readonly struct GameplayAction
    {
        internal readonly GameplayCommand Command;
        private GameplayAction(GameplayCommand command)
        {
            Command = command;
        }
        internal static GameplayAction FromCommand(GameplayCommand command) => new GameplayAction(command);
        private GameplayAction(bool damage, GameplayStamp target, GameplayApplyKey key, ulong definition, long amount)
        {
            Command = new GameplayCommand(damage, target, key, definition, amount);
        }
        public static GameplayAction ApplyStatus(GameplayStamp target, GameplayApplyKey key, ulong statusDef, long strengthRaw = -1, GameplaySourceSnapshot sourceSnapshot = default)
            => new GameplayAction(new GameplayCommand(2, target, key, statusDef, strengthRaw, 0, sourceSnapshot));
        public static GameplayAction Dispel(GameplayStamp target, GameplayApplyKey key, ulong tags, int maximum = 1)
            => new GameplayAction(new GameplayCommand(3, target, key, tags, 0, maximum, default));
        public static GameplayAction GrantAbsorb(GameplayStamp target, GameplayApplyKey key, ulong statusDef, long amountRaw)
            => new GameplayAction(new GameplayCommand(4, target, key, statusDef, amountRaw, 0, default));
        public static GameplayAction Damage(GameplayStamp target, GameplayApplyKey key, ulong damageType, long amountRaw)
            => new GameplayAction(true, target, key, damageType, amountRaw);
        public static GameplayAction ChangeResource(GameplayStamp target, GameplayApplyKey key, ulong resourceKey, long deltaRaw)
            => new GameplayAction(false, target, key, resourceKey, deltaRaw);
    }
    public static class GameplayEvents
    {
        public static EventTypeId Life => new EventTypeId(1900210);
        public static EventTypeId AbilityFailure => new EventTypeId(1900211);
        public static EventTypeId StatusChanged => new EventTypeId(1900212);
        internal static EventTypeId DownedExpiry => new EventTypeId(1900213);
        public static EventTypeId Damage => new EventTypeId(1900201);
        public static EventTypeId Resource => new EventTypeId(1900202);
        public static EventTypeId FinalLife => new EventTypeId(1900203);
        internal static EventTypeId StatusExpiry => new EventTypeId(1900205);
        internal static EventTypeId StatusPulse => new EventTypeId(1900206);
        internal static EventTypeId AbilityPhase => new EventTypeId(1900207);
        internal static EventTypeId AbilityPulse => new EventTypeId(1900208);
        internal static EventTypeId AbilityCooldown => new EventTypeId(1900209);
        internal static EventTypeId Regen => new EventTypeId(1900204);
    }
}
