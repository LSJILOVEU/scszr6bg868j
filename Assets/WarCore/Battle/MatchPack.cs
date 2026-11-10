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
        public ControlPointDef(int id, SimVec3 center, SimFix radius, int initialOwner = 0, bool hasSpawn = false, SimVec3 spawn = default)
        {
            if (radius.Raw <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (initialOwner < 0 || initialOwner > 2) throw new ArgumentOutOfRangeException(nameof(initialOwner));
            Id = id; Center = center; Radius = radius; InitialOwner = initialOwner; HasSpawn = hasSpawn; Spawn = spawn;
        }
        internal void WriteState(StateHash hash)
        { hash.Add(Id); Center.WriteState(hash); hash.Add(Radius.Raw); hash.Add(InitialOwner); hash.Add(HasSpawn); Spawn.WriteState(hash); }
    }

    public readonly struct SpawnAnchor
    {
        public readonly int Team;
        public readonly SimVec3 Point;
        public SpawnAnchor(int team, SimVec3 point)
        {
            if (team < 1 || team > 2) throw new ArgumentOutOfRangeException(nameof(team));
            Team = team; Point = point;
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
        public ulong PolicyDigest { get; }
        public const int MaxRespawnTicks = 36000;

        public MatchPack(ControlPointDef[] points, SpawnAnchor[] bases, int num, int den, bool strict,
            int captureTicks, InterruptMode interrupt, int decayPerTick, int intervalTicks, int thinkInterval,
            int maxTicks, SimFix sight, int gridColumns, int gridRows, SimFix gridSpacing, int respawnTicks = 0, int respawnGuardTicks = 0,
            string[] rules = null)
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
            this.rules = rules == null ? DefaultRules : (string[])rules.Clone();
            for (int i = 0; i < this.rules.Length; i++)
                if (!MatchMode.RuleFactories.ContainsKey(this.rules[i])) throw new ArgumentException("rules contains unknown id '" + this.rules[i] + "'.", nameof(rules));
            this.points = (ControlPointDef[])points.Clone(); this.bases = (SpawnAnchor[])bases.Clone();
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Radius.Raw <= 0) throw new ArgumentException("Point radius must be positive.");
                for (int j = 0; j < i; j++) if (points[i].Id == points[j].Id) throw new ArgumentException("Point IDs must be unique.");
            }
            Num = num; Den = den; Strict = strict; CaptureTicks = captureTicks; Interrupt = interrupt;
            DecayPerTick = decayPerTick; IntervalTicks = intervalTicks; ThinkInterval = thinkInterval; MaxTicks = maxTicks;
            Sight = sight; GridColumns = gridColumns; GridRows = gridRows; GridSpacing = gridSpacing; RespawnTicks = respawnTicks; RespawnGuardTicks = respawnGuardTicks;
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
            PolicyDigest = hash.Value;
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
    }
}
