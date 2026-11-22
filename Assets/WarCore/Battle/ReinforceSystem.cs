using System;
using WarCore.Combat;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

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
    //
    // RESPAWN-AI-2: with a positive match.reentryTicks the same system (same id, same order) deploys by debt and IntervalTicks
    // is not read. Per team: due = its units that are dead and died at least reentryTicks ago, the unit the player controls
    // left out (the player's respawn replaces that one); entered = a saved count, plus one for each deployment here and for
    // each takeover the player's respawn reports (NoteTakeover); owed = due - entered. Each step a team that is owed deploys
    // one reserve, at most one, through the same list, chooser and grid, where the attempt number is the team's successes
    // here. No reserve, an empty list or no free cell leaves every count as it was and the next step tries again.
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
        // RESPAWN-AI-2: entered is saved; due is what the last step counted (not saved, as Deployed).
        private readonly long[] entered, due;
        private readonly Simulation sim;
        private readonly IControlTarget controlled;
        private readonly EntryOption[] options;
        private readonly DeployRecord[] deployed;
        private int deployCount;
        private IEntryChooser chooser;
        private bool started;
        // layers: on a map with floor plates (ML-9b) an enemy contests a point only on the point's layer; null keeps the plane alone.
        // Read only with match.reinforceSkipContested.
        // sim, controlled: read only with match.reentryTicks, which needs sim (the death ticks); controlled is the player's
        // control target, null in a world without a player.
        public ReinforceSystem(CombatSystem combat, MatchPack pack, CaptureSystem capture, IUnitDeployer deployer, long startTick = 0, IUnitLayers layers = null,
            Simulation sim = null, IControlTarget controlled = null)
        {
            if (pack.ReentryTicks > 0 && sim == null) throw new ArgumentNullException(nameof(sim), "match.reentryTicks reads death ticks from the simulation.");
            this.combat = combat; this.pack = pack; this.capture = capture; this.deployer = deployer; this.sim = sim; this.controlled = controlled;
            stage = new EntityStage(combat.Entities); positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            presence = new PointPresence(combat, pack.TeamCount, layers);
            options = new EntryOption[pack.Bases.Length + pack.Points.Length];
            int teams = pack.TeamCount;
            next = new long[teams]; attempts = new long[teams]; successes = new long[teams]; deployed = new DeployRecord[teams];
            entered = new long[teams]; due = new long[teams];
            for (int i = 0; i < teams; i++) next[i] = startTick + pack.IntervalTicks;
        }
        public int Order => 8;
        public string StateTypeId => "battle.reinforce";
        // NT-1: two teams keep version 1 and its bytes; more teams save TeamCount triples (version 2). RESPAWN-AI-1: a bound
        // chooser adds its key to the bytes and 2 to the version; without one the bytes and version are as before.
        // RESPAWN-AI-2: by debt the state is other numbers (entered and successes per team) and the version is 4 higher.
        public int StateVersion => (pack.TeamCount == 2 ? 1 : 2) + (chooser == null ? 0 : 2) + (ByDebt ? 4 : 0);
        public bool ByDebt => pack.ReentryTicks > 0;
        // Kept until this system's next step: systems ordered after 8 read this tick's deployments.
        public ReadOnlySpan<DeployRecord> Deployed => deployed.AsSpan(0, deployCount);
        public long Attempts(int team) => attempts[team - 1];
        public long Successes(int team) => successes[team - 1];
        public long NextTick(int team) => next[team - 1];
        // RESPAWN-AI-2 (all 0 without match.reentryTicks). Due is as the last step counted it, 0 before the first step and
        // after a load; Owed may be negative (the player took a reserve over before the body was due).
        public long Entered(int team) => entered[team - 1];
        public long Due(int team) => due[team - 1];
        public long Owed(int team) => due[team - 1] - entered[team - 1];

        // The player's respawn took a reserve of this team over: that reserve entered. Called by RespawnSystem, which runs
        // before this system in the step, only in a world with match.reentryTicks.
        public void NoteTakeover(int team)
        {
            if (!ByDebt) throw new InvalidOperationException("Takeovers are counted only with match.reentryTicks.");
            entered[team - 1]++;
        }

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
            if (ByDebt) CountDue(c.Clock.Tick);
            for (int team = 1; team <= pack.TeamCount; team++)
            {
                int index = team - 1;
                long attempt;
                if (ByDebt)
                {
                    if (due[index] - entered[index] <= 0) continue;
                    attempt = successes[index];
                }
                else
                {
                    if (c.Clock.Tick < next[index]) continue;
                    next[index] += pack.IntervalTicks;
                    attempt = attempts[index]++;
                }
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
                        if (ByDebt) entered[index]++;
                        deployed[deployCount++] = new DeployRecord(c.Clock.Tick, combat.Entities.StableOf(reserve), team, point, entry.Layer, entry.Point);
                        done = true; break;
                    }
            }
        }

        // RESPAWN-AI-2: each team's units that are dead since at least ReentryTicks. Bodies stay in the world, so none is
        // missed; a downed unit is not dead; the unit the player controls is not counted while it is controlled.
        private void CountDue(long tick)
        {
            Array.Clear(due, 0, due.Length);
            StableId player = default;
            if (controlled != null && combat.Entities.IsAlive(controlled.Current)) player = combat.Entities.StableOf(controlled.Current);
            foreach (EntityId entity in stage.Capture())
            {
                if (!combat.Entities.TryGet(combatants, entity, out Combatant unit) || unit.Team < 1 || unit.Team > pack.TeamCount || !combat.IsDead(entity)) continue;
                StableId id = combat.Entities.StableOf(entity);
                if (id.Equals(player)) continue;
                if (tick >= sim.GameplayLife(id).EnteredTick + pack.ReentryTicks) due[unit.Team - 1]++;
            }
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.UInt64(pack.PolicyDigest); writer.UInt64(deployer.PolicyKey);
            if (chooser != null) writer.UInt64(chooser.PolicyKey);
            if (ByDebt) { for (int i = 0; i < pack.TeamCount; i++) { writer.Int64(entered[i]); writer.Int64(successes[i]); } return; }
            for (int i = 0; i < pack.TeamCount; i++) { writer.Int64(next[i]); writer.Int64(attempts[i]); writer.Int64(successes[i]); }
        }
        public void LoadState(SimStateReader reader)
        {
            started = true;
            pack.Check(reader); SimStateReader.Require(reader.UInt64() == deployer.PolicyKey, "Deployment configuration differs.");
            if (chooser != null) SimStateReader.Require(reader.UInt64() == chooser.PolicyKey, "Entry chooser differs.");
            deployCount = 0;
            if (ByDebt)
            {
                for (int i = 0; i < pack.TeamCount; i++) { entered[i] = reader.Int64(); successes[i] = reader.Int64(); }
                Array.Clear(due, 0, due.Length);
                return;
            }
            for (int i = 0; i < pack.TeamCount; i++) { next[i] = reader.Int64(); attempts[i] = reader.Int64(); successes[i] = reader.Int64(); }
        }
        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add(deployer.PolicyKey);
            if (chooser != null) hash.Add(chooser.PolicyKey);
            if (ByDebt) { for (int i = 0; i < pack.TeamCount; i++) { hash.Add(entered[i]); hash.Add(successes[i]); } return; }
            for (int i = 0; i < pack.TeamCount; i++) { hash.Add(next[i]); hash.Add(attempts[i]); hash.Add(successes[i]); }
        }
    }
}
