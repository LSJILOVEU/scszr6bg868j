using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Combat;
using WarCore.Control;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    public static class WorldBuilder
    {
        private const uint MoverLayer = 1u;

        // Slot gap 2 m: stopped units leave 1.5 m between their edges, so a latecomer can walk between them.
        internal static readonly SimFix CrowdSlotSpacing = SimFix.FromInt(2);

        internal static (long X, long Z) FormationOffsetMm(int rank, int count)
        {
            SimVec3 offset = InterimOrders.SlotOffset(rank, count, SimFix.FromInt(1));
            return (offset.X.Raw * 1000 / SimFix.Scale, offset.Z.Raw * 1000 / SimFix.Scale);
        }

        public static SimSession Create(WorldRecipe recipe) => Create(recipe, null);

        // Units stand on the map's flat floor; walls become static boxes on the layers the file names.
        public static SimSession Create(WorldRecipe recipe, byte[] mapBytes) => Build(recipe, mapBytes, false);

        internal static SimSession Build(WorldRecipe recipe, byte[] mapBytes, bool reverseAllocation)
        {
            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));
            if (recipe.Mover != MoverKind.Interim && recipe.Mover != MoverKind.InterimPath && recipe.Mover != MoverKind.InterimCrowd)
                throw new ArgumentOutOfRangeException(nameof(recipe), "Unsupported mover.");
            bool crowd = recipe.Mover == MoverKind.InterimCrowd;
            bool path = recipe.Mover == MoverKind.InterimPath || crowd;
            if (path && mapBytes == null)
                throw new ArgumentException("Path and crowd movers require map bytes.", nameof(mapBytes));
            if (recipe.Match != null && (recipe.Combat == null || !crowd))
                throw new ArgumentException("Battle requires combat and crowd movement.", nameof(recipe));
            if (recipe.PlayerKey.HasValue && (!crowd || recipe.Combat == null || recipe.Match == null))
                throw new ArgumentException("Player control requires InterimCrowd, Combat and Match.", nameof(recipe));
            WcMap map = mapBytes == null ? null : WcMapReader.Read(mapBytes);
            TerrainTables terrain = null;
            if (map != null && map.HasTerrain)
            {
                // Order matters (the tests pin it): mover kind, then the player, then a missing pack, then the derivation.
                if (!path)
                    throw new NotSupportedException("Terrain maps (height field or surface table) need a path or crowd mover.");
                if (recipe.PlayerKey.HasValue)
                    throw new NotSupportedException("Player control on terrain maps is not supported before T1c.");
                if (recipe.TerrainPack == null)
                    throw new ArgumentException("A terrain map needs a content pack with terrain rules (WorldRecipe.WithTerrainRules).", nameof(recipe));
                terrain = TerrainDerivation.Derive(map, recipe.TerrainPack);
            }
            long groundRaw = map == null ? 0 : map.FlatHeight.Raw;
            uint wallMask = 0;
            if (map != null)
                foreach (WcMapWall wall in map.Walls)
                    wallMask |= map.LayerMask(wall.LayerName);
            if ((wallMask & MoverLayer) != 0)
                throw new InvalidOperationException("Wall layers must not share the mover layer.");
            // The terrain digest names the exact (map, rules) pair, so a snapshot cannot be restored under other heights or rules.
            var content = terrain == null ? new StableContentManifest(1)
                : new StableContentManifest(1, new StableContentKey("terrain.tables", terrain.Digest));
            var placed = new List<PlacedCatalogEntry>();
            if (map != null)
                foreach (WcMapWall wall in map.Walls)
                    placed.Add(new PlacedCatalogEntry(wall.Chunk, wall.Placement));
            int firstUnit = placed.Count;
            foreach (UnitSpawn spawn in recipe.Units)
                placed.Add(new PlacedCatalogEntry(1, spawn.Key));
            PlacedCatalogEntry[] placements = placed.ToArray();
            var catalog = new PlacedCatalog(content, placements);
            // The map path sizes the entity table to walls plus units; the plain path keeps the M1 default.
            SimConfig config = map == null ? null : new SimConfig(entityCapacity: placements.Length);
            if (recipe.Combat != null) config = recipe.Combat.BuildConfig(placements.Length, recipe.Units.Length);
            // The mode is created first, one fresh instance per world, and bound to its read-only facts below.
            MatchMode mode = recipe.Match == null ? null : new MatchMode(recipe.Match, recipe.RuleSetId);
            var sim = recipe.Match == null
                ? Simulation.CreateRetainedWorld(recipe.Seed, catalog, config, content: content,
                    gameplaySchema: recipe.Combat?.BuildSchema(recipe.Units.Length))
                : Simulation.CreateRetainedWorld(recipe.Seed, catalog, config, content: content, mode: mode, modeId: mode.RuleSetId,
                    gameplaySchema: recipe.Combat?.BuildSchema(recipe.Units.Length));
            ComponentType<Position> positions = sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            ComponentType<Collider> colliders = sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            ComponentType<MoveIntent> intents = sim.Entities.Register<MoveIntent>(MoveIntent.ComponentId, new MoveIntentSerializer());
            ComponentType<MoveWaypoint> waypoints = default;
            if (path)
                waypoints = sim.Entities.Register<MoveWaypoint>(MoveWaypoint.ComponentId, new MoveWaypointSerializer());
            if (terrain != null)
                sim.Entities.Register<MoveProgress>(MoveProgress.ComponentId, new MoveProgressSerializer());
            ComponentType<UnitFacing> facings = default;
            if (recipe.Facing)
                facings = sim.Entities.Register<UnitFacing>(UnitFacing.ComponentId, new UnitFacingSerializer());
            ComponentType<MotionLimits> motionLimits = default;
            if (recipe.Motion)
                motionLimits = sim.Entities.Register<MotionLimits>(MotionLimits.ComponentId, new MotionLimitsSerializer());
            ComponentType<Combatant> combatants = default;
            if (recipe.Combat != null)
                combatants = sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(2));
            if (map != null)
            {
                var origin = new SimVec3(map.Min.X, map.FlatHeight, map.Min.Z);
                for (int n = 0; n < map.Walls.Count; n++)
                {
                    WcMapWall wall = map.Walls[reverseAllocation ? map.Walls.Count - 1 - n : n];
                    EntityId entity = sim.Entities.Create(wall.Key);
                    sim.Entities.Set(positions, entity, new Position(origin + wall.LocalCenter));
                    sim.Entities.Set(colliders, entity, Collider.Box(wall.HalfX, wall.Height, wall.HalfZ,
                        BoxYaw.Identity, layer: map.LayerMask(wall.LayerName), isStatic: true));
                }
            }
            var units = new EntityId[recipe.Units.Length];
            for (int n = 0; n < units.Length; n++)
            {
                int i = reverseAllocation ? units.Length - 1 - n : n;
                UnitSpawn spawn = recipe.Units[i];
                SimVec3 point = SimSession.Target(spawn.XMm, spawn.ZMm, groundRaw);
                if (terrain?.Ground != null && terrain.Ground.TryHeightAt(point.X, point.Z, out SimFix groundY))
                    point = new SimVec3(point.X, groundY, point.Z);
                SimFix speed = SimFix.Ratio(spawn.SpeedMmPerSec, 1000 * SimClock.TickRate);
                EntityId unit = sim.Entities.Create(placements[firstUnit + i].Key);
                units[i] = unit;
                if (recipe.Facing)
                    sim.Entities.Set(facings, unit, UnitFacing.Default);
                if (recipe.Motion)
                    sim.Entities.Set(motionLimits, unit, new MotionLimits(speed, default, default, default));
                if (recipe.Match == null || !spawn.Reserve)
                {
                    // A terrain world never places a unit where its footprint touches blocked ground or leaves the map.
                    if (terrain != null && !terrain.Nav.SegmentClear(point, point, InterimLocomotion.UnitFootprint))
                        throw new ArgumentException("Unit spawn " + spawn.Key + " is not on standable ground in a terrain world.", nameof(recipe));
                    sim.Entities.Set(positions, unit, new Position(point));
                    sim.Entities.Set(colliders, unit, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(1)));
                    sim.Entities.Set(intents, unit, new MoveIntent(point, speed, spawn.Squad, false));
                    if (path) sim.Entities.Set(waypoints, unit, default);
                }
                if (recipe.Combat != null)
                {
                    sim.BindGameplay(unit, 1);
                    int ammo = spawn.Weapon < 0 ? 0 : recipe.Combat.Weapons[spawn.Weapon].Magazine;
                    sim.Entities.Set(combatants, unit, new Combatant(spawn.Team, spawn.Weapon, ammo, 0));
                }
            }
            // All entities are present before control and combat allocate bindings/buffers. No runtime births.
            PlayerControl controlled = null;
            if (recipe.PlayerKey.HasValue)
            {
                int playerIndex = -1;
                for (int i = 0; i < recipe.Units.Length; i++)
                    if (recipe.Units[i].Key == recipe.PlayerKey.Value) { playerIndex = i; break; }
                if (playerIndex < 0)
                    throw new ArgumentException("Player key does not match a UnitSpawn.Key.", nameof(recipe));
                EntityId player = units[playerIndex];
                if (!sim.Entities.TryGet(positions, player, out _) || !sim.Entities.TryGet(intents, player, out _)
                    || !sim.Entities.TryGet(colliders, player, out _))
                    throw new ArgumentException("Player key must name a deployed unit with Position, MoveIntent and Collider.", nameof(recipe));
                controlled = new PlayerControl(sim.Entities, player);
            }
            CombatSystem combat = null;
            if (recipe.Combat != null)
            {
                combat = new CombatSystem(sim, positions, combatants, recipe.Combat, wallMask, MoverLayer, recipe.Units.Length, controlled);
                sim.RegisterGameplay("combat.system", combat, SimSystemPhase.Queries);
                // The combat system registers the melee component, so the units get their specs only now.
                for (int i = 0; i < recipe.Units.Length; i++)
                    if (recipe.Units[i].Melee >= 0) combat.SetMelee(units[i], recipe.Units[i].Melee);
                sim.RegisterGameplay("bridge.dead-cleanup", new DeadCleanup(sim, combatants, intents), SimSystemPhase.Requests);
            }
            if (recipe.CrowdPass != CrowdPassKind.None && (!crowd || recipe.Combat == null))
                throw new ArgumentException("A crowd pass rule requires crowd movement and combat.", nameof(recipe));
            ICrowdPass crowdPass = recipe.CrowdPass == CrowdPassKind.TeamIdle ? new TeamIdleCrowdPass(sim, combat, recipe.Combat, controlled) : null;
            var locomotion = new InterimLocomotion(sim.Entities, wallMask,
                path ? (terrain != null ? terrain.Nav : WcMapNavigation.Create(map)) : null, crowd,
                controlled == null ? null : new PlayerStep(sim, controlled), crowdPass, terrain);
            if (controlled != null) locomotion.Stop(controlled.Current);
            if (recipe.Facing) locomotion.BindFacing(facings);
            sim.RegisterGameplay("interim.orders", new InterimOrders(sim.Entities, locomotion, crowd ? CrowdSlotSpacing : default, controlled), SimSystemPhase.Requests);
            sim.RegisterGameplay("interim.locomotion", locomotion, SimSystemPhase.Movement);
            CaptureSystem capture = null; ReinforceSystem reinforce = null; MatchSystem match = null;
            if (recipe.Match != null)
            {
                capture = new CaptureSystem(combat, recipe.Match);
                var adapter = new BattleAdapter(sim, locomotion, recipe, new DeployGround(terrain != null ? terrain.Nav : WcMapNavigation.Create(map), terrain, groundRaw), groundRaw);
                reinforce = new ReinforceSystem(combat, recipe.Match, capture, adapter);
                match = new MatchSystem(combat, recipe.Match, capture, mode);
                mode.Bind(match);
                sim.RegisterGameplay("battle.reinforce", reinforce, SimSystemPhase.Requests);
                sim.RegisterGameplay("battle.ai", new SimpleAiSystem(combat, recipe.Combat, recipe.Match, capture, adapter, controlled), SimSystemPhase.Requests);
                sim.RegisterGameplay("battle.capture", capture, SimSystemPhase.Queries);
                sim.RegisterGameplay("battle.match", match, SimSystemPhase.Queries);
            }
            if (controlled != null)
            {
                sim.RegisterGameplay("control.player", controlled, SimSystemPhase.Requests);
                sim.RegisterGameplay("control.player-fire", new PlayerFire(sim, combat, controlled, recipe.Facing ? facings : (ComponentType<UnitFacing>?)null), SimSystemPhase.Queries);
                sim.RegisterGameplay("control.fallback", new PlayerFallback(combat, controlled, locomotion,
                    () => mode.IsFinished), SimSystemPhase.Requests);
            }
            sim.CompleteSpatialBoundary();
            var session = new SimSession(sim, units, recipe.Units.ToArray(), positions, groundRaw, map, wallMask,
                mapBytes == null ? null : ViewMap.Parse(mapBytes), combat, recipe.Match, capture, reinforce, match, recipe.Combat?.MaxHealthRaw ?? 0, controlled, recipe.Combat, recipe.Facing ? facings : (ComponentType<UnitFacing>?)null);
            foreach (BridgeCommand command in recipe.Script)
                session.Enqueue(command);
            return session;
        }
    }

    // Keep simulation types inside the two bridge files. Bind stable identities, never cached entity slots.
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
        private static readonly SimFix Radius = SimFix.Ratio(1, 4);
        public ulong PolicyKey { get; }
        internal BattleAdapter(Simulation sim, InterimLocomotion locomotion, WorldRecipe recipe, DeployGround ground, long groundRaw)
        {
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

    internal sealed class DeadCleanup : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly EntityStage stage;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<MoveIntent> intents;
        public DeadCleanup(Simulation sim, ComponentType<Combatant> combatants, ComponentType<MoveIntent> intents)
        { this.sim = sim; this.combatants = combatants; this.intents = intents; stage = new EntityStage(sim.Entities); }
        public int Order => 5;
        public string StateTypeId => "bridge.dead-cleanup";
        public int StateVersion => 1;
        public void Step(SimContext context)
        {
            foreach (EntityId unit in stage.Capture())
                if (sim.Entities.TryGet(combatants, unit, out _) && sim.Entities.TryGet(intents, unit, out _)
                    && sim.GameplayLife(sim.Entities.StableOf(unit)).State == GameplayLifeState.Dead)
                    sim.Entities.Remove(intents, unit);
        }
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }

    /// <summary>Default crowd pass rule: a moving unit may pass a stationary unit of the same team (team 1 or higher).
    /// Never involves the controlled unit, and never involves a unit that is engaged: one that fired or swung within
    /// the last EngagedTicks (or whose next shot or swing is still pending) keeps blocking and is blocked,
    /// so a friend cannot creep onto a muzzle between shots.</summary>
    internal sealed class TeamIdleCrowdPass : ICrowdPass
    {
        // Algorithm identity ("TEAMIDL1"); no static configuration.
        internal const ulong RuleDigest = 0x5445414D49444C31UL;
        // PLACEHOLDER: how long after its last shot or swing a unit still counts as engaged (about two seconds).
        internal const long EngagedTicks = 60;
        private readonly Simulation sim;
        private readonly EntityStore entities;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<MeleeState> melee;
        private readonly bool hasMelee;
        private readonly IControlTarget controlled;

        internal TeamIdleCrowdPass(Simulation sim, CombatSystem combat, CombatPack pack, IControlTarget controlled)
        {
            this.sim = sim; entities = combat.Entities; this.controlled = controlled;
            combatants = entities.Resolve<Combatant>();
            hasMelee = pack.HasMelee;
            if (hasMelee) melee = entities.Resolve<MeleeState>();
        }

        public ulong Digest => RuleDigest;

        public bool MayPass(EntityId mover, EntityId idle)
        {
            if (controlled != null && (mover.Equals(controlled.Current) || idle.Equals(controlled.Current))) return false;
            if (!entities.TryGet(combatants, mover, out Combatant a) || !entities.TryGet(combatants, idle, out Combatant b)) return false;
            if (a.Team < 1 || a.Team != b.Team) return false;
            long tick = sim.Clock.Tick;
            return !Busy(mover, a, tick) && !Busy(idle, b, tick);
        }

        private bool Busy(EntityId unit, Combatant state, long tick)
        {
            // DueMilli is zero until the first shot, so a unit that never fired is never engaged.
            if (state.DueMilli > 0 && state.DueMilli > (tick - EngagedTicks) * 1000) return true;
            return hasMelee && entities.TryGet(melee, unit, out MeleeState swing)
                && (swing.Pending || swing.NextStartTick > 0 && tick < swing.NextStartTick + EngagedTicks);
        }
    }
}
