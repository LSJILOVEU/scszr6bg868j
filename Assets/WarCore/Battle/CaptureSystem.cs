using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    public readonly struct PointState
    {
        public readonly int Owner, Capturing, Progress;
        public PointState(int owner, int capturing, int progress) { Owner = owner; Capturing = capturing; Progress = progress; }
    }
    public readonly struct CaptureRecord
    {
        public readonly long Tick;
        public readonly int PointId, OldOwner, NewOwner;
        public CaptureRecord(long tick, int pointId, int oldOwner, int newOwner)
        { Tick = tick; PointId = pointId; OldOwner = oldOwner; NewOwner = newOwner; }
    }
    public sealed class CaptureSystem : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly MatchPack pack;
        private readonly PointState[] states;
        private readonly int[] counts;
        private readonly CaptureRecord[] changes;
        private int changeCount;
        public CaptureSystem(CombatSystem combat, MatchPack pack)
        {
            this.combat = combat; this.pack = pack; stage = new EntityStage(combat.Entities);
            positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            states = new PointState[pack.Points.Length]; counts = new int[states.Length * 2]; changes = new CaptureRecord[states.Length];
            for (int i = 0; i < states.Length; i++) states[i] = new PointState(pack.Points[i].InitialOwner, 0, 0);
        }
        public int Order => 40;
        public string StateTypeId => "battle.capture";
        public int StateVersion => 1;
        public ReadOnlySpan<PointState> Points => states;
        public ReadOnlySpan<CaptureRecord> Changes => changes.AsSpan(0, changeCount);
        public static bool Inside(SimVec3 point, in ControlPointDef def) =>
            CombatSystem.Horizontal(point - def.Center).LengthSquared <= new SimVec3(def.Radius, default, default).LengthSquared;
        public void Step(SimContext c)
        {
            changeCount = 0; Array.Clear(counts, 0, counts.Length);
            foreach (EntityId unit in stage.Capture())
            {
                if (!combat.Entities.TryGet(combatants, unit, out Combatant value) || value.Team < 1 || value.Team > 2
                    || !combat.Entities.TryGet(positions, unit, out Position position) || combat.IsDead(unit)) continue;
                for (int i = 0; i < states.Length; i++) if (Inside(position.Value, pack.Points[i])) counts[2 * i + value.Team - 1]++;
            }
            for (int i = 0; i < states.Length; i++)
            {
                int one = counts[2 * i], two = counts[2 * i + 1], leader = one > two ? 1 : two > one ? 2 : 0;
                long a = Math.Max(one, two), b = Math.Min(one, two);
                bool advantage = leader != 0 && (pack.Strict ? a * pack.Den > b * pack.Num : a * pack.Den >= b * pack.Num);
                PointState old = states[i];
                if (advantage && leader != old.Owner)
                {
                    int progress = (old.Capturing == leader ? old.Progress : 0) + 1;
                    if (progress >= pack.CaptureTicks)
                    {
                        states[i] = new PointState(leader, 0, 0);
                        changes[changeCount++] = new CaptureRecord(c.Clock.Tick, pack.Points[i].Id, old.Owner, leader);
                    }
                    else states[i] = new PointState(old.Owner, leader, progress);
                }
                else if (pack.Interrupt != InterruptMode.Pause)
                {
                    int progress = pack.Interrupt == InterruptMode.Decay ? Math.Max(0, old.Progress - pack.DecayPerTick) : 0;
                    states[i] = new PointState(old.Owner, progress == 0 ? 0 : old.Capturing, progress);
                }
            }
        }
        public void SaveState(SimStateWriter writer)
        { writer.UInt64(pack.PolicyDigest); foreach (var s in states) { writer.Int32(s.Owner); writer.Int32(s.Capturing); writer.Int32(s.Progress); } }
        public void LoadState(SimStateReader reader)
        { pack.Check(reader); for (int i = 0; i < states.Length; i++) states[i] = new PointState(reader.Int32(), reader.Int32(), reader.Int32()); changeCount = 0; }
        public void WriteState(StateHash hash)
        { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); foreach (var s in states) { hash.Add(s.Owner); hash.Add(s.Capturing); hash.Add(s.Progress); } }
    }
}
