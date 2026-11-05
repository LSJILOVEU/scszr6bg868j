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
        private readonly long[] next = new long[2], attempts = new long[2], successes = new long[2];
        private readonly SimVec3[] anchors;
        private readonly DeployRecord[] deployed = new DeployRecord[2];
        private int deployCount;
        public ReinforceSystem(CombatSystem combat, MatchPack pack, CaptureSystem capture, IUnitDeployer deployer, long startTick = 0)
        {
            this.combat = combat; this.pack = pack; this.capture = capture; this.deployer = deployer;
            stage = new EntityStage(combat.Entities); positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            anchors = new SimVec3[pack.Bases.Length + pack.Points.Length];
            next[0] = next[1] = startTick + pack.IntervalTicks;
        }
        public int Order => 8;
        public string StateTypeId => "battle.reinforce";
        public int StateVersion => 1;
        public ReadOnlySpan<DeployRecord> Deployed => deployed.AsSpan(0, deployCount);
        public long Attempts(int team) => attempts[team - 1];
        public long Successes(int team) => successes[team - 1];
        public long NextTick(int team) => next[team - 1];
        public void Step(SimContext c)
        {
            deployCount = 0;
            for (int team = 1; team <= 2; team++)
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
                foreach (var anchor in pack.Bases) if (anchor.Team == team) anchors[count++] = anchor.Point;
                for (int i = 0; i < pack.Points.Length; i++)
                    if (capture.Points[i].Owner == team && pack.Points[i].HasSpawn) anchors[count++] = pack.Points[i].Spawn;
                if (count == 0) continue;
                int cells = pack.GridColumns * pack.GridRows;
                bool done = false;
                for (int a = 0; a < count && !done; a++)
                    for (int cell = 0; cell < cells; cell++)
                    {
                        int slot = (int)((attempt + cell) % cells);
                        SimVec3 offset = new SimVec3(SimFix.Ratio(2 * (slot % pack.GridColumns) - pack.GridColumns + 1, 2) * pack.GridSpacing,
                            default, SimFix.Ratio(2 * (slot / pack.GridColumns) - pack.GridRows + 1, 2) * pack.GridSpacing);
                        SimVec3 point = anchors[(int)((attempt + a) % count)] + offset;
                        if (!deployer.TryDeploy(reserve, point)) continue;
                        successes[index]++;
                        deployed[deployCount++] = new DeployRecord(c.Clock.Tick, combat.Entities.StableOf(reserve), team, point);
                        done = true; break;
                    }
            }
        }
        public void SaveState(SimStateWriter writer)
        {
            writer.UInt64(pack.PolicyDigest); writer.UInt64(deployer.PolicyKey);
            for (int i = 0; i < 2; i++) { writer.Int64(next[i]); writer.Int64(attempts[i]); writer.Int64(successes[i]); }
        }
        public void LoadState(SimStateReader reader)
        {
            pack.Check(reader); SimStateReader.Require(reader.UInt64() == deployer.PolicyKey, "Deployment configuration differs.");
            for (int i = 0; i < 2; i++) { next[i] = reader.Int64(); attempts[i] = reader.Int64(); successes[i] = reader.Int64(); }
            deployCount = 0;
        }
        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add(deployer.PolicyKey);
            for (int i = 0; i < 2; i++) { hash.Add(next[i]); hash.Add(attempts[i]); hash.Add(successes[i]); }
        }
    }
}
