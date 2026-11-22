using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // One move-spread table (AIM-1): 1 to 8 bands, each "a step faster than AboveMmPerSec multiplies the shot's spread by
    // Permille / 1000". The band a step falls in is the last one whose speed it exceeds strictly; none: 1000 (no change).
    // Speeds are compared without division: each band keeps the per-tick distance floor(above * 65536 / 30000) raw and a
    // step of (dx, dz) raw exceeds it when dx^2 + dz^2 > threshold^2.
    // Widths: a component above 2^24 raw (256 m, a respawn or a teleport) takes the top band without squaring; otherwise
    // dx^2 + dz^2 <= 2^49, and the largest threshold is 300000 * 65536 / 30000 = 655360 raw, whose square is < 2^40.
    public sealed class MoveSpreadTable
    {
        public const int MaxBands = 8, MaxAboveMmPerSec = 300000;
        public const long FarRaw = 1L << 24;
        private readonly int[] above, permille;
        private readonly long[] threshold;

        public MoveSpreadTable(int[] aboveMmPerSec, int[] permille)
        {
            if (aboveMmPerSec == null) throw new ArgumentNullException(nameof(aboveMmPerSec));
            if (permille == null) throw new ArgumentNullException(nameof(permille));
            if (aboveMmPerSec.Length != permille.Length) throw new ArgumentException("One permille per band is required.", nameof(permille));
            if (aboveMmPerSec.Length < 1 || aboveMmPerSec.Length > MaxBands) throw new ArgumentOutOfRangeException(nameof(aboveMmPerSec));
            above = (int[])aboveMmPerSec.Clone();
            this.permille = (int[])permille.Clone();
            threshold = new long[above.Length];
            for (int i = 0; i < above.Length; i++)
            {
                if (above[i] < 0 || above[i] > MaxAboveMmPerSec || (i > 0 && above[i] <= above[i - 1]))
                    throw new ArgumentOutOfRangeException(nameof(aboveMmPerSec));
                if (this.permille[i] < StatLine.MinPermille || this.permille[i] > StatLine.MaxPermille)
                    throw new ArgumentOutOfRangeException(nameof(permille));
                threshold[i] = above[i] * SimFix.Scale / (SimClock.TickRate * 1000L);
            }
        }

        public int Count => above.Length;
        public int AboveMmPerSec(int band) => above[band];
        public int Permille(int band) => permille[band];
        // The band's distance per tick in raw units.
        public long ThresholdRaw(int band) => threshold[band];

        // The factor of a step that moved dx, dz raw on the horizontal plane.
        public int PermilleFor(long dx, long dz)
        {
            if (dx > FarRaw || dx < -FarRaw || dz > FarRaw || dz < -FarRaw) return permille[permille.Length - 1];
            long squared = dx * dx + dz * dz;
            int result = 1000;
            for (int i = 0; i < threshold.Length && squared > threshold[i] * threshold[i]; i++) result = permille[i];
            return result;
        }

        internal void WriteState(StateHash hash)
        {
            hash.Add(above.Length);
            for (int i = 0; i < above.Length; i++) { hash.Add(above[i]); hash.Add(permille[i]); }
        }
    }

    // The pack's combat.moveSpread: the default table and, per weapon, an optional table that replaces it whole.
    public sealed class MoveSpread
    {
        private readonly MoveSpreadTable[] byWeapon;

        // byWeapon: null, or one entry per weapon of the pack (null = that weapon uses the default table).
        public MoveSpread(MoveSpreadTable defaultTable, MoveSpreadTable[] byWeapon = null)
        {
            Default = defaultTable ?? throw new ArgumentNullException(nameof(defaultTable));
            this.byWeapon = byWeapon == null ? Array.Empty<MoveSpreadTable>() : (MoveSpreadTable[])byWeapon.Clone();
        }

        public MoveSpreadTable Default { get; }
        // 0 when no weapon has a table of its own, else the pack's weapon count.
        public int WeaponSlots => byWeapon.Length;
        public MoveSpreadTable For(int weapon) => weapon >= 0 && weapon < byWeapon.Length && byWeapon[weapon] != null ? byWeapon[weapon] : Default;

        // The default table, then the weapons with a table of their own: their count, then index and table of each, by index.
        internal void WriteState(StateHash hash)
        {
            Default.WriteState(hash);
            int own = 0;
            foreach (MoveSpreadTable table in byWeapon) if (table != null) own++;
            hash.Add(own);
            for (int i = 0; i < byWeapon.Length; i++)
                if (byWeapon[i] != null) { hash.Add(i); byWeapon[i].WriteState(hash); }
        }
    }
}
