using System;

namespace WarCore.Sim
{
    // SLOPE-1: how much a slope slows a step. Two tables of bands, uphill and downhill, each strictly increasing in
    // abovePermille (0..2000, the steepest legal height-field slope is 2/1) with a speed factor of 1..1000 (a slope only
    // slows, so a step stays at most 0.5 m). The band taken is the last one whose abovePermille the slope exceeds; no band
    // matched, or no table for that direction, is 1000. Immutable; it lives in Sim because content builds it and movement
    // (Interim) reads it, and later route costs read the same numbers.
    public sealed class SlopeTable
    {
        public const int MaxBands = 8, MaxAbovePermille = 2000, MinSpeedPermille = 1, MaxSpeedPermille = 1000;
        // The probe distance, 0.5 m: a slope is the rise over that run, in permille.
        public const long ProbeRaw = SimFix.Scale / 2;

        private readonly int[] upAbove, upSpeed, downAbove, downSpeed;

        public ulong Digest { get; }

        // null for a direction the content did not write; a written direction has 1..MaxBands bands.
        public SlopeTable(int[] upAbove, int[] upSpeed, int[] downAbove, int[] downSpeed)
        {
            this.upAbove = Bands(upAbove, upSpeed, nameof(upAbove), out this.upSpeed);
            this.downAbove = Bands(downAbove, downSpeed, nameof(downAbove), out this.downSpeed);
            if (this.upAbove == null && this.downAbove == null)
                throw new ArgumentException("A slope table needs an uphill or a downhill table.");
            var hash = new StateHash();
            hash.Add("sim.slope-table.v1");
            Add(hash, this.upAbove, this.upSpeed);
            Add(hash, this.downAbove, this.downSpeed);
            Digest = hash.Value;
        }

        public bool HasUp => upAbove != null;
        public bool HasDown => downAbove != null;
        public int UpCount => upAbove == null ? 0 : upAbove.Length;
        public int DownCount => downAbove == null ? 0 : downAbove.Length;

        // The slope in permille of a rise over the probe distance: floor(|rise| * 1000 / 32768). |rise| < 2^30 (heights are
        // limited to 2^29 raw), so the product stays inside a long and the result inside an int.
        public static int SlopePermille(long rise)
        {
            long magnitude = rise < 0 ? -rise : rise;
            return (int)(magnitude * 1000 / ProbeRaw);
        }

        // The speed factor of a slope (permille, from SlopePermille) going up or down.
        public int SpeedPermille(int slopePermille, bool uphill)
        {
            int[] above = uphill ? upAbove : downAbove;
            int[] speed = uphill ? upSpeed : downSpeed;
            int factor = 1000;
            if (above == null) return factor;
            for (int i = 0; i < above.Length; i++)
                if (slopePermille > above[i]) factor = speed[i];
            return factor;
        }

        public int UpAbove(int band) => upAbove[band];
        public int UpSpeed(int band) => upSpeed[band];
        public int DownAbove(int band) => downAbove[band];
        public int DownSpeed(int band) => downSpeed[band];

        private static int[] Bands(int[] above, int[] speed, string name, out int[] speedCopy)
        {
            speedCopy = null;
            if (above == null && speed == null) return null;
            if (above == null || speed == null || above.Length != speed.Length)
                throw new ArgumentException("Every band needs a slope and a speed.", name);
            if (above.Length < 1 || above.Length > MaxBands)
                throw new ArgumentOutOfRangeException(name, "A table has 1 to " + MaxBands + " bands.");
            for (int i = 0; i < above.Length; i++)
            {
                if (above[i] < 0 || above[i] > MaxAbovePermille) throw new ArgumentOutOfRangeException(name);
                if (i > 0 && above[i] <= above[i - 1]) throw new ArgumentException("Bands must be strictly increasing.", name);
                if (speed[i] < MinSpeedPermille || speed[i] > MaxSpeedPermille) throw new ArgumentOutOfRangeException(name);
            }
            speedCopy = (int[])speed.Clone();
            return (int[])above.Clone();
        }

        private static void Add(StateHash hash, int[] above, int[] speed)
        {
            if (above == null) { hash.Add(-1); return; }
            hash.Add(above.Length);
            for (int i = 0; i < above.Length; i++) { hash.Add(above[i]); hash.Add(speed[i]); }
        }
    }
}
