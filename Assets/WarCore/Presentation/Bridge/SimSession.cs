using System;
using System.Collections.Generic;
using System.Diagnostics;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Combat;
using WarCore.Battle;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    public readonly struct SimStepTiming
    {
        public int Count { get; }
        public double MeanMs { get; }
        public double P95Ms { get; }
        public double MaxMs { get; }
        internal SimStepTiming(int count, double meanMs, double p95Ms, double maxMs)
        { Count = count; MeanMs = meanMs; P95Ms = p95Ms; MaxMs = maxMs; }
    }

    // What the builder hands a new session: one field per former constructor parameter, same names and defaults.
    internal sealed class SessionParts
    {
        internal Simulation Sim;
        internal EntityId[] Units;
        internal UnitSpawn[] Spawns;
        internal ComponentType<Position> Positions;
        internal long GroundRaw;
        internal WcMap Map;
        internal uint WallMask;
        internal ViewMap View;
        internal CombatSystem Combat;
        internal MatchPack MatchPack;
        internal CaptureSystem Capture;
        internal ReinforceSystem Reinforce;
        internal MatchSystem Match;
        internal long MaxHealthRaw;
        internal IControlTarget Controlled;
        internal CombatPack CombatPack;
        internal ComponentType<UnitFacing>? Facings;
        // Terrain tables and the rules they were derived under; both null on flat maps. Read by TerrainCheck only.
        internal TerrainTables Terrain;
        internal TerrainRules TerrainRules;
    }

    /// <summary>
    /// Drives Sim with squad commands and held player input. The held value and future
    /// input list are bridge state, excluded from Sim snapshots. There is no bridge restore API.
    /// After a Sim restore, the caller must reconstruct the held input from PlayerLog and
    /// requeue it for the next tick, then reload later inputs and commands. Unconsumed
    /// scheduled inputs are not in PlayerLog; the caller must retain them separately.
    /// </summary>
    public sealed class SimSession
    {
        public const int MaxTicksPerFrame = 5;
        public const int TickRate = SimClock.TickRate;
        private const long MicrosPerSecond = 1000000;
        private readonly Simulation sim;
        private readonly CombatSystem combat;
        private readonly CombatPack combatPack;
        private readonly IControlTarget controlled;
        private readonly StableId[] units;
        private readonly MatchPack matchPack;
        private readonly CaptureSystem capture;
        private readonly ReinforceSystem reinforce;
        private readonly MatchSystem match;
        private readonly UnitSpawn[] spawns;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<UnitFacing>? facings;
        private readonly List<BridgeCommand> pending = new List<BridgeCommand>();
        private readonly List<BridgeCommand> commandLog = new List<BridgeCommand>();
        private readonly List<PlayerInputRecord> pendingPlayer = new List<PlayerInputRecord>();
        private readonly List<PlayerInputRecord> playerLog = new List<PlayerInputRecord>();
        private PlayerInput heldPlayerInput;
        private readonly long groundRaw;
        private readonly long maxHealthRaw;
        private readonly WcMap map;
        private readonly uint wallMask;
        private long remainder;
        private long[] stepSamples, sortedStepSamples;
        private int stepSampleCount;
        public bool StepTimingEnabled { get; private set; }

        // Allocate/reset outside the measured loop. A full buffer stops recording; it never grows.
        public void EnableStepTiming(int capacity = 9000)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            stepSamples = new long[capacity]; sortedStepSamples = new long[capacity];
            stepSampleCount = 0; StepTimingEnabled = true;
        }
        public void DisableStepTiming() => StepTimingEnabled = false;
        // Read on demand (for example at match end). Sorting uses preallocated scratch, not the samples.
        public SimStepTiming StepTiming
        {
            get
            {
                if (stepSampleCount == 0) return default;
                Array.Copy(stepSamples, sortedStepSamples, stepSampleCount);
                Array.Sort(sortedStepSamples, 0, stepSampleCount);
                double total = 0;
                for (int i = 0; i < stepSampleCount; i++) total += stepSamples[i];
                double ms = 1000.0 / Stopwatch.Frequency;
                return new SimStepTiming(stepSampleCount, total / stepSampleCount * ms,
                    sortedStepSamples[(stepSampleCount * 95L + 99) / 100 - 1] * ms,
                    sortedStepSamples[stepSampleCount - 1] * ms);
            }
        }

        internal SimSession(SessionParts parts)
        {
            facings = parts.Facings;
            combatPack = parts.CombatPack;
            controlled = parts.Controlled;
            maxHealthRaw = parts.MaxHealthRaw;
            combat = parts.Combat;
            matchPack = parts.MatchPack; capture = parts.Capture; reinforce = parts.Reinforce; match = parts.Match;
            groundRaw = parts.GroundRaw;
            map = parts.Map;
            wallMask = parts.WallMask;
            Map = parts.View;
            Terrain = parts.Terrain;
            TerrainRules = parts.TerrainRules;
            sim = parts.Sim;
            units = new StableId[parts.Units.Length];
            for (int i = 0; i < parts.Units.Length; i++) units[i] = sim.Entities.StableOf(parts.Units[i]);
            spawns = parts.Spawns;
            positions = parts.Positions;
            CommandLog = commandLog.AsReadOnly();
            PlayerLog = playerLog.AsReadOnly();
            Current = Capture();
            Previous = Current;
        }

        public static SimSession Create(WorldRecipe recipe) => WorldBuilder.Create(recipe);
        public static SimSession Create(WorldRecipe recipe, byte[] mapBytes) => WorldBuilder.Create(recipe, mapBytes);
        public ViewMap Map { get; }
        internal TerrainTables Terrain { get; }
        internal TerrainRules TerrainRules { get; }
        // View key is the original UnitSpawn.Key, not the hashed StableId or the physical entity slot.
        public ulong? ControlledKey
        {
            get
            {
                if (controlled == null || !sim.Entities.IsAlive(controlled.Current)) return null;
                StableId id = sim.Entities.StableOf(controlled.Current);
                for (int i = 0; i < units.Length; i++)
                    if (units[i].Equals(id)) return spawns[i].Key;
                return null;
            }
        }
        // The mode is the single owner of the result; Winner, EndTick and EndReason are projections of it.
        public bool IsFinished => sim.IsFinished;
        public int Winner => match?.Winner ?? 0;
        public long EndTick => match?.EndTick ?? 0;
        public int EndReason => (int)(match?.EndReason ?? MatchEndReason.None);
        public long Tick => sim.Clock.Tick;
        public ulong StateHash => sim.StateHashRetained();
        public FrameView Previous { get; private set; }
        public FrameView Current { get; private set; }
        public float Alpha => (float)remainder / MicrosPerSecond;
        public IReadOnlyList<BridgeCommand> CommandLog { get; }
        public IReadOnlyList<PlayerInputRecord> PlayerLog { get; }

        // Collision view of the map: does a static wall touch the centre of cell (x, z)? Test and bake probe.
        // The probe stands at FlatHeight + 0.5 m whatever the ground does: on a legal terrain map every wall starts at
        // FlatHeight = Min.Y and rises at least 1 m above the highest ground, so the fixed probe never misses one.
        // Navigation on terrain is not this: it is the file bit AND a walkable surface AND a legal slope (TerrainCheck).
        public bool BlockedCell(int x, int z)
        {
            if (map == null || wallMask == 0)
                return false;
            var centre = new SimVec3(map.Min.X + SimFix.FromInt(x) + SimFix.Ratio(1, 2), map.FlatHeight + SimFix.Ratio(1, 2),
                map.Min.Z + SimFix.FromInt(z) + SimFix.Ratio(1, 2));
            return sim.SpatialQuery.Any(SpatialQueries.Sphere(centre, default, wallMask));
        }

        // Temporary HUD interface retained in WP-18; migration to ViewEntity is deferred.
        // Samples the current committed Tick (after StepOnce/Advance when called by the host).
        // WaitTicksLeft = max(0, ceil(DueMilli/1000) - Tick), for both shot and reload waits.
        // Zero wait does not imply a living, armed unit. Resource 1 is CombatPack's health.
        // Facing of a unit as raw fixed-point (X, Z) of a direction vector; false when the recipe did not open facing or the key is unknown.
        public bool TryFacing(ulong viewKey, out long xRaw, out long zRaw)
        {
            xRaw = zRaw = 0;
            if (facings == null)
                return false;
            for (int i = 0; i < units.Length; i++)
            {
                if (spawns[i].Key != viewKey)
                    continue;
                UnitFacing value = sim.Entities.Read(facings.Value, sim.Entities.Resolve(new EntityLink(units[i])));
                xRaw = value.X.Raw; zRaw = value.Z.Raw;
                return true;
            }
            return false;
        }

        public bool TryUnitStatus(ulong viewKey, out UnitStatus status)
        {
            status = default;
            if (combat == null)
                return false;
            for (int i = 0; i < units.Length; i++)
            {
                if (spawns[i].Key != viewKey)
                    continue;
                EntityId unit = sim.Entities.Resolve(new EntityLink(units[i]));
                Combatant state = combat.Unit(unit);
                bool hasWeapon = state.Weapon >= 0;
                long dueTick = state.DueMilli / 1000 + (state.DueMilli % 1000 > 0 ? 1 : 0);
                long wait = Math.Max(0, dueTick - Tick);
                bool reloading = hasWeapon && state.Ammo == combatPack.Weapons[state.Weapon].Magazine
                    && state.DueMilli > Tick * 1000;
                status = new UnitStatus((float)sim.GameplayResource(units[i], 1) / SimFix.Scale,
                    (float)maxHealthRaw / SimFix.Scale, state.Ammo, wait, reloading, !combat.IsDead(unit), hasWeapon,
                    combat.IsHolding(unit));
                return true;
            }
            return false;
        }

        private bool HasUnit(ulong key)
        {
            for (int i = 0; i < spawns.Length; i++)
                if (spawns[i].Key == key) return true;
            return false;
        }

        private EntityLink UnitLink(ulong key)
        {
            for (int i = 0; i < spawns.Length; i++)
                if (spawns[i].Key == key) return new EntityLink(units[i]);
            throw new ArgumentException("Follow target is not a unit of this session.", nameof(key));
        }

        // A squad target travels as a link beside the frame; frames without one take the plain path.
        private void StepSim(InputFrame input, EntityLink followTarget)
        {
            if (followTarget.HasValue) sim.StepStable(new StableInputFrame(input, followTarget));
            else sim.Step(input);
        }

        public void Enqueue(BridgeCommand command)
        {
            if (command.AtTick <= Tick)
                throw new ArgumentOutOfRangeException(nameof(command), "Command must target a future tick.");
            if (command.Kind == BridgeCommandKind.MoveTo)
                Target(command.XMm, command.ZMm, groundRaw);
            else if (command.Kind == BridgeCommandKind.Follow && !HasUnit(command.TargetKey))
                throw new ArgumentException("Follow target is not a unit of this session.", nameof(command));
            int index = 0;
            while (index < pending.Count && pending[index].AtTick < command.AtTick)
                index++;
            if (index < pending.Count && pending[index].AtTick == command.AtTick)
                return;
            pending.Insert(index, command);
        }

        public void SetPlayerInput(long atTick, PlayerInput input)
        {
            if (atTick <= Tick)
                throw new ArgumentOutOfRangeException(nameof(atTick), "Input must target a future tick.");
            if (controlled == null)
                throw new InvalidOperationException("This session has no player control source.");
            int index = 0;
            while (index < pendingPlayer.Count && pendingPlayer[index].AtTick < atTick)
                index++;
            var record = new PlayerInputRecord(atTick, input);
            if (index < pendingPlayer.Count && pendingPlayer[index].AtTick == atTick)
                pendingPlayer[index] = record;
            else
                pendingPlayer.Insert(index, record);
        }

        public int Advance(long elapsedMicros)
        {
            if (elapsedMicros < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedMicros));
            if (IsFinished) return 0;
            // Split whole seconds before multiplying, so even long.MaxValue is valid.
            long wholeTicks = elapsedMicros / MicrosPerSecond * SimClock.TickRate;
            long fraction = elapsedMicros % MicrosPerSecond * SimClock.TickRate + remainder;
            long ticks = wholeTicks + fraction / MicrosPerSecond;
            remainder = fraction % MicrosPerSecond;
            int count = (int)Math.Min(ticks, MaxTicksPerFrame);
            int done = 0;
            while (done < count && !IsFinished) { StepOnce(); done++; }
            return done;
        }

        // After the owner restored a Sim snapshot: show the restored frame (no interpolation) and drop any partial tick.
        public void RefreshView()
        {
            Current = Capture(); Previous = Current; remainder = 0;
        }

        public void StepOnce()
        {
            if (IsFinished) return;
            long nextTick = checked(Tick + 1);
            SquadCommand? squad = null;
            EntityLink followTarget = default;
            bool hasCommand = pending.Count > 0 && pending[0].AtTick == nextTick;
            if (hasCommand)
            {
                BridgeCommand command = pending[0];
                squad = command.Kind switch
                {
                    BridgeCommandKind.MoveTo => new SquadCommand(SquadCommandKind.MoveTo, command.Squad, Target(command.XMm, command.ZMm, groundRaw), default),
                    BridgeCommandKind.Hold => new SquadCommand(SquadCommandKind.Hold, command.Squad, default, default),
                    _ => new SquadCommand(SquadCommandKind.Follow, command.Squad, default, default)
                };
                if (command.Kind == BridgeCommandKind.Follow) followTarget = UnitLink(command.TargetKey);
            }
            var input = new InputFrame(default, default, squad: squad);
            PlayerInput nextPlayerInput = heldPlayerInput;
            int playerCount = 0;
            if (controlled != null)
            {
                while (playerCount < pendingPlayer.Count && pendingPlayer[playerCount].AtTick <= nextTick)
                    nextPlayerInput = pendingPlayer[playerCount++].Input;
                input = new InputFrame(
                    new SimVec3(SimFix.Ratio(nextPlayerInput.MoveXMilli, 1000), default, SimFix.Ratio(nextPlayerInput.MoveZMilli, 1000)),
                    nextPlayerInput.AimYMm.HasValue
                        ? TargetAt(nextPlayerInput.AimXMm, nextPlayerInput.AimYMm.Value, nextPlayerInput.AimZMm)
                        : Target(nextPlayerInput.AimXMm, nextPlayerInput.AimZMm, groundRaw),
                    primaryAttack: nextPlayerInput.Primary, block: nextPlayerInput.Block,
                    @throw: nextPlayerInput.Throw, skillSlot: nextPlayerInput.SkillSlot, squad: squad);
            }
            if (StepTimingEnabled && stepSampleCount < stepSamples.Length)
            {
                long start = Stopwatch.GetTimestamp();
                StepSim(input, followTarget);
                long end = Stopwatch.GetTimestamp();
                stepSamples[stepSampleCount++] = end - start;
            }
            else StepSim(input, followTarget);
            // Commit bridge state only after Sim has successfully advanced.
            if (!nextPlayerInput.Equals(heldPlayerInput))
                playerLog.Add(new PlayerInputRecord(nextTick, nextPlayerInput));
            heldPlayerInput = nextPlayerInput;
            if (playerCount > 0)
                pendingPlayer.RemoveRange(0, playerCount);
            if (hasCommand)
            {
                commandLog.Add(pending[0]);
                pending.RemoveAt(0);
            }
            Previous = Current;
            Current = Capture();
            if (IsFinished)
            {
                Previous = Current;
                remainder = 0;
            }
        }

        private FrameView Capture()
        {
            var views = new List<ViewEntity>(units.Length);
            int aliveOne = 0, aliveTwo = 0, reserveOne = 0, reserveTwo = 0;
            for (int i = 0; i < units.Length; i++)
            {
                EntityId unit = sim.Entities.Resolve(new EntityLink(units[i]));
                bool alive = combat == null || !combat.IsDead(unit);
                int team = combat == null ? 0 : combat.Unit(unit).Team;
                if (!sim.Entities.TryGet(positions, unit, out Position position))
                {
                    if (alive) { if (team == 1) reserveOne++; else if (team == 2) reserveTwo++; }
                    continue;
                }
                if (alive) { if (team == 1) aliveOne++; else if (team == 2) aliveTwo++; }
                SimVec3 point = position.Value;
                views.Add(new ViewEntity(spawns[i].Key, spawns[i].Archetype,
                    (float)point.X.Raw / SimFix.Scale, (float)point.Y.Raw / SimFix.Scale,
                    (float)point.Z.Raw / SimFix.Scale, spawns[i].Squad,
                    team, alive));
            }
            var fires = new ViewFire[combat == null ? 0 : combat.Fires.Length];
            for (int i = 0; i < fires.Length; i++)
                fires[i] = ToView(combat.Fires[i]);
            var points = new ViewPoint[capture == null ? 0 : capture.Points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                ControlPointDef def = matchPack.Points[i]; PointState state = capture.Points[i];
                points[i] = new ViewPoint(def.Id, (float)def.Center.X.Raw / SimFix.Scale, (float)def.Center.Z.Raw / SimFix.Scale,
                    (float)def.Radius.Raw / SimFix.Scale, state.Owner, state.Capturing, (float)state.Progress / matchPack.CaptureTicks);
            }
            // Respawn as RespawnSystem reads it: the controlled unit is dead and respawn is on (a controlled unit implies match
            // rules: WorldBuilder refuses a player without them). Reserves are the ones counted above (alive, no Position), the
            // rule ReinforceSystem and RespawnSystem take reserves by. Not hashed, recomputed every frame.
            ViewRespawn respawn = default;
            if (controlled != null && matchPack.RespawnTicks > 0 && sim.Entities.IsAlive(controlled.Current))
            {
                EntityId player = controlled.Current;
                GameplayLifeView life = sim.GameplayLife(sim.Entities.StableOf(player));
                if (life.State == GameplayLifeState.Dead)
                {
                    int team = combat.Unit(player).Team;
                    int reserves = team == 1 ? reserveOne : team == 2 ? reserveTwo : 0;
                    long left = Math.Max(0, life.EnteredTick + matchPack.RespawnTicks - Tick);
                    RespawnPhase state = reserves == 0 ? RespawnPhase.NoReserve : left > 0 ? RespawnPhase.Waiting : RespawnPhase.Overdue;
                    respawn = new ViewRespawn(state, left, reserves);
                }
            }
            return new FrameView(Tick, views.ToArray(), fires, points,
                new ViewMatch(Winner, (int)EndTick, EndReason, aliveOne, aliveTwo, reserveOne, reserveTwo, IsFinished), respawn);
        }

        public static ViewFire ToView(in FireRecord fire) => new ViewFire(
            (float)fire.Origin.X.Raw / SimFix.Scale, (float)fire.Origin.Z.Raw / SimFix.Scale,
            (float)fire.End.X.Raw / SimFix.Scale, (float)fire.End.Z.Raw / SimFix.Scale,
            fire.ShooterTeam, fire.DamageRaw > 0);

        // A4: an aim point with its own height. X and Z convert and check exactly as in Target; Y = yMm / 1000 m.
        internal static SimVec3 TargetAt(long xMm, long yMm, long zMm)
        {
            CheckAimY(yMm);
            SimVec3 flat = Target(xMm, zMm);
            var target = new SimVec3(flat.X, SimFix.Ratio(yMm, 1000), flat.Z);
            target.ValidateWorld();
            return target;
        }

        // The height bound of PlayerInput.AimYMm (the world limit, as for X and Z).
        internal static void CheckAimY(long yMm)
        {
            const long limitMm = SimVec3.WorldLimitMetres * 1000L;
            if (yMm < -limitMm || yMm > limitMm)
                throw new ArgumentOutOfRangeException("aimYMm", "Aim height exceeds the world bounds.");
        }

        internal static SimVec3 Target(long xMm, long zMm, long groundRaw = 0)
        {
            const long limitMm = SimVec3.WorldLimitMetres * 1000L;
            if (xMm < -limitMm || xMm > limitMm || zMm < -limitMm || zMm > limitMm)
                throw new ArgumentOutOfRangeException(nameof(xMm), "Target exceeds the world bounds.");
            var target = new SimVec3(SimFix.Ratio(xMm, 1000), SimFix.FromRaw(groundRaw), SimFix.Ratio(zMm, 1000));
            target.ValidateWorld();
            return target;
        }
    }
}
