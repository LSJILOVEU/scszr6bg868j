using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private GameplayCastRow[] abilityCasts;
        private GameplayCooldownRow[] abilityCooldowns;
        private GameplayStamp[] abilityTargets;
        private long[] abilityDistances;
        private GameplayInputCommand[] abilityInput;
        private GameplayInputResult[] abilityResults;
        private long[] abilityFailures;
        private SpatialHit[] abilityHits;
        private GameplayAction[] abilityReleaseActions;
        private int abilityCastCount, cooldownCount, abilityInputCount, resultCount;
        private int heldAbilityCommands, heldAbilityStatuses, heldAbilityTimers;
        private void InitAbilities()
        {
            abilityCasts = new GameplayCastRow[schema.Capacities.Casts];
            abilityCooldowns = new GameplayCooldownRow[schema.Capacities.Cooldowns];
            ulong targetProduct = (ulong)schema.Capacities.Casts * (ulong)schema.Capacities.TargetsPerCast;
            if (targetProduct > (ulong)GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_ABILITY_TARGET_CAPACITY");
            int targets = (int)targetProduct;
            abilityTargets = new GameplayStamp[targets];
            abilityDistances = new long[targets];
            abilityInput = new GameplayInputCommand[schema.Capacities.InputCommands];
            abilityResults = new GameplayInputResult[schema.Capacities.InputCommands];
            selectedAbilityTargets = new GameplayStamp[schema.Capacities.TargetsPerCast];
            selectedAbilityDistances = new long[schema.Capacities.TargetsPerCast];
            abilityFailures = new long[25];
            abilityHits = new SpatialHit[sim.Entities.Capacity];
            abilityReleaseActions = new GameplayAction[schema.Capacities.Commands];
        }
        internal int AbilityCastCount => abilityCastCount;
        internal int AbilityInputResultCount => resultCount;
        internal GameplayCastView AbilityCastAt(int index) => new GameplayCastView(abilityCasts[index]);
        internal GameplayInputResult AbilityInputResultAt(int index) => abilityResults[index];
        internal long AbilityFailureCount(GameplayAbilityFailure failure) => abilityFailures[(int)failure];
        internal GameplayCastPhase AbilityPhase(StableId owner)
        {
            for (int i = 0; i < abilityCastCount; i++)
                if (abilityCasts[i].Owner.Id.Equals(owner))
                    return abilityCasts[i].Phase;
            return GameplayCastPhase.Idle;
        }
        internal long AbilityCooldownReady(StableId owner, ulong key)
        {
            for (int i = 0; i < cooldownCount; i++)
                if (abilityCooldowns[i].Owner.Id.Equals(owner) && abilityCooldowns[i].Key == key)
                    return abilityCooldowns[i].ReadyTick;
            return 0;
        }
    }
    public sealed partial class Sim
    {
        public int GameplayCastCount => gameplay == null ? 0 : gameplay.AbilityCastCount;
        public int GameplayInputResultCount => gameplay == null ? 0 : gameplay.AbilityInputResultCount;
        public GameplayCastView GameplayCastAt(int index) => gameplay.AbilityCastAt(index);
        public GameplayInputResult GameplayInputResultAt(int index) => gameplay.AbilityInputResultAt(index);
        public GameplayCastPhase GameplayCastPhase(StableId owner) => gameplay.AbilityPhase(owner);
        public long GameplayCooldownReady(StableId owner, ulong key) => gameplay.AbilityCooldownReady(owner, key);
        public long GameplayAbilityFailureCount(GameplayAbilityFailure failure) => gameplay.AbilityFailureCount(failure);
    }
}
