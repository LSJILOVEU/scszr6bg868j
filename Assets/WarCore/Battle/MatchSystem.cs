using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Gathers the read-only facts and asks the mode to judge. It owns no result state: Winner, EndTick and EndReason
    // are read-only projections of the mode.
    public sealed class MatchSystem : IRetainedGameplaySystem, IMatchReader
    {
        private readonly CombatSystem combat;
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        private readonly MatchMode mode;
        private readonly EntityStage stage;
        private readonly ComponentType<Combatant> combatants;
        public int Winner => mode.Winner;
        public long EndTick => mode.EndTick;
        public MatchEndReason EndReason => mode.EndReason;
        public MatchSystem(CombatSystem combat, MatchPack pack, CaptureSystem capture, MatchMode mode)
        {
            this.combat = combat; this.pack = pack; this.capture = capture; this.mode = mode;
            stage = new EntityStage(combat.Entities); combatants = combat.Entities.Resolve<Combatant>();
        }
        public int Order => 50;
        public string StateTypeId => "battle.match";
        public int StateVersion => 2;
        public int PointCount => capture.Points.Length;
        public void CountAlive(out int teamOne, out int teamTwo)
        {
            teamOne = 0; teamTwo = 0;
            foreach (EntityId entity in stage.Capture())
                if (combat.Entities.TryGet(combatants, entity, out Combatant unit) && !combat.IsDead(entity))
                { if (unit.Team == 1) teamOne++; else if (unit.Team == 2) teamTwo++; }
        }
        public int PointsOwnedBy(int team)
        {
            int count = 0;
            foreach (var point in capture.Points) if (point.Owner == team) count++;
            return count;
        }
        public void Step(SimContext c) => mode.Evaluate(c.Clock.Tick);
        public void SaveState(SimStateWriter writer) => writer.UInt64(pack.PolicyDigest);
        public void LoadState(SimStateReader reader) => pack.Check(reader);
        public void WriteState(StateHash hash)
        { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); }
    }
}
