using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    public readonly struct DeployRecord
    {
        public readonly long Tick;
        public readonly StableId Unit;
        public readonly int Team;
        public readonly SimVec3 Point;
        // RESPAWN-AI-1: the layer the unit was deployed on and the index in MatchPack.Points of the point it came out at (-1: a base).
        public readonly int Layer;
        public readonly int PointIndex;
        public DeployRecord(long tick, StableId unit, int team, SimVec3 point, int layer, int pointIndex)
        { Tick = tick; Unit = unit; Team = team; Point = point; Layer = layer; PointIndex = pointIndex; }
    }
    // Timed reinforcement: every IntervalTicks each team counts an attempt and deploys its first reserve (in Capture() order).
    // The entry list is the team's bases in pack order, then its owned points with a spawn in pack order; with
    // match.reinforceSkipContested a point another team contests (PointPresence, the rule capture counts by) is left out.
    // An empty list or no reserve wastes the attempt. The first entry tried is attempt % count, or what a bound IEntryChooser
    // returns; from there the system goes on round the list, and on each entry round the grid from cell attempt % cells.
    public sealed class ReinforceSystem : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        private readonly IUnitDeployer deployer;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly PointPresence presence;
        private readonly long[] next, attempts, successes;
        private readonly EntryOption[] options;
        private readonly DeployRecord[] deployed;
        private int deployCount;
        private IEntryChooser chooser;
        private bool started;
        // layers: on a map with floor plates (ML-9b) an enemy contests a point only on the point's layer; null keeps the plane alone.
        // Read only with match.reinforceSkipContested.
        public ReinforceSystem(CombatSystem combat, MatchPack pack, CaptureSystem capture, IUnitDeployer deployer, long startTick = 0, IUnitLayers layers = null)
        {
            this.combat = combat; this.pack = pack; this.capture = capture; this.deployer = deployer;
            stage = new EntityStage(combat.Entities); positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            presence = new PointPresence(combat, pack.TeamCount, layers);
            options = new EntryOption[pack.Bases.Length + pack.Points.Length];
            int teams = pack.TeamCount;
            next = new long[teams]; attempts = new long[teams]; successes = new long[teams]; deployed = new DeployRecord[teams];
            for (int i = 0; i < teams; i++) next[i] = startTick + pack.IntervalTicks;
        }
        public int Order => 8;
        public string StateTypeId => "battle.reinforce";
        // NT-1: two teams keep version 1 and its bytes; more teams save TeamCount triples (version 2). RESPAWN-AI-1: a bound
        // chooser adds its key to the bytes and 2 to the version; without one the bytes and version are as before.
        public int StateVersion => (pack.TeamCount == 2 ? 1 : 2) + (chooser == null ? 0 : 2);
        // Kept until this system's next step: systems ordered after 8 read this tick's deployments.
        public ReadOnlySpan<DeployRecord> Deployed => deployed.AsSpan(0, deployCount);
        public long Attempts(int team) => attempts[team - 1];
        public long Successes(int team) => successes[team - 1];
        public long NextTick(int team) => next[team - 1];

        // RESPAWN-AI-1: binds the entry chooser, once, before the first step and before a snapshot is loaded.
        public void BindChooser(IEntryChooser chooser)
        {
            if (chooser == null) throw new ArgumentNullException(nameof(chooser));
            if (this.chooser != null) throw new InvalidOperationException("An entry chooser is already bound.");
            if (started) throw new InvalidOperationException("The entry chooser is bound before the first step.");
            this.chooser = chooser;
        }

        public void Step(SimContext c)
        {
            started = true;
            deployCount = 0;
            for (int team = 1; team <= pack.TeamCount; team++)
            {
                int index = team - 1;
                if (c.Clock.Tick < next[index]) continue;
                next[index] += pack.IntervalTicks;
                long attempt = attempts[index]++;
                EntityId reserve = default; bool found = false;
                foreach (EntityId entity in stage.Capture())
                    if (combat.Entities.TryGet(combatants, entity, out Combatant unit) && unit.Team == team
                        && !combat.Entities.TryGet(positions, entity, out _) && !combat.IsDead(entity))
                    { reserve = entity; found = true; break; }
                if (!found) continue;
                int count = 0;
                foreach (var anchor in pack.Bases) if (anchor.Team == team) options[count++] = new EntryOption(true, -1, anchor.Point, anchor.Layer);
                for (int i = 0; i < pack.Points.Length; i++)
                    if (capture.Points[i].Owner == team && pack.Points[i].HasSpawn && !(pack.ReinforceSkipContested && presence.Contested(team, pack.Points[i])))
                        options[count++] = new EntryOption(false, i, pack.Points[i].Spawn, pack.Points[i].SpawnLayer);
                if (count == 0) continue;
                int first = (int)(attempt % count);
                if (chooser != null)
                {
                    first = chooser.First(team, reserve, new ReadOnlySpan<EntryOption>(options, 0, count), attempt);
                    if (first < 0 || first >= count)
                        throw new ArgumentOutOfRangeException(nameof(IEntryChooser.First), first, "The entry chooser returned an index outside the list.");
                }
                int cells = pack.GridColumns * pack.GridRows;
                bool done = false;
                for (int a = 0; a < count && !done; a++)
                    for (int cell = 0; cell < cells; cell++)
                    {
                        int slot = (int)((attempt + cell) % cells);
                        SimVec3 offset = new SimVec3(SimFix.Ratio(2 * (slot % pack.GridColumns) - pack.GridColumns + 1, 2) * pack.GridSpacing,
                            default, SimFix.Ratio(2 * (slot / pack.GridColumns) - pack.GridRows + 1, 2) * pack.GridSpacing);
                        EntryOption entry = options[(first + a) % count];
                        SimVec3 point = entry.At + offset;
                        if (!deployer.TryDeploy(reserve, point, entry.Layer)) continue;
                        successes[index]++;
                        deployed[deployCount++] = new DeployRecord(c.Clock.Tick, combat.Entities.StableOf(reserve), team, point, entry.Layer, entry.Point);
                        done = true; break;
                    }
            }
        }
        public void SaveState(SimStateWriter writer)
        {
            writer.UInt64(pack.PolicyDigest); writer.UInt64(deployer.PolicyKey);
            if (chooser != null) writer.UInt64(chooser.PolicyKey);
            for (int i = 0; i < pack.TeamCount; i++) { writer.Int64(next[i]); writer.Int64(attempts[i]); writer.Int64(successes[i]); }
        }
        public void LoadState(SimStateReader reader)
        {
            started = true;
            pack.Check(reader); SimStateReader.Require(reader.UInt64() == deployer.PolicyKey, "Deployment configuration differs.");
            if (chooser != null) SimStateReader.Require(reader.UInt64() == chooser.PolicyKey, "Entry chooser differs.");
            for (int i = 0; i < pack.TeamCount; i++) { next[i] = reader.Int64(); attempts[i] = reader.Int64(); successes[i] = reader.Int64(); }
            deployCount = 0;
        }
        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add(deployer.PolicyKey);
            if (chooser != null) hash.Add(chooser.PolicyKey);
            for (int i = 0; i < pack.TeamCount; i++) { hash.Add(next[i]); hash.Add(attempts[i]); hash.Add(successes[i]); }
        }
    }
}
