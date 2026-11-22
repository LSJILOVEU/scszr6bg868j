using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // DOWN-1: downed and stunned units. On only when the recipe's combat pack keeps casualties (combat.downed, or a blast that
    // stuns), so every other world keeps its exact components, systems, snapshots and hashes. It registers the casualty
    // component; right after the stances it binds it to combat, registers system 0 (the forced downed stance, when the pack
    // names one) and system 33 (bleed-outs, end of stuns) and adds the casualty row to the action gate; last of all it adds the
    // crawl gear to the speed line (a pack with combat.downed), holds stunned units in place and binds the gate to combat.
    internal sealed class DownedFeature : WorldStageFeature
    {
        internal static readonly DownedFeature Instance = new DownedFeature();

        public override string OptionId => "world.downed";

        public override bool IsActive(WorldAssembly a) => a.Recipe.Combat != null && a.Recipe.Combat.UsesCasualties;

        public override void RegisterComponents(WorldAssembly a) => a.Casualties = Casualty.Register(a.Sim.Entities);

        // Runs after StanceFeature's Connect, which makes the gate when the pack has stances; without stances the gate is made here.
        public override void Connect(WorldAssembly a)
        {
            CombatSystem combat = a.Combat;
            combat.BindCasualties(a.Casualties);
            if (combat.Pack.Downed != null && combat.Pack.Downed.StanceLevel > 0)
                a.Sim.RegisterGameplay("combat.casualty-pose", new CasualtyPoseSystem(a.Sim, combat, a.Stances), SimSystemPhase.Requests);
            a.Sim.RegisterGameplay("combat.casualty", new CasualtySystem(a.Sim, combat), SimSystemPhase.Queries);
            a.Gate = a.Gate ?? new ActionGate();
            a.Gate.Add(new CasualtyLimit(combat));
        }

        // Last in the Rules table: the speed line already wraps the motion model when the pack has stances; otherwise it is made
        // here the same way, around the motion model bound now, and given to the locomotion and the player's step.
        public override void RegisterRules(WorldAssembly a)
        {
            CombatSystem combat = a.Combat;
            if (combat.Pack.Downed != null)
            {
                if (!(a.Locomotion.Motion is SpeedLine line))
                {
                    line = new SpeedLine(a.Locomotion.Motion);
                    a.Locomotion.BindMotion(line);
                    a.PlayerStep?.BindSpeed(line);
                }
                line.AddGear(new CrawlGear(combat));
            }
            a.Locomotion.BindHold(new StunHold(combat));
            combat.UseActionGate(a.Gate);
        }

        // A downed unit crawls at the pack's crawl permille; everyone else 1000.
        private sealed class CrawlGear : ISpeedGear
        {
            private readonly CombatSystem combat;
            private readonly int crawl;
            internal CrawlGear(CombatSystem combat) { this.combat = combat; crawl = combat.Pack.Downed.CrawlPermille; }
            public int Permille(EntityId unit) => combat.IsDowned(unit) ? crawl : 1000;
        }

        // A stunned unit does not walk by itself.
        private sealed class StunHold : IUnitHold
        {
            private readonly CombatSystem combat;
            internal StunHold(CombatSystem combat) { this.combat = combat; }
            public bool Holds(EntityId unit) => combat.IsStunned(unit);
        }
    }
}
