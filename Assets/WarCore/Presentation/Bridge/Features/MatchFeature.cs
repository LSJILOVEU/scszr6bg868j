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
            // A map with plates only may run on the terrain branch without a terrain pack; its AI rules then come from the AI pack.
            bool terrainRules = terrain != null && recipe.TerrainPack != null;
            AiRules ai = (terrainRules ? recipe.TerrainPack : recipe.AiPack)?.Section<AiRules>();
            // ML-9b: on a map with plates the match systems read layers (null without plates: the old rules).
            PlateLayers layers = PlateLayers.Of(a);
            var capture = new CaptureSystem(combat, recipe.Match, layers);
            var adapter = new BattleAdapter(sim, a.Locomotion, recipe, new DeployGround(terrain != null ? terrain.Nav : WcMapNavigation.Create(a.Map), terrain, a.GroundRaw, WorldAssembly.UnitRadius(recipe.Combat)), a.GroundRaw, a.Standing,
                a.Plates != null ? a.Verticals : (ComponentType<VerticalState>?)null, a.Plates);
            var reinforce = new ReinforceSystem(combat, recipe.Match, capture, adapter, layers: layers);
            var match = new MatchSystem(combat, recipe.Match, capture, a.Mode);
            a.Mode.Bind(match);
            sim.RegisterGameplay("battle.reinforce", reinforce, SimSystemPhase.Requests);
            // A positive match.respawnTicks with a player: the player takes over a reserve after the delay (PlayerFeature then adds no fallback).
            if (recipe.Match.RespawnTicks > 0 && a.Controlled != null)
            {
                var respawn = new RespawnSystem(sim, combat, recipe.Match, capture, adapter, a.Controlled, layers);
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
                contacts = new TargetMemory(combat, memoryTicks, layers);
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
            // Only with all ten pressure fields written: how hard each unit is pressed, read by the engage part. With cover
            // rules it also keeps where the fire came from.
            Pressure pressure = null;
            if (ai?.Pressure != null)
            {
                pressure = new Pressure(combat, ai.Pressure.Value, ai.CoverRadiusMm > 0);
                sim.RegisterGameplay("battle.ai-pressure", pressure, SimSystemPhase.Queries);
            }
            // Only with all seven squad fields written: who follows whom, read by the squad parts.
            Squads squads = null;
            if (ai?.Squads != null)
            {
                squads = new Squads(combat, adapter, a.Controlled, ai.Squads.Value);
                sim.RegisterGameplay("battle.ai-squads", squads, SimSystemPhase.Requests);
            }
            int meleeChaseRange = terrainRules ? recipe.TerrainPack.Section<AiRules>().MeleeChaseRange : 0;
            if (!terrainRules && ai != null) meleeChaseRange = ai.MeleeChaseRange;
            bool fair = recipe.TerrainPack != null && recipe.TerrainPack.Section<AiRules>().FairDecisions;
            // TRV-7a: the entry table is built from the same cost tables once the walls are in the spatial index (WorldBuilder).
            a.Reach = PointReach.Of(a, recipe.Match);
            sim.RegisterGameplay("battle.ai", new SimpleAiSystem(combat, recipe.Combat, recipe.Match, capture, adapter, a.Controlled, memory, contacts, search, suppress, meleeChaseRange, fair, recipe.Seed, ContentPolicies.Parts(ai, recipe.Match.Points, recipe.Match.ThinkInterval, terrain == null, layers != null, squads), pressure, layers, layers,
                a.Reach), SimSystemPhase.Requests);
            sim.RegisterGameplay("battle.capture", capture, SimSystemPhase.Queries);
            sim.RegisterGameplay("battle.match", match, SimSystemPhase.Queries);
            a.Capture = capture; a.Reinforce = reinforce; a.Match = match; a.Pressure = pressure; a.Squads = squads;
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
        private readonly ComponentType<VerticalState>? verticals;
        private readonly PlateWorld plates;
        private readonly SimFix radius;
        private readonly SimFix unitHeight;
        public ulong PolicyKey { get; }
        internal BattleAdapter(Simulation sim, InterimLocomotion locomotion, WorldRecipe recipe, DeployGround ground, long groundRaw, ComponentType<SquadStanding>? standing = null,
            ComponentType<VerticalState>? verticals = null, PlateWorld plates = null)
        {
            this.standing = standing;
            this.verticals = verticals;
            this.plates = plates;
            unitHeight = WorldAssembly.UnitHeight(recipe.Combat);
            radius = WorldAssembly.UnitRadius(recipe.Combat);
            this.sim = sim; this.locomotion = locomotion; this.ground = ground; this.groundRaw = groundRaw;
            stage = new EntityStage(sim.Entities); spawns = recipe.Units.ToArray(); ids = new StableId[spawns.Length];
            positions = sim.Entities.Resolve<Position>(); colliders = sim.Entities.Resolve<Collider>();
            intents = sim.Entities.Resolve<MoveIntent>(); waypoints = sim.Entities.Resolve<MoveWaypoint>(); combatants = sim.Entities.Resolve<Combatant>();
            var hash = new StateHash(); hash.Add("bridge.deployer.v1"); hash.Add(ground.Digest); hash.Add(groundRaw);
            hash.Add(radius.Raw); hash.Add(2); hash.Add(spawns.Length);
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
        // ML-11a: the same ports with a layer; layer 0 is the ground's port.
        public bool CanStandAt(SimVec3 at, int layer) => layer == 0 ? ground.CanStand(at) : plates != null && locomotion.CanStandOn(layer, at);
        public void Order(StableId unit, SimVec3 target, int layer) => locomotion.Order(Resolve(unit), target, layer);
        public bool TryGetOrder(StableId unit, out SimVec3 target, out int layer)
        {
            EntityId entity = Resolve(unit);
            bool found = sim.Entities.TryGet(intents, entity, out MoveIntent intent) && intent.Active;
            target = intent.Target;
            layer = found && verticals.HasValue && sim.Entities.TryGet(verticals.Value, entity, out VerticalState state) ? state.TargetLayer : 0;
            return found;
        }
        public bool HasFailed(StableId unit) => locomotion.HasFailed(Resolve(unit));
        public bool TryRouteCost(SimVec3 from, SimVec3 to, long limitCost, out long cost) => locomotion.TryRouteCost(from, to, limitCost, out cost);
        public bool IsCommanded(StableId unit) => standing.HasValue && sim.Entities.TryGet(standing.Value, Resolve(unit), out _);
        public bool TryDeploy(EntityId unit, SimVec3 at)
        {
            StableId stable = sim.Entities.StableOf(unit); unit = Resolve(stable);
            if (!ground.TryGround(at, out at) || !ground.CanStand(at)) return false;
            return Place(stable, unit, at, 0);
        }

        // On a plate (layer > 0): a walkable cell of that plate, Y on its top; the ground's footprint and stand rules are not
        // read (the footprint on a plate is the movers' business). Layer 0 is the ground deployment.
        public bool TryDeploy(EntityId unit, SimVec3 at, int layer)
        {
            if (layer == 0) return TryDeploy(unit, at);
            if (plates == null) throw new NotSupportedException("Deployment on layer " + layer + " needs a world with floor plates.");
            StableId stable = sim.Entities.StableOf(unit); unit = Resolve(stable);
            if (!plates.TryFloor(layer, at.X, at.Z, true, out SimFix y)) return false;
            return Place(stable, unit, new SimVec3(at.X, y, at.Z), layer);
        }

        private int LayerOf(EntityId unit) => sim.Entities.TryGet(verticals.Value, unit, out VerticalState state) ? state.Layer : 0;

        private bool Place(StableId stable, EntityId unit, SimVec3 at, int layer)
        {
            foreach (EntityId other in stage.Capture())
            {
                if (!sim.Entities.TryGet(combatants, other, out _) || !sim.Entities.TryGet(positions, other, out Position position)
                    || sim.GameplayLife(sim.Entities.StableOf(other)).State == GameplayLifeState.Dead) continue;
                // ML-9b: on a map with plates only units on the same layer keep their distance (one under a floor does not count).
                if (verticals.HasValue && LayerOf(other) != layer) continue;
                Collider collider = sim.Entities.Read(colliders, other);
                SimFix gap = (radius + collider.Radius) * SimFix.FromInt(2);
                if (CombatSystem.Horizontal(at - position.Value).LengthSquared < new SimVec3(gap, default, default).LengthSquared) return false;
            }
            int index = Array.IndexOf(ids, stable); UnitSpawn spawn = spawns[index];
            sim.Entities.Set(positions, unit, new Position(at));
            sim.Entities.Set(colliders, unit, new Collider(radius, unitHeight));
            sim.Entities.Set(intents, unit, new MoveIntent(at, SimFix.Ratio(spawn.SpeedMmPerSec, 1000 * SimClock.TickRate), spawn.Squad, false));
            sim.Entities.Set(waypoints, unit, default);
            // The deployment's layer: a unit that stood on a plate before and comes back on the ground is on layer 0 again.
            if (verticals.HasValue) sim.Entities.Set(verticals.Value, unit, new VerticalState(layer, 0));
            return true;
        }
    }
}
