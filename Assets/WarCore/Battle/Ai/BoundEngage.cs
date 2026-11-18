using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Engage "bound": move a stretch, fire a stretch. A unit advances at a think when all three hold:
    // - it is an advancer: Permille(key, ShareSalt) < sharePermille (the others always hold);
    // - its cycle is in the move window: (tick + Mix(key ^ PhaseSalt) % cycle) % cycle < moveTicks, cycle = move + fire;
    // - the enemy is at least minRangeMm away horizontally.
    // Stateless: the phase comes from the key, so every unit has its own and a restore changes nothing.
    public sealed class BoundEngage : IAiEngage
    {
        private const ulong ShareSalt = 0x3c6ef372fe94f82bUL, PhaseSalt = 0xa54ff53a5f1d36f1UL;
        private readonly int moveTicks, fireTicks, sharePermille, minRangeMm;
        private readonly long cycle;
        private readonly SimWide minRangeSquared;
        public BoundEngage(int moveTicks, int fireTicks, int sharePermille, int minRangeMm)
        {
            if (moveTicks < 1) throw new ArgumentOutOfRangeException(nameof(moveTicks));
            if (fireTicks < 1) throw new ArgumentOutOfRangeException(nameof(fireTicks));
            if (sharePermille < 0 || sharePermille > 1000) throw new ArgumentOutOfRangeException(nameof(sharePermille));
            if (minRangeMm < 0) throw new ArgumentOutOfRangeException(nameof(minRangeMm));
            this.moveTicks = moveTicks; this.fireTicks = fireTicks; this.sharePermille = sharePermille; this.minRangeMm = minRangeMm;
            cycle = (long)moveTicks + fireTicks;
            minRangeSquared = new SimVec3(SimFix.Ratio(minRangeMm, 1000), default, default).LengthSquared;
        }
        public int MoveTicks => moveTicks;
        public int FireTicks => fireTicks;

        public ulong Fold(ulong digest)
        {
            var hash = new StateHash(); hash.Add("battle.ai.bound.v1"); hash.Add(digest);
            hash.Add(moveTicks); hash.Add(fireTicks); hash.Add(sharePermille); hash.Add(minRangeMm);
            return hash.Value;
        }

        public AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field)
        {
            ulong key = unit.Id.Value;
            if (AiParts.Permille(key, ShareSalt) >= sharePermille) return AiEngage.Hold;
            long phase = (long)(AiParts.Mix(key ^ PhaseSalt) % (ulong)cycle);
            if ((field.Tick + phase) % cycle >= moveTicks) return AiEngage.Hold;
            if (CombatSystem.Horizontal(enemy - unit.Here).LengthSquared < minRangeSquared) return AiEngage.Hold;
            return AiEngage.Advance;
        }
    }
}
