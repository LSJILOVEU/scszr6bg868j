using System;

namespace WarCore.Sim
{
    public readonly struct StructureHealth : IStateHash
    {
        public SimFix Health { get; }
        public SimFix CollapseThreshold { get; }
        public bool Collapsed { get; }
        public StructureHealth(SimFix health, SimFix collapseThreshold, bool collapsed = false)
        {
            Health = health;
            CollapseThreshold = collapseThreshold;
            Collapsed = collapsed;
        }

        public StructureHealth WithHealth(SimFix health) => new StructureHealth(health, CollapseThreshold, Collapsed);
        public void WriteState(StateHash hash)
        {
            Health.WriteState(hash);
            CollapseThreshold.WriteState(hash);
            hash.Add(Collapsed);
        }
    }

    public sealed class StructureSystem : ISimSystem, IStableStateSerializer, ISimWarmupSystem
    {
        private readonly EntityStore entities;
        private readonly EntityStage stage;
        private readonly ComponentType<StructureHealth> healths;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Collider> colliders;
        private readonly int reason;
        private bool assemblyChecked;
        public string AssemblyDiagnostic { get; private set; }
        public int Order { get; }
        public string StateTypeId { get; }
        public int StateVersion => 1;
        public StructureSystem(EntityStore entities, ComponentType<StructureHealth> healths,
            ComponentType<Position> positions, ComponentType<Collider> colliders, int reason, int order)
        {
            if (entities == null || healths.Owner != entities || positions.Owner != entities || colliders.Owner != entities)
                throw new ArgumentException("Structure handles must belong to the supplied store.");
            this.entities = entities;
            stage = new EntityStage(entities);
            this.healths = healths;
            this.positions = positions;
            this.colliders = colliders;
            this.reason = reason;
            Order = order;
            StateTypeId = "warcore.structure/" + healths.Storage.Name + "/" + positions.Storage.Name + "/" + colliders.Storage.Name;
        }

        public void PrepareWarmup()
        {
            if (assemblyChecked)
                return;
            assemblyChecked = true;
            int missing = 0;
            int first = -1;
            foreach (EntityId entity in entities)
            {
                if (!entities.TryGet(healths, entity, out _))
                    continue;
                if (entities.TryGet(positions, entity, out _) && entities.TryGet(colliders, entity, out _))
                    continue;
                if (first < 0)
                    first = entity.Id;
                missing++;
            }
            // Assembly diagnostics are cold and observable without allocating or reporting every tick.
            if (missing != 0)
                AssemblyDiagnostic = "Incomplete structure Position/Collider: entity=" + first + "; count=" + missing + ".";
        }

        public void Step(SimContext context)
        {
            if (context.Entities != entities)
                throw new InvalidOperationException("Structure system belongs to another simulation.");
            foreach (EntityId entity in stage.Capture())
            {
                if (!entities.TryGet(healths, entity, out StructureHealth health) || health.Collapsed || health.Health >= health.CollapseThreshold)
                    continue;
                if (!entities.TryGet(positions, entity, out Position position) || !entities.TryGet(colliders, entity, out Collider collider))
                    continue;
                SimVec3 center = position.Value;
                entities.Set(healths, entity, new StructureHealth(health.Health, health.CollapseThreshold, true));
                // The latch is authoritative even if the output event is dropped by a full buffer.
                context.Emit(SimEvent.WorldChangeEvent(context.Clock.Tick, center,
                    collider.WorldHalfExtents, reason));
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId);
            hash.Add(reason);
        }

        public void SaveState(SimStateWriter writer) => writer.Int32(reason);
        public void LoadState(SimStateReader reader) => SimStateReader.Require(reader.Int32() == reason, "Structure reason configuration mismatch.");
    }
}
