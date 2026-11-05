using System;

namespace WarCore.Sim
{
    public enum SimTierLevel
    {
        Full,
        Reduced,
        Dormant
    }

    public readonly struct SimTier : IStateHash
    {
        public SimTierLevel Level { get; }
        public static SimTier Full => new SimTier(SimTierLevel.Full);
        public static SimTier Reduced => new SimTier(SimTierLevel.Reduced);
        public static SimTier Dormant => new SimTier(SimTierLevel.Dormant);
        public SimTier(SimTierLevel level)
        {
            if (level < SimTierLevel.Full || level > SimTierLevel.Dormant)
                throw new ArgumentOutOfRangeException(nameof(level));
            Level = level;
        }

        public void WriteState(StateHash hash) => hash.Add((int)Level);
    }

    public sealed class SimTierSerializer : IStableComponentSerializer<SimTier>
    {
        public int TypeId => 4;
        public int Version => 1;
        public void Write(SimStateWriter writer, in SimTier value) => writer.Int32((int)value.Level);
        public SimTier Read(SimStateReader reader) => new SimTier((SimTierLevel)reader.Int32());
    }

    public interface ISimEntitySystem : IStateHash
    {
        int Order { get; }
        void StepEntity(SimContext context, EntityId entity);
    }

    internal sealed class TieredSystem : ISimSystem, IStableStateSerializer, ISimPhaseSystem, ISimWarmupSystem, ISimRestoreValidator
    {
        private readonly ISimEntitySystem system;
        private readonly EntityStage stage;
        private readonly ComponentType<SimTier> tiers;
        private readonly int period;
        private readonly ISimElapsedEntitySystem elapsedSystem;
        private readonly TieredTimeline timeline;
        public int Order => system.Order;
        public SimSystemPhase Phase => system is ISimPhaseSystem staged ? staged.Phase : SimSystemPhase.Requests;
        private ISimStateSerializer Serializer => system as ISimStateSerializer
            ?? throw new SimSnapshotException("Missing ISimStateSerializer for tiered entity system.");
        // Include immutable execution configuration in compatibility checks before restoration mutates state.
        public string StateTypeId { get; }
        public int StateVersion => elapsedSystem == null ? Serializer.StateVersion : 2;
        public TieredSystem(ISimEntitySystem system, ComponentType<SimTier> tiers, int period)
        {
            if (period <= 0)
                throw new ArgumentOutOfRangeException(nameof(period));
            this.system = system ?? throw new ArgumentNullException(nameof(system));
            stage = new EntityStage(tiers.Owner);
            this.tiers = tiers;
            this.period = period;
            elapsedSystem = system as ISimElapsedEntitySystem;
            if (elapsedSystem != null)
                timeline = new TieredTimeline(tiers.Owner);
            StateTypeId = "warcore.tiered/" + period + "/" + tiers.Storage.Name + "/" + Serializer.StateTypeId
                + (elapsedSystem == null ? "" : "/elapsed-v2/" + Serializer.StateVersion);
        }

        public void Step(SimContext context)
        {
            ulong phase = (ulong)context.Clock.Tick % (ulong)period;
            foreach (EntityId entity in stage.Capture())
            {
                SimTierLevel level = context.Entities.TryGet(tiers, entity, out SimTier tier) ? tier.Level : SimTierLevel.Full;
                if (elapsedSystem != null)
                {
                    timeline.Advance(context, entity, level, period, elapsedSystem);
                    continue;
                }
                // Absolute ticks need no phase counter; changing tier or restoring cannot restart a cycle.
                if (level == SimTierLevel.Full || (level == SimTierLevel.Reduced && phase == context.Entities.StableOf(entity).Value % (ulong)period))
                    system.StepEntity(context, entity);
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add("warcore.tiered-v1");
            hash.Add(tiers.Storage.Name);
            hash.Add(period);
            system.WriteState(hash);
            timeline?.WriteState(hash);
        }

        public void PrepareWarmup() => timeline?.PrepareWarmup();
        public void SaveState(SimStateWriter writer)
        {
            if (writer.IsStable)
                writer.Section(Serializer);
            else
                Serializer.SaveState(writer);
            timeline?.Save(writer);
        }
        public void LoadState(SimStateReader reader)
        {
            if (reader.IsStable)
            {
                var section = reader.Section();
                Serializer.LoadState(section);
                section.End();
            }
            else
                Serializer.LoadState(reader);
            timeline?.Load(reader);
        }
        public void ValidateRestoredState(SimContext context)
        {
            timeline?.Validate(context.Clock.Tick);
            if (system is ISimRestoreValidator validator)
                validator.ValidateRestoredState(context);
        }
    }

    public sealed partial class Sim
    {
        public void RegisterTiered(string stableId, ISimEntitySystem system, ComponentType<SimTier> tiers, int reducedPeriod)
        {
            if (tiers.Owner != Entities)
                throw new ArgumentException("Tier handle belongs to another entity store.", nameof(tiers));
            Register(stableId, new TieredSystem(system, tiers, reducedPeriod));
        }
    }
}
