using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // DOWN-1: when a unit goes down instead of dying. Lethal: a hit that would kill a unit leaves it at 1 raw and down instead.
    // Gear: no hit downs anyone by itself; only a later rule (armour) calls CombatSystem's Down, and the "already down" rule
    // below holds for such a unit exactly as for Lethal.
    public enum DownedWhen : byte
    {
        Lethal = 1,
        Gear = 2
    }

    // DOWN-1 combat.downed. Ticks: how long a downed unit lies before it bleeds out (1..108000). StanceLevel: the stance a downed
    // unit is forced into (a level of the pack's stances, 1..; 0 = no change). CrawlPermille: the speed gear of a downed unit
    // (1..1000). Finish: whether a downed unit can be hit to death once FinishAfterTicks (0..Ticks) have passed since it went
    // down; inside those ticks every hit on it is zero. StunTicks: the length of a short knock-out a later rule may give
    // (0..600; read, checked and hashed only). Targetable: false makes the AI target pickers skip casualties.
    public sealed class DownedRules
    {
        public const int MaxTicks = 108000, MaxCrawlPermille = 1000, MaxStunTicks = 600;

        public DownedWhen When { get; }
        public int Ticks { get; }
        public int StanceLevel { get; }
        public int CrawlPermille { get; }
        public bool Finish { get; }
        public int FinishAfterTicks { get; }
        public int StunTicks { get; }
        public bool Targetable { get; }

        public DownedRules(DownedWhen when, int ticks, int crawlPermille, int stanceLevel = 0, bool finish = true, int finishAfterTicks = 0,
            int stunTicks = 0, bool targetable = true)
        {
            if (when != DownedWhen.Lethal && when != DownedWhen.Gear) throw new ArgumentOutOfRangeException(nameof(when));
            if (ticks < 1 || ticks > MaxTicks) throw new ArgumentOutOfRangeException(nameof(ticks));
            if (crawlPermille < 1 || crawlPermille > MaxCrawlPermille) throw new ArgumentOutOfRangeException(nameof(crawlPermille));
            if (stanceLevel < 0 || stanceLevel > CombatPack.MaxStances) throw new ArgumentOutOfRangeException(nameof(stanceLevel));
            if (finishAfterTicks < 0 || finishAfterTicks > ticks) throw new ArgumentOutOfRangeException(nameof(finishAfterTicks));
            if (stunTicks < 0 || stunTicks > MaxStunTicks) throw new ArgumentOutOfRangeException(nameof(stunTicks));
            When = when; Ticks = ticks; CrawlPermille = crawlPermille; StanceLevel = stanceLevel; Finish = finish;
            FinishAfterTicks = finishAfterTicks; StunTicks = stunTicks; Targetable = targetable;
        }

        internal void WriteState(StateHash hash)
        {
            hash.Add("combat.pack.downed.v1");
            hash.Add((int)When); hash.Add(Ticks); hash.Add(StanceLevel); hash.Add(CrawlPermille);
            hash.Add(Finish); hash.Add(FinishAfterTicks); hash.Add(StunTicks); hash.Add(Targetable);
        }
    }
}
