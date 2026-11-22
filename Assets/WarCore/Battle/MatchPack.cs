using System;
using WarCore.Sim;

namespace WarCore.Battle
{
    public enum InterruptMode { Reset, Pause, Decay }
    public enum MatchEndReason { None, AllPoints, Exhausted, Time, Draw }

    public readonly struct ControlPointDef
    {
        public readonly int Id, InitialOwner;
        public readonly SimVec3 Center, Spawn;
        public readonly SimFix Radius;
        public readonly bool HasSpawn;
        // The layer the spawn stands on: 0 = the ground, 1..N = a floor plate (only with a spawn).
        public readonly int SpawnLayer;
        // ML-9b: the layer the centre stands on: 0 = the ground, 1..N = a floor plate. Only units on this layer are in the circle.
        public readonly int Layer;
        public ControlPointDef(int id, SimVec3 center, SimFix radius, int initialOwner = 0, bool hasSpawn = false, SimVec3 spawn = default,
            int spawnLayer = 0, int layer = 0)
        {
            if (radius.Raw <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (initialOwner < 0 || initialOwner > MatchPack.MaxTeams) throw new ArgumentOutOfRangeException(nameof(initialOwner));
            if (spawnLayer < 0 || spawnLayer > MatchPack.MaxLayer) throw new ArgumentOutOfRangeException(nameof(spawnLayer));
            if (spawnLayer != 0 && !hasSpawn) throw new ArgumentException("A spawn layer needs a spawn.", nameof(spawnLayer));
            if (layer < 0 || layer > MatchPack.MaxLayer) throw new ArgumentOutOfRangeException(nameof(layer));
            Layer = layer;
            Id = id; Center = center; Radius = radius; InitialOwner = initialOwner; HasSpawn = hasSpawn; Spawn = spawn; SpawnLayer = spawnLayer;
        }
        internal void WriteState(StateHash hash)
        { hash.Add(Id); Center.WriteState(hash); hash.Add(Radius.Raw); hash.Add(InitialOwner); hash.Add(HasSpawn); Spawn.WriteState(hash); }
    }

    public readonly struct SpawnAnchor
    {
        public readonly int Team;
        public readonly SimVec3 Point;
        // 0 = the ground, 1..N = a floor plate.
        public readonly int Layer;
        public SpawnAnchor(int team, SimVec3 point, int layer = 0)
        {
            if (team < 1 || team > MatchPack.MaxTeams) throw new ArgumentOutOfRangeException(nameof(team));
            if (layer < 0 || layer > MatchPack.MaxLayer) throw new ArgumentOutOfRangeException(nameof(layer));
            Team = team; Point = point; Layer = layer;
        }
    }

    public sealed class MatchPack
    {
        private static readonly string[] DefaultRules = { "all-points", "exhausted", "time-limit" };
        private readonly string[] rules;
        public ReadOnlySpan<string> Rules => rules;
        private readonly ControlPointDef[] points;
        private readonly SpawnAnchor[] bases;
        public ReadOnlySpan<ControlPointDef> Points => points;
        public ReadOnlySpan<SpawnAnchor> Bases => bases;
        public int Num { get; }
        public int Den { get; }
        public bool Strict { get; }
        public int CaptureTicks { get; }
        public InterruptMode Interrupt { get; }
        public int DecayPerTick { get; }
        public int IntervalTicks { get; }
        public int ThinkInterval { get; }
        public int MaxTicks { get; }
        public SimFix Sight { get; }
        public int GridColumns { get; }
        public int GridRows { get; }
        public SimFix GridSpacing { get; }
        // Ticks from the player's death to the switch to a reserve; 0 turns respawn off.
        public int RespawnTicks { get; }
        // Ticks after a respawn switch during which combat deals the new unit no damage; 0 turns it off. Needs a positive delay.
        public int RespawnGuardTicks { get; }
        // RESPAWN-AI-1: the timed reinforcement leaves out points another team contests (PointPresence); false keeps the old list.
        public bool ReinforceSkipContested { get; }
        // RESPAWN-AI-2: ticks from a death to the reserve that replaces it; 0 keeps the timed reinforcement.
        public int ReentryTicks { get; }
        // RESPAWN-AI-2: reserves a map file start adds for each team with a base, and their unit type id; 0 adds none. The
        // pack only carries the two: MapRecipe.From makes the units.
        public int ReserveCount { get; }
        public string ReserveType { get; }
        public ulong PolicyDigest { get; }
        // NT-1: teams 1..TeamCount take part; 0 is no team. At least 2, else the highest team a base anchor or an initial owner
        // names. Fixed by the pack (never by the units), so state sized by it is known when the world is built. Not hashed:
        // the digest already covers every anchor team and initial owner.
        public int TeamCount { get; }
        public const int MaxTeams = 8;
        public const int MaxRespawnTicks = 36000;
        public const int MaxReentryTicks = 36000;
        public const int MaxReserveCount = 1024;
        // The highest layer an anchor may name: the plate limit of a map (as UnitSpawn.MaxLayer and the Sim's).
        public const int MaxLayer = 4096;

        public MatchPack(ControlPointDef[] points, SpawnAnchor[] bases, int num, int den, bool strict,
            int captureTicks, InterruptMode interrupt, int decayPerTick, int intervalTicks, int thinkInterval,
            int maxTicks, SimFix sight, int gridColumns, int gridRows, SimFix gridSpacing, int respawnTicks = 0, int respawnGuardTicks = 0,
            string[] rules = null, bool reinforceSkipContested = false, int reentryTicks = 0, int reserveCount = 0, string reserveType = null)
        {
            if (den < 1 || num < den) throw new ArgumentOutOfRangeException(nameof(num));
            if (captureTicks < 1 || intervalTicks < 1 || thinkInterval < 1 || maxTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(captureTicks));
            if (interrupt < InterruptMode.Reset || interrupt > InterruptMode.Decay || (interrupt == InterruptMode.Decay && decayPerTick < 1))
                throw new ArgumentOutOfRangeException(nameof(interrupt));
            if (points.Length < 1 || bases.Length < 1) throw new ArgumentException("Points and base anchors are required.");
            if (sight.Raw <= 0 || gridColumns < 1 || gridRows < 1 || gridSpacing.Raw <= 0)
                throw new ArgumentOutOfRangeException(nameof(sight));
            if (respawnTicks < 0 || respawnTicks > MaxRespawnTicks) throw new ArgumentOutOfRangeException(nameof(respawnTicks));
            if (respawnGuardTicks < 0 || respawnGuardTicks > MaxRespawnTicks || (respawnGuardTicks > 0 && respawnTicks == 0))
                throw new ArgumentOutOfRangeException(nameof(respawnGuardTicks));
            if (reentryTicks < 0 || reentryTicks > MaxReentryTicks) throw new ArgumentOutOfRangeException(nameof(reentryTicks));
            if (reserveCount < 0 || reserveCount > MaxReserveCount) throw new ArgumentOutOfRangeException(nameof(reserveCount));
            if (reserveCount > 0 && string.IsNullOrEmpty(reserveType)) throw new ArgumentException("A reserve count needs a reserve type.", nameof(reserveType));
            this.rules = rules == null ? DefaultRules : (string[])rules.Clone();
            for (int i = 0; i < this.rules.Length; i++)
                if (!MatchMode.RuleFactories.ContainsKey(this.rules[i])) throw new ArgumentException("rules contains unknown id '" + this.rules[i] + "'.", nameof(rules));
            this.points = (ControlPointDef[])points.Clone(); this.bases = (SpawnAnchor[])bases.Clone();
            int teams = 2;
            foreach (var anchor in this.bases) teams = Math.Max(teams, anchor.Team);
            foreach (var point in this.points) teams = Math.Max(teams, point.InitialOwner);
            TeamCount = teams;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Radius.Raw <= 0) throw new ArgumentException("Point radius must be positive.");
                for (int j = 0; j < i; j++) if (points[i].Id == points[j].Id) throw new ArgumentException("Point IDs must be unique.");
            }
            Num = num; Den = den; Strict = strict; CaptureTicks = captureTicks; Interrupt = interrupt;
            DecayPerTick = decayPerTick; IntervalTicks = intervalTicks; ThinkInterval = thinkInterval; MaxTicks = maxTicks;
            Sight = sight; GridColumns = gridColumns; GridRows = gridRows; GridSpacing = gridSpacing; RespawnTicks = respawnTicks; RespawnGuardTicks = respawnGuardTicks;
            ReinforceSkipContested = reinforceSkipContested;
            ReentryTicks = reentryTicks; ReserveCount = reserveCount; ReserveType = reserveCount > 0 ? reserveType : null;
            var hash = new StateHash(); hash.Add("battle.pack.v1"); hash.Add(points.Length);
            foreach (var point in this.points) point.WriteState(hash);
            hash.Add(bases.Length); foreach (var anchor in this.bases) { hash.Add(anchor.Team); anchor.Point.WriteState(hash); }
            hash.Add(num); hash.Add(den); hash.Add(strict); hash.Add(captureTicks); hash.Add((int)interrupt); hash.Add(decayPerTick);
            hash.Add(intervalTicks); hash.Add(thinkInterval); hash.Add(maxTicks); hash.Add(sight.Raw);
            hash.Add(gridColumns); hash.Add(gridRows); hash.Add(gridSpacing.Raw);
            // Only a positive delay is hashed, so a pack without respawn keeps its digest from before the field existed.
            if (respawnTicks > 0) { hash.Add("battle.pack.respawn.v1"); hash.Add(respawnTicks); }
            if (respawnGuardTicks > 0) { hash.Add("battle.pack.respawn-guard.v1"); hash.Add(respawnGuardTicks); }
            // Default rule order keeps the existing snapshot digest.
            if (!Rules.SequenceEqual(DefaultRules))
            { hash.Add("battle.pack.rules.v1"); hash.Add(this.rules.Length); foreach (string id in this.rules) hash.Add(id); }
            // Only when some anchor is on a plate, so a pack on the ground keeps its digest from before layers existed.
            if (HasLayers())
            {
                hash.Add("battle.pack.layers.v1");
                foreach (var point in this.points) hash.Add(point.SpawnLayer);
                foreach (var anchor in this.bases) hash.Add(anchor.Layer);
            }
            // ML-9b: only when some centre is on a plate, so a pack with ground centres keeps its digest.
            if (HasCenterLayers())
            {
                hash.Add("battle.pack.center-layers.v1");
                foreach (var point in this.points) hash.Add(point.Layer);
            }
            // RESPAWN-AI-1: only when on, so a pack without the switch keeps its digest.
            if (reinforceSkipContested) hash.Add("battle.pack.reinforce-skip.v1");
            // RESPAWN-AI-2: each only when on, so a pack without them keeps its digest. The type is not hashed without a count.
            if (reentryTicks > 0) { hash.Add("battle.pack.reentry.v1"); hash.Add(reentryTicks); }
            if (reserveCount > 0) { hash.Add("battle.pack.reserve.v1"); hash.Add(reserveCount); hash.Add(reserveType); }
            PolicyDigest = hash.Value;
        }
        private bool HasLayers()
        {
            foreach (var point in points) if (point.SpawnLayer != 0) return true;
            foreach (var anchor in bases) if (anchor.Layer != 0) return true;
            return false;
        }
        // ML-9b: some point's centre stands on a plate.
        public bool HasCenterLayers()
        {
            foreach (var point in points) if (point.Layer != 0) return true;
            return false;
        }
        internal void Check(SimStateReader reader)
        {
            if (reader.UInt64() != PolicyDigest) throw new InvalidOperationException("Battle configuration differs from snapshot.");
        }
    }

    public interface IUnitDeployer
    {
        ulong PolicyKey { get; }
        bool TryDeploy(EntityId unit, SimVec3 at);
        // Deploy on a layer: 0 is the ground (the two-argument method); a deployer without floor plates knows no other layer.
        bool TryDeploy(EntityId unit, SimVec3 at, int layer)
        {
            if (layer == 0) return TryDeploy(unit, at);
            throw new NotSupportedException("This deployer has no floor plates.");
        }
    }
    public interface IMoveOrders
    {
        bool CanStandAt(SimVec3 at) => true;
        // True while a squad command (hold, follow, move) is being carried out: the AI leaves such a unit alone.
        bool IsCommanded(StableId unit) => false;
        void Order(StableId unit, SimVec3 target);
        void Stop(StableId unit);
        bool TryGetOrder(StableId unit, out SimVec3 target);
        bool HasFailed(StableId unit) => false;
        // Walking cost from one point to another when it is at most limitCost (1000 per orthogonal cell step times the cell cost,
        // 1414 per diagonal step); false when there is no such route or no navigation. Read only: never changes the simulation.
        bool TryRouteCost(SimVec3 from, SimVec3 to, long limitCost, out long cost) { cost = 0; return false; }
        // ML-11a: the same with a layer (0 = the ground, 1..N = a floor plate). An implementation without plates keeps these
        // defaults: layer 0 is the port without a layer, and nothing stands on, or is ordered to, any other layer.
        bool CanStandAt(SimVec3 at, int layer) => layer == 0 && CanStandAt(at);
        void Order(StableId unit, SimVec3 target, int layer)
        {
            if (layer != 0) throw new NotSupportedException("Layer orders need a world with floor plates.");
            Order(unit, target);
        }
        bool TryGetOrder(StableId unit, out SimVec3 target, out int layer) { layer = 0; return TryGetOrder(unit, out target); }
    }
}
