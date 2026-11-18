using System;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Placement "scatter": each unit wants a spot of its own near the point's centre, so a circle fills in no particular
    // order instead of from the side the units arrive on. The spot depends only on the unit key, the point index, the
    // point's centre and scatterMm, so it is the same on every tick and after a restore.
    public sealed class ScatterPlacement : IAiPlacement
    {
        private const ulong Salt = 0x6a09e667bb67ae85UL;
        private readonly int scatterMm;
        // scatterMm: half the side of the square, centred on the point, that the spots fall in (millimetres, at least 1).
        public ScatterPlacement(int scatterMm)
        {
            if (scatterMm < 1) throw new ArgumentOutOfRangeException(nameof(scatterMm));
            this.scatterMm = scatterMm;
        }

        public ulong Fold(ulong digest)
        {
            var hash = new StateHash(); hash.Add("battle.ai.scatter.v1"); hash.Add(digest); hash.Add(scatterMm);
            return hash.Value;
        }

        // m = Mix(key ^ Salt ^ point). u = (m & 0xFFFFFFFF) % (2s + 1) - s and v = (m >> 32) % (2s + 1) - s, in whole
        // millimetres (s = scatterMm), so each offset is an integer in [-s, +s]; the low 32 bits give x, the high 32 bits z.
        // Millimetres become raw units by SimFix.Ratio(mm, 1000), truncated toward zero.
        public SimVec3 Anchor(in AiUnit unit, int point, AiField field)
        {
            ulong m = AiParts.Mix(unit.Id.Value ^ Salt ^ (ulong)point);
            ulong span = 2UL * (ulong)scatterMm + 1UL;
            long u = (long)((m & 0xFFFFFFFFUL) % span) - scatterMm;
            long v = (long)((m >> 32) % span) - scatterMm;
            SimVec3 center = field.Center(point);
            return new SimVec3(center.X + SimFix.Ratio(u, 1000), center.Y, center.Z + SimFix.Ratio(v, 1000));
        }
    }
}
