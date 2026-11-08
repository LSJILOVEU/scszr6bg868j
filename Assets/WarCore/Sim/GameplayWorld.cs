using System;
namespace WarCore.Sim
{
    public sealed partial class Sim
    {
        private bool gameplayQueries;
        public int GameplayStatusCount => gameplay == null ? 0 : gameplay.StatusCount;
        public ulong GameplayStatusTags(StableId target)
        {
            if (gameplay == null)
                throw new SimBoundaryException("M0C_GAMEPLAY_REQUIRED");
            return gameplay.StatusTags(target);
        }
        public GameplayStatusView GameplayStatusAt(int index)
        {
            if (gameplay == null)
                throw new SimBoundaryException("M0C_GAMEPLAY_REQUIRED");
            return gameplay.StatusAt(index);
        }
        public void BindGameplay(EntityId entity, ulong archetypeKey)
        {
            RequireGameplayProtocol();
            if (gameplay == null || started || stepping)
                throw new SimBoundaryException("M0C_BIND_INITIALIZATION_ONLY");
            gameplay.Bind(entity, archetypeKey);
        }
        public long GameplayResource(StableId owner, ulong resourceKey)
        {
            if (gameplay == null)
                throw new SimBoundaryException("M0C_GAMEPLAY_REQUIRED");
            return gameplay.Read(owner, resourceKey);
        }
        public GameplayStamp GameplayTarget(StableId owner)
        {
            if (gameplay == null)
                throw new SimBoundaryException("M0C_GAMEPLAY_REQUIRED");
            return new GameplayStamp(owner, gameplay.Generation(owner));
        }
        internal GameplayState GameplayWriter()
        {
            if (gameplay == null || !gameplayQueries)
                throw new SimBoundaryException("M0C_GAMEPLAY_QUERY_PHASE");
            return gameplay;
        }
        internal bool TryGameplaySpatial(StableId id, out SimVec3 position, out uint layer) => TryGameplaySpatial(id, out position, out layer, out _);
        // Explicit and occlusion-checked targets are points at the collider centre; block 3a admits only capsules there.
        internal bool TryGameplaySpatial(StableId id, out SimVec3 position, out uint layer, out ShapeKind shape)
        {
            position = default;
            layer = 0;
            shape = ShapeKind.VerticalCapsule;
            if (spatialIndex == null || !Entities.TryResolve(id, out EntityRef entity)
                || !Entities.TryGet(spatialPositions, (EntityId)entity, out Position p)
                || !Entities.TryGet(spatialColliders, (EntityId)entity, out Collider collider))
                return false;
            position = p.Value;
            layer = collider.Layer;
            shape = collider.ShapeKind;
            return true;
        }
    }
    public sealed partial class SimContext
    {
        public GameplayFailure ApplyStatus(GameplayStamp target, GameplayApplyKey key, ulong statusDef, long strengthRaw = -1, GameplaySourceSnapshot sourceSnapshot = default, uint commandOrdinal = 0)
            => Owner.GameplayWriter().AdmitStatus(GameplayAction.ApplyStatus(target, key, statusDef, strengthRaw, sourceSnapshot).Command, commandOrdinal);
        public GameplayFailure GrantAbsorb(GameplayStamp target, GameplayApplyKey key, ulong statusDef, long amountRaw, uint commandOrdinal = 0)
            => Owner.GameplayWriter().AdmitStatus(GameplayAction.GrantAbsorb(target, key, statusDef, amountRaw).Command, commandOrdinal);
        public GameplayFailure Dispel(GameplayStamp target, GameplayApplyKey key, ulong tags, int maximum = 1, uint commandOrdinal = 0)
            => Owner.GameplayWriter().AdmitStatus(GameplayAction.Dispel(target, key, tags, maximum).Command, commandOrdinal);
        public GameplayFailure Damage(GameplayStamp target, GameplayApplyKey key, ulong damageType, long amountRaw)
            => Owner.GameplayWriter().Enqueue(true, target, key, damageType, amountRaw);
        public GameplayFailure ChangeResource(GameplayStamp target, GameplayApplyKey key, ulong resourceKey, long deltaRaw)
            => Owner.GameplayWriter().Enqueue(false, target, key, resourceKey, deltaRaw);
        public GameplayFailure SubmitGameplay(GameplayStamp owner, ReadOnlySpan<GameplayCost> costs, ReadOnlySpan<GameplayAction> actions, uint commandOrdinal = 0)
            => Owner.GameplayWriter().Submit(owner, costs, actions, commandOrdinal);
        public GameplayFailure SpendResources(GameplayStamp owner, ReadOnlySpan<GameplayCost> costs, uint commandOrdinal = 0)
            => Owner.GameplayWriter().Spend(owner, costs, commandOrdinal);
    }
    public sealed partial class EntityStore
    {
        internal GameplayState Gameplay
        {
            get;
            set;
        }
    }
    public sealed partial class SimScheduler
    {
        internal void ValidateGameplayRegeneration(TaskKey[] keys, bool[] present, int length)
        {
            int matched = 0;
            for (int slot = 0; slot < tasks.Length; slot++)
            {
                if (taskStates[slot] == 0 || !stableTasks[slot].Event.Type.Equals(GameplayEvents.Regen))
                    continue;
                int found = 0;
                for (int i = 0; i < length; i++)
                    if (present[i] && keys[i].Equals(stableTasks[slot].Key))
                        found++;
                SimStateReader.Require(found == 1, "M0C_ORPHAN_REGEN_TASK");
                matched++;
            }
            int expected = 0;
            for (int i = 0; i < length; i++)
                if (present[i])
                    expected++;
            SimStateReader.Require(matched == expected, "M0C_REGEN_TASK_COUNT");
        }
    }
}
