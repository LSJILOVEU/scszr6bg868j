using System;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // The crowd pass rule the locomotion asks before a moving unit passes an idle one. On when the recipe names a rule; made
    // after combat, which it reads, and before the movement systems, which take it.
    internal sealed class CrowdPassFeature : WorldStageFeature
    {
        internal static readonly CrowdPassFeature Instance = new CrowdPassFeature();

        public override string OptionId => "world.crowd-pass";

        public override bool IsActive(WorldAssembly a) => a.Recipe.CrowdPass != CrowdPassKind.None;

        public override void Connect(WorldAssembly a)
        {
            WorldRecipe recipe = a.Recipe;
            if (!a.Crowd || recipe.Combat == null)
                throw new ArgumentException("A crowd pass rule requires crowd movement and combat.", nameof(recipe));
            a.CrowdPass = recipe.CrowdPass == CrowdPassKind.TeamIdle ? new TeamIdleCrowdPass(a.Sim, a.Combat, recipe.Combat, a.Controlled) : null;
        }
    }

    /// <summary>Default crowd pass rule: a moving unit may pass a stationary unit of the same team (team 1 or higher).
    /// Never involves the controlled unit, and never involves a unit that is engaged: one that fired or swung within
    /// the last EngagedTicks (or whose next shot or swing is still pending) keeps blocking and is blocked,
    /// so a friend cannot creep onto a muzzle between shots.</summary>
    internal sealed class TeamIdleCrowdPass : ICrowdPass
    {
        // Algorithm identity ("TEAMIDL1"); no static configuration.
        internal const ulong RuleDigest = 0x5445414D49444C31UL;
        // PLACEHOLDER: how long after its last shot or swing a unit still counts as engaged (about two seconds).
        internal const long EngagedTicks = 60;
        private readonly Simulation sim;
        private readonly EntityStore entities;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<MeleeState> melee;
        private readonly bool hasMelee;
        private readonly IControlTarget controlled;

        // RC2-1: in a battle with a led squad (the match feature hands it over once it is made) the squad's leader passes the
        // members of its squad as the mover, whether they stand, walk or fight. RC2-6a: a member passes its leader while the
        // leader stands, as two team mates pass each other (neither of them engaged); nobody else passes the controlled unit,
        // and nobody passes it while it walks. Everything else is the rule above, bit for bit, so a battle in which no squad
        // is ever called moves as under the plain rule. ("TEAMIDL3"; RC2-1's rule was "TEAMIDL2".)
        internal const ulong LeaderDigest = 0x5445414D49444C33UL;
        internal WarCore.Battle.LedSquad Led { get; set; }

        internal TeamIdleCrowdPass(Simulation sim, CombatSystem combat, CombatPack pack, IControlTarget controlled)
        {
            this.sim = sim; entities = combat.Entities; this.controlled = controlled;
            combatants = entities.Resolve<Combatant>();
            hasMelee = pack.HasMelee;
            if (hasMelee) melee = entities.Resolve<MeleeState>();
        }

        public ulong Digest => Led != null ? LeaderDigest : RuleDigest;

        public bool MayPass(EntityId mover, EntityId idle)
        {
            if (controlled != null && mover.Equals(controlled.Current)) return LeaderPasses(mover, idle);
            // The standing controlled unit: only a member of the squad it leads goes on to the rule of two team mates.
            if (controlled != null && idle.Equals(controlled.Current) && !LeaderPasses(idle, mover)) return false;
            if (!entities.TryGet(combatants, mover, out Combatant a) || !entities.TryGet(combatants, idle, out Combatant b)) return false;
            if (a.Team < 1 || a.Team != b.Team) return false;
            long tick = sim.Clock.Tick;
            return !Busy(mover, a, tick) && !Busy(idle, b, tick);
        }

        public bool MayPassMoving(EntityId mover, EntityId other) => LeaderPasses(mover, other);

        private bool LeaderPasses(EntityId mover, EntityId other) =>
            Led != null && entities.StableOf(mover).Value == Led.Leader && Led.IsMember(entities.StableOf(other));

        private bool Busy(EntityId unit, Combatant state, long tick)
        {
            // DueMilli is zero until the first shot, so a unit that never fired is never engaged.
            if (state.DueMilli > 0 && state.DueMilli > (tick - EngagedTicks) * 1000) return true;
            return hasMelee && entities.TryGet(melee, unit, out MeleeState swing)
                && (swing.Pending || swing.NextStartTick > 0 && tick < swing.NextStartTick + EngagedTicks);
        }
    }
}
