using System;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // The match: the mode (one fresh instance per world, made before the simulation, which owns it), and the capture,
    // reinforcement, AI and end-of-match systems, made once the locomotion exists. On when the recipe carries match rules.
    internal sealed class MatchFeature : WorldStageFeature
    {
        internal static readonly MatchFeature Instance = new MatchFeature();

        public override string OptionId => "world.match";

        public override bool IsActive(WorldAssembly a) => a.Recipe.Match != null;

        public override void Prepare(WorldAssembly a) => a.Mode = new MatchMode(a.Recipe.Match, a.Recipe.RuleSetId);

        public override void RegisterRules(WorldAssembly a)
        {
            Simulation sim = a.Sim;
            WorldRecipe recipe = a.Recipe;
            CombatSystem combat = a.Combat;
            TerrainTables terrain = a.Terrain;
            AiRules ai = terrain != null ? recipe.TerrainPack.Section<AiRules>() : recipe.Ai;
            var capture = new CaptureSystem(combat, recipe.Match);
            var adapter = new BattleAdapter(sim, a.Locomotion, recipe, new DeployGround(terrain != null ? terrain.Nav : WcMapNavigation.Create(a.Map), terrain, a.GroundRaw), a.GroundRaw, a.Standing);
            var reinforce = new ReinforceSystem(combat, recipe.Match, capture, adapter);
            var match = new MatchSystem(combat, recipe.Match, capture, a.Mode);
            a.Mode.Bind(match);
            sim.RegisterGameplay("battle.reinforce", reinforce, SimSystemPhase.Requests);
            // A positive match.respawnTicks with a player: the player takes over a reserve after the delay (PlayerFeature then adds no fallback).
            if (recipe.Match.RespawnTicks > 0 && a.Controlled != null)
            {
                var respawn = new RespawnSystem(sim, combat, recipe.Match, capture, adapter, a.Controlled);
                sim.RegisterGameplay("battle.respawn", respawn, SimSystemPhase.Requests);
                if (recipe.Match.RespawnGuardTicks > 0) combat.SetDamageGuard(respawn);
            }
            FailedSlotMemory memory = null;
            int failedTicks = ai?.FailedSlotTicks ?? 0;
            if (failedTicks > 0)
            {
                ComponentType<MoveIntent> intents = sim.Entities.Resolve<MoveIntent>();
                // The audience of a squad move command, as the orders system picks it (the controlled unit the AI skips anyway).
                memory = new FailedSlotMemory(combat.Entities, adapter, failedTicks, (unit, squad) =>
                    sim.Entities.TryGet(intents, sim.Entities.Resolve(new EntityLink(unit)), out MoveIntent intent) && intent.Squad == squad);
                sim.RegisterGameplay("battle.ai-memory", memory, SimSystemPhase.Requests);
            }
            TargetMemory contacts = null;
            int memoryTicks = ai?.TargetMemoryTicks ?? 0;
            if (memoryTicks > 0)
            {
                contacts = new TargetMemory(combat, memoryTicks);
                sim.RegisterGameplay("battle.ai-contacts", contacts, SimSystemPhase.Requests);
            }
            // Only with the memory on: a positive searchTicks adds the search; 0 (the default) adds nothing.
            TargetSearch search = null;
            int searchTicks = contacts != null ? ai.SearchTicks : 0;
            if (searchTicks > 0)
            {
                search = new TargetSearch(combat.Entities, contacts, searchTicks);
                sim.RegisterGameplay("battle.ai-search", search, SimSystemPhase.Requests);
            }
            // Only with the memory on: a positive suppressTicks adds the suppressive fire; 0 (the default) adds nothing.
            TargetSuppress suppress = null;
            int suppressTicks = contacts != null ? ai.SuppressTicks : 0;
            if (suppressTicks > 0)
            {
                suppress = new TargetSuppress(combat, contacts, adapter, suppressTicks);
                sim.RegisterGameplay("battle.ai-suppress", suppress, SimSystemPhase.Requests);
            }
            int meleeChaseRange = ai?.MeleeChaseRange ?? 0;
            sim.RegisterGameplay("battle.ai", new SimpleAiSystem(combat, recipe.Combat, recipe.Match, capture, adapter, a.Controlled, memory, contacts, search, suppress, meleeChaseRange), SimSystemPhase.Requests);
            sim.RegisterGameplay("battle.capture", capture, SimSystemPhase.Queries);
            sim.RegisterGameplay("battle.match", match, SimSystemPhase.Queries);
            a.Capture = capture; a.Reinforce = reinforce; a.Match = match;
        }
    }

    // Keep simulation types inside the bridge. Bind stable identities, never cached entity slots.
    internal sealed class BattleAdapter : IUnitDeployer, IMoveOrders
    {
        private readonly Simulation sim;
        private readonly InterimLocomotion locomotion;
        private readonly EntityStage stage;
        private readonly DeployGround ground;
        private readonly UnitSpawn[] spawns;
        private readonly StableId[] ids;
        private readonly long groundRaw;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Collider> colliders;
        private readonly ComponentType<MoveIntent> intents;
        private readonly ComponentType<MoveWaypoint> waypoints;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<SquadStanding>? standing;
        private static readonly SimFix Radius = SimFix.Ratio(1, 4);
        public ulong PolicyKey { get; }
        internal BattleAdapter(Simulation sim, InterimLocomotion locomotion, WorldRecipe recipe, DeployGround ground, long groundRaw, ComponentType<SquadStanding>? standing = null)
        {
            this.standing = standing;
            this.sim = sim; this.locomotion = locomotion; this.ground = ground; this.groundRaw = groundRaw;
            stage = new EntityStage(sim.Entities); spawns = recipe.Units.ToArray(); ids = new StableId[spawns.Length];
            positions = sim.Entities.Resolve<Position>(); colliders = sim.Entities.Resolve<Collider>();
            intents = sim.Entities.Resolve<MoveIntent>(); waypoints = sim.Entities.Resolve<MoveWaypoint>(); combatants = sim.Entities.Resolve<Combatant>();
            var hash = new StateHash(); hash.Add("bridge.deployer.v1"); hash.Add(ground.Digest); hash.Add(groundRaw);
            hash.Add(Radius.Raw); hash.Add(2); hash.Add(spawns.Length);
            for (int i = 0; i < spawns.Length; i++)
            {
                ids[i] = StableEntityKey.Placed(1, spawns[i].Key).Id;
                hash.Add(ids[i].Value); hash.Add(spawns[i].SpeedMmPerSec); hash.Add(spawns[i].Squad);
            }
            PolicyKey = hash.Value;
        }
        private EntityId Resolve(StableId unit) => sim.Entities.Resolve(new EntityLink(unit));
        public void Order(StableId unit, SimVec3 target) => locomotion.Order(Resolve(unit), target);
        public void Stop(StableId unit) => locomotion.Stop(Resolve(unit));
        public bool TryGetOrder(StableId unit, out SimVec3 target)
        {
            bool found = sim.Entities.TryGet(intents, Resolve(unit), out MoveIntent intent) && intent.Active;
            target = intent.Target; return found;
        }
        public bool CanStandAt(SimVec3 at) => ground.CanStand(at);
        public bool HasFailed(StableId unit) => locomotion.HasFailed(Resolve(unit));
        public bool IsCommanded(StableId unit) => standing.HasValue && sim.Entities.TryGet(standing.Value, Resolve(unit), out _);
        public bool TryDeploy(EntityId unit, SimVec3 at)
        {
            StableId stable = sim.Entities.StableOf(unit); unit = Resolve(stable);
            if (!ground.TryGround(at, out at) || !ground.CanStand(at)) return false;
            foreach (EntityId other in stage.Capture())
            {
                if (!sim.Entities.TryGet(combatants, other, out _) || !sim.Entities.TryGet(positions, other, out Position position)
                    || sim.GameplayLife(sim.Entities.StableOf(other)).State == GameplayLifeState.Dead) continue;
                Collider collider = sim.Entities.Read(colliders, other);
                SimFix gap = (Radius + collider.Radius) * SimFix.FromInt(2);
                if (CombatSystem.Horizontal(at - position.Value).LengthSquared < new SimVec3(gap, default, default).LengthSquared) return false;
            }
            int index = Array.IndexOf(ids, stable); UnitSpawn spawn = spawns[index];
            sim.Entities.Set(positions, unit, new Position(at));
            sim.Entities.Set(colliders, unit, new Collider(Radius, SimFix.FromInt(1)));
            sim.Entities.Set(intents, unit, new MoveIntent(at, SimFix.Ratio(spawn.SpeedMmPerSec, 1000 * SimClock.TickRate), spawn.Squad, false));
            sim.Entities.Set(waypoints, unit, default);
            return true;
        }
    }
}
