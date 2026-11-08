using System;
namespace WarCore.Sim
{
    public enum GameplayLifeState
    {
        Active = 0, Downed = 1, Dead = 2
    }
    // One frozen rule per archetype. The threshold belongs to its life resource.
    public readonly struct GameplayLifeRuleDef : IComparable<GameplayLifeRuleDef>
    {
        public readonly ulong ArchetypeKey, ResourceKey, RescueTags, TerminateTags, PreventDownedTags;
        public readonly bool AllowDowned;
        public readonly int DownedTicks;
        public GameplayLifeRuleDef(ulong archetypeKey, ulong resourceKey, bool allowDowned, int downedTicks,
            ulong rescueTags = 0, ulong terminateTags = 0, ulong preventDownedTags = 0)
        {
            ArchetypeKey = archetypeKey;
            ResourceKey = resourceKey;
            AllowDowned = allowDowned;
            DownedTicks = downedTicks;
            RescueTags = rescueTags;
            TerminateTags = terminateTags;
            PreventDownedTags = preventDownedTags;
        }
        public int CompareTo(GameplayLifeRuleDef other) => ArchetypeKey.CompareTo(other.ArchetypeKey);
    }
    public readonly struct GameplayLifeView
    {
        public readonly GameplayLifeState State;
        public readonly long EnteredTick, DownedDueTick;
        internal GameplayLifeView(GameplayLifeState state, long entered, long due)
        {
            State = state;
            EnteredTick = entered;
            DownedDueTick = due;
        }
    }
    public sealed partial class GameplaySchema
    {
        private GameplayLifeRuleDef[] lifeRules = new GameplayLifeRuleDef[0];
        public int NoticeStagingCapacity
        {
            get;
            private set;
        }
        public int LifeRuleCount => lifeRules.Length;
        public GameplayLifeRuleDef LifeRuleAt(int i) => lifeRules[i];
        public int FindLifeRule(ulong archetype)
        {
            int lo = 0, hi = lifeRules.Length - 1;
            while (lo <= hi)
            {
                int m = lo + (hi - lo) / 2;
                ulong key = lifeRules[m].ArchetypeKey;
                if (key == archetype)
                    return m;
                if (key < archetype)
                    lo = m + 1;
                else
                    hi = m - 1;
            }
            return -1;
        }
        private void InitLifeRules(GameplayLifeRuleDef[] definitions)
        {
            if (definitions == null)
                return;
            ValidateArray(definitions);
            lifeRules = (GameplayLifeRuleDef[])definitions.Clone();
            Array.Sort(lifeRules);
            ValidateCapacity(Capacities.InputCommands);
            ValidateCapacity(Capacities.Casts);
            long staging = 2L * Capacities.Commands + Capacities.Statuses + Capacities.Resources + Capacities.Owners + Capacities.InputCommands + Capacities.Casts + 1;
            if (lifeRules.Length != 0 && staging > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_NOTICE_STAGING_CAPACITY");
            NoticeStagingCapacity = lifeRules.Length == 0 ? 0 : (int)staging;
            for (int i = 0; i < lifeRules.Length; i++)
            {
                var rule = lifeRules[i];
                int a = FindArchetype(rule.ArchetypeKey), r = FindResource(rule.ResourceKey);
                bool bound = false;
                if (a >= 0)
                    for (int j = 0; j < archetypes[a].ResourceCount; j++)
                        bound |= archetypes[a].ResourceKeyAt(j) == rule.ResourceKey;
                if (i > 0 && lifeRules[i - 1].ArchetypeKey == rule.ArchetypeKey || !bound || r < 0 || !resources[r].IsLife
                    || (rule.AllowDowned ? rule.DownedTicks < 1 : rule.DownedTicks != 0))
                    throw new SimBoundaryException("M0C_LIFE_RULE");
            }
        }
        private void HashLifeRules(StateHash h)
        {
            h.Add(lifeRules.Length);
            foreach (var r in lifeRules)
            {
                h.Add(r.ArchetypeKey);
                h.Add(r.ResourceKey);
                h.Add(r.AllowDowned);
                h.Add(r.DownedTicks);
                h.Add(r.RescueTags);
                h.Add(r.TerminateTags);
                h.Add(r.PreventDownedTags);
            }
            h.Add(archetypes.Length);
            foreach (var a in archetypes)
            {
                h.Add(a.Key);
                h.Add(a.Tags);
            }
            h.Add(abilities.Length);
            foreach (var a in abilities)
            {
                h.Add(a.Key);
                h.Add(a.AllowWhileDowned);
            }
        }
    }
    public sealed partial class Sim
    {
        public GameplayLifeView GameplayLife(StableId owner)
        {
            if (gameplay == null)
                throw new SimBoundaryException("M0C_GAMEPLAY_REQUIRED");
            return gameplay.Life(owner);
        }
    }
}
