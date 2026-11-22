using System;
using WarCore.Battle;
using WarCore.Combat;

namespace WarCore.Presentation
{
    // NERVE-1: on when the recipe's combat pack has a combat.status pinned or broken row, so every other world keeps its exact
    // components, systems, snapshots and hashes. It registers nothing: in RegisterRules (after the match installer made the
    // pressure system) it binds the pressure system as the combat system's nerve source, unless a source was bound already
    // (a script in Connect). A world with such a row but no pressure system (no match, or AI rules without every pressure*
    // field) is refused: the check is here, not in the content loader, because the AI rules may come from another pack.
    internal sealed class NerveFeature : WorldStageFeature
    {
        internal static readonly NerveFeature Instance = new NerveFeature();

        public override string OptionId => "world.nerve";

        public override bool IsActive(WorldAssembly a) => a.Recipe.Combat?.Status != null && a.Recipe.Combat.Status.HasNerve;

        public override void RegisterRules(WorldAssembly a)
        {
            if (a.Combat.HasNerveSource) return;
            if (a.Pressure == null)
                throw new InvalidOperationException("combat.status pinned or broken needs the pressure system: a match and AI rules with every pressure* field.");
            a.Combat.BindNerve(new PressureNerve(a.Pressure));
        }

        // The pressure system's level as 0, 1, 2, value by value (never a cast of the enum).
        internal sealed class PressureNerve : INerveSource
        {
            private readonly Pressure pressure;
            internal PressureNerve(Pressure pressure) { this.pressure = pressure; }

            public int NerveLevel(WarCore.Sim.StableId unit)
            {
                switch (pressure.Level(unit))
                {
                    case AiNerve.Calm: return CombatSystem.NerveCalm;
                    case AiNerve.Pinned: return CombatSystem.NervePinned;
                    case AiNerve.Broken: return CombatSystem.NerveBroken;
                    default: throw new InvalidOperationException("Unknown pressure level.");
                }
            }
        }
    }
}
