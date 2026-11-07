using System;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The player's unit: the control target, made before combat (which reads it), and the three player systems, made once the
    // locomotion and the match mode exist. On when the recipe names a player key.
    internal sealed class PlayerFeature : WorldStageFeature
    {
        internal static readonly PlayerFeature Instance = new PlayerFeature();

        public override string OptionId => "world.player";

        public override bool IsActive(WorldAssembly a) => a.Recipe.PlayerKey.HasValue;

        // All entities are present before control and combat allocate bindings/buffers. No runtime births.
        public override void Connect(WorldAssembly a)
        {
            WorldRecipe recipe = a.Recipe;
            int playerIndex = -1;
            for (int i = 0; i < recipe.Units.Length; i++)
                if (recipe.Units[i].Key == recipe.PlayerKey.Value) { playerIndex = i; break; }
            if (playerIndex < 0)
                throw new ArgumentException("Player key does not match a UnitSpawn.Key.", nameof(recipe));
            EntityId player = a.Units[playerIndex];
            EntityStore entities = a.Sim.Entities;
            if (!entities.TryGet(a.Positions, player, out _) || !entities.TryGet(a.Intents, player, out _)
                || !entities.TryGet(a.Colliders, player, out _))
                throw new ArgumentException("Player key must name a deployed unit with Position, MoveIntent and Collider.", nameof(recipe));
            a.Controlled = new PlayerControl(entities, player);
        }

        public override void RegisterRules(WorldAssembly a)
        {
            PlayerControl controlled = a.Controlled;
            var sim = a.Sim;
            sim.RegisterGameplay("control.player", controlled, SimSystemPhase.Requests);
            sim.RegisterGameplay("control.player-fire", new PlayerFire(sim, a.Combat, controlled, a.Recipe.Facing ? a.Facings : (ComponentType<UnitFacing>?)null), SimSystemPhase.Queries);
            // With match respawn on, the respawn system (MatchFeature) replaces the immediate fallback.
            if (a.Recipe.Match != null && a.Recipe.Match.RespawnTicks > 0) return;
            WarCore.Battle.MatchMode mode = a.Mode;
            sim.RegisterGameplay("control.fallback", new PlayerFallback(a.Combat, controlled, a.Locomotion,
                () => mode.IsFinished), SimSystemPhase.Requests);
        }
    }
}
