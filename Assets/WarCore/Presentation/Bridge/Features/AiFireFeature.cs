using System;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The AI reaction time, sense interval and burst: on with AI rules whose content sets ai.reactionTicks above 0,
    // ai.senseTicks above 1 or ai.burstShots above 0. It wraps the combat system's built-in fire decision and registers its state system
    // (battle.ai-fire, Order 14). It runs in RegisterRules, so it replaces a decision an extension installed in an earlier
    // hook, and an extension's RegisterRules runs after it.
    internal sealed class AiFireFeature : WorldStageFeature
    {
        internal static readonly AiFireFeature Instance = new AiFireFeature();

        public override string OptionId => "world.ai-fire";

        public override bool IsActive(WorldAssembly a)
        {
            AiRules ai = (a.Terrain != null ? a.Recipe.TerrainPack : a.Recipe.AiPack)?.Section<AiRules>();
            if (ai == null) return false;
            return ai.ReactionTicks > 0 || ai.SenseTicks > 1 || ai.BurstShots > 0;
        }

        public override void RegisterRules(WorldAssembly a)
        {
            if (a.Combat == null) throw new ArgumentException("The AI reaction time needs combat.");
            AiRules ai = (a.Terrain != null ? a.Recipe.TerrainPack : a.Recipe.AiPack).Section<AiRules>();
            var fire = new ReactionFire(a.Combat, a.Combat.BuiltInAutoFire, ai.ReactionTicks, ai.SenseTicks, ai.BurstShots, ai.BurstPauseTicks);
            a.Combat.UseAutoFire(fire);
            a.Sim.RegisterGameplay("battle.ai-fire", fire, SimSystemPhase.Requests);
        }
    }
}
