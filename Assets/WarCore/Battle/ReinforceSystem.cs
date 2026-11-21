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
        public DeployRecord(long tick, StableId unit, int team, SimVec3 point) { Tick = tick; Unit = unit; Team = team; Point = point; }
    }
    public sealed class ReinforceSystem : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        private readonly IUnitDeployer deployer;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly long[] next, attempts, successes;
        private readonly SimVec3[] anchors;
        private readonly int[] layers;
        private readonly DeployRecord[] deployed;
        private int deployCount;
        public ReinforceSystem(CombatSystem combat, MatchPack pack, CaptureSystem capture, IUnitDeployer deployer, long startTick = 0)
        {
            this.combat = combat; this.pack = pack; this.capture = capture; this.deployer = deployer;
            stage = new EntityStage(combat.Entities); positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            anchors = new SimVec3[pack.Bases.Length + pack.Points.Length];
            layers = new int[anchors.Length];
            int teams = pack.TeamCount;
            next = new long[teams]; attempts = new long[teams]; successes = new long[teams]; deployed = new DeployRecord[teams];
            for (int i = 0; i < teams; i++) next[i] = startTick + pack.IntervalTicks;
        }
        public int Order => 8;
        public string StateTypeId => "battle.reinforce";
        // NT-1: two teams keep version 1 and its bytes; more teams save TeamCount triples (version 2).
        public int StateVersion => pack.TeamCount == 2 ? 1 : 2;
        public ReadOnlySpan<DeployRecord> Deployed => deployed.AsSpan(0, deployCount);
        public long Attempts(int team) => attempts[team - 1];
        public long Successes(int team) => successes[team - 1];
        public long NextTick(int team) => next[team - 1];
        public void Step(SimContext c)
        {
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
                foreach (var anchor in pack.Bases) if (anchor.Team == team) { layers[count] = anchor.Layer; anchors[count++] = anchor.Point; }
                for (int i = 0; i < pack.Points.Length; i++)
                    if (capture.Points[i].Owner == team && pack.Points[i].HasSpawn) { layers[count] = pack.Points[i].SpawnLayer; anchors[count++] = pack.Points[i].Spawn; }
                if (count == 0) continue;
                int cells = pack.GridColumns * pack.GridRows;
                bool done = false;
                for (int a = 0; a < count && !done; a++)
                    for (int cell = 0; cell < cells; cell++)
                    {
                        int slot = (int)((attempt + cell) % cells);
                        SimVec3 offset = new SimVec3(SimFix.Ratio(2 * (slot % pack.GridColumns) - pack.GridColumns + 1, 2) * pack.GridSpacing,
                            default, SimFix.Ratio(2 * (slot / pack.GridColumns) - pack.GridRows + 1, 2) * pack.GridSpacing);
                        int pick = (int)((attempt + a) % count);
                        SimVec3 point = anchors[pick] + offset;
                        if (!deployer.TryDeploy(reserve, point, layers[pick])) continue;
                        successes[index]++;
                        deployed[deployCount++] = new DeployRecord(c.Clock.Tick, combat.Entities.StableOf(reserve), team, point);
                        done = true; break;
                    }
            }
        }
        public void SaveState(SimStateWriter writer)
        {
            writer.UInt64(pack.PolicyDigest); writer.UInt64(deployer.PolicyKey);
            for (int i = 0; i < pack.TeamCount; i++) { writer.Int64(next[i]); writer.Int64(attempts[i]); writer.Int64(successes[i]); }
        }
        public void LoadState(SimStateReader reader)
        {
            pack.Check(reader); SimStateReader.Require(reader.UInt64() == deployer.PolicyKey, "Deployment configuration differs.");
            for (int i = 0; i < pack.TeamCount; i++) { next[i] = reader.Int64(); attempts[i] = reader.Int64(); successes[i] = reader.Int64(); }
            deployCount = 0;
        }
        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add(deployer.PolicyKey);
            for (int i = 0; i < pack.TeamCount; i++) { hash.Add(next[i]); hash.Add(attempts[i]); hash.Add(successes[i]); }
        }
    }
}
