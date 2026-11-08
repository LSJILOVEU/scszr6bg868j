using System;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The AI reaction time: on in a terrain world whose content sets ai.reactionTicks above 0. It wraps the combat system's
    // built-in fire decision and registers its state system (battle.ai-fire, Order 14). It runs in RegisterRules, so it
    // replaces a decision an extension installed in an earlier hook, and an extension's RegisterRules runs after it.
    internal sealed class AiFireFeature : WorldStageFeature
    {
        internal static readonly AiFireFeature Instance = new AiFireFeature();

        public override string OptionId => "world.ai-fire";

        public override bool IsActive(WorldAssembly a) =>
            a.Terrain != null && a.Recipe.TerrainPack != null && a.Recipe.TerrainPack.Section<AiRules>().ReactionTicks > 0;

        public override void RegisterRules(WorldAssembly a)
        {
            if (a.Combat == null) throw new ArgumentException("The AI reaction time needs combat.");
            var fire = new ReactionFire(a.Combat, a.Combat.BuiltInAutoFire, a.Recipe.TerrainPack.Section<AiRules>().ReactionTicks);
            a.Combat.UseAutoFire(fire);
            a.Sim.RegisterGameplay("battle.ai-fire", fire, SimSystemPhase.Requests);
        }
    }
}
