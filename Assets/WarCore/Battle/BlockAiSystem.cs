using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Where a unit faces, as a horizontal direction. The bridge answers it from the facing component.
    public interface IUnitFacings
    {
        bool TryFacing(StableId unit, out SimVec3 direction);
    }

    // C4: an AI-driven unit that can block raises its block against a swing that is about to land on it, with the chance its block spec
    // gives (BlockSpec.AiRaisePermille), and lowers it when no swing is coming. The shield goes up toward where the unit faces and is not
    // turned toward the attacker, so a blow from the side or behind lands. The chance is rolled once per swing from a fixed mix of the
    // attacker, the unit and the swing's start tick: the answer stays the same on every tick of the swing, the same run gives the same
    // answers, and a higher chance only ever adds swings to the ones a lower chance blocks. The controlled unit is never touched.
    public sealed class BlockAiSystem : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly IUnitFacings facings;
        private readonly IControlTarget controlled;
        private readonly EntityStage stage;
        private readonly ComponentType<Combatant> combatants;

        public BlockAiSystem(CombatSystem combat, IUnitFacings facings, IControlTarget controlled = null)
        {
            this.combat = combat; this.facings = facings; this.controlled = controlled;
            stage = new EntityStage(combat.Entities);
            combatants = combat.Entities.Resolve<Combatant>();
        }

        public int Order => 11;
        public string StateTypeId => "battle.block-ai";
        public int StateVersion => 1;

        // True when the swing started at `startTick` by `attacker` on `victim` is one a unit with this chance blocks.
        public static bool Raises(StableId attacker, StableId victim, long startTick, int permille)
        {
            unchecked
            {
                ulong z = attacker.Value * 0x9E3779B97F4A7C15UL + victim.Value;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL + (ulong)startTick;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (int)(z % 1000UL) < permille;
            }
        }

        public void Step(SimContext c)
        {
            foreach (EntityId unit in stage.Capture())
            {
                if (controlled != null && unit.Equals(controlled.Current)) continue;
                if (!combat.Entities.TryGet(combatants, unit, out Combatant _) || combat.IsDead(unit) || !combat.CanBlock(unit)) continue;
                int permille = combat.BlockSpecOf(unit).AiRaisePermille;
                if (permille == 0) continue;
                StableId self = combat.Entities.StableOf(unit);
                bool raise = combat.TryMeleeThreat(unit, out StableId attacker, out long startTick) && Raises(attacker, self, startTick, permille);
                if (!raise) { if (combat.IsHolding(unit)) combat.Release(unit); continue; }
                if (!combat.IsHolding(unit) && facings.TryFacing(self, out SimVec3 direction) && direction.LengthSquared.Sign != 0)
                    combat.Hold(unit, direction);
            }
        }

        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }
}
