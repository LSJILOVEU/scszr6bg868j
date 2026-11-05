using System;
using System.Globalization;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification
{
    // Post-run only. Compare every empirical quantile, including the maximum.
    // Replication gives exact equal weights for 300 versus 1200 samples.
    public sealed class ProfilerBaselineDelta
    {
        public readonly bool Pass;
        public readonly long ByteExcess;
        public readonly long CountExcess;
        public readonly int Ranks;
        public readonly long SimBytes, BaselineBytes, SimCount, BaselineCount;
        public readonly int SimFrames, BaselineFrames;

        public ProfilerBaselineDelta(ProfilerFrameReading[] samples, int simStart, int simFrames,
            int baselineStart, int baselineFrames)
        {
            if (simFrames <= 0 || baselineFrames <= 0 || baselineFrames % simFrames != 0)
                throw new ArgumentException("Baseline frames must be a positive multiple of Sim frames.");
            SimFrames = simFrames;
            BaselineFrames = baselineFrames;
            Ranks = baselineFrames;
            var sb = new long[simFrames];
            var sc = new long[simFrames];
            var bb = new long[baselineFrames];
            var bc = new long[baselineFrames];
            for (int i = 0; i < simFrames; i++)
            {
                var sample = samples[simStart + i];
                if (!sample.Available) throw new ArgumentException("Unavailable Sim frame.");
                SimBytes += sb[i] = sample.Bytes;
                SimCount += sc[i] = sample.Allocations;
            }
            for (int i = 0; i < baselineFrames; i++)
            {
                var sample = samples[baselineStart + i];
                if (!sample.Available) throw new ArgumentException("Unavailable baseline frame.");
                BaselineBytes += bb[i] = sample.Bytes;
                BaselineCount += bc[i] = sample.Allocations;
            }
            Array.Sort(sb); Array.Sort(sc); Array.Sort(bb); Array.Sort(bc);
            int repeat = baselineFrames / simFrames;
            for (int rank = 0; rank < Ranks; rank++)
            {
                // Negative differences never cancel a positive difference.
                ByteExcess += Math.Max(0L, sb[rank / repeat] - bb[rank]);
                CountExcess += Math.Max(0L, sc[rank / repeat] - bc[rank]);
            }
            Pass = ByteExcess == 0 && CountExcess == 0;
        }

        private static string Ratio(long sim, int ns, long baseline, int nb)
            => baseline == 0 ? (sim == 0 ? "1" : "infinity")
                : ((double)sim * nb / (baseline * (double)ns)).ToString("R", CultureInfo.InvariantCulture);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "rule=all-quantiles-v1; pass={0}; sim_frames={1}; baseline_frames={2}; sim_bytes_sum={3}; baseline_bytes_sum={4}; sim_count_sum={5}; baseline_count_sum={6}; byte_mean_delta={7:R}; count_mean_delta={8:R}; byte_mean_ratio={9}; count_mean_ratio={10}; ranks={11}; positive_byte_rank_sum={12}; positive_count_rank_sum={13}",
                Pass, SimFrames, BaselineFrames, SimBytes, BaselineBytes, SimCount, BaselineCount,
                (double)SimBytes / SimFrames - (double)BaselineBytes / BaselineFrames,
                (double)SimCount / SimFrames - (double)BaselineCount / BaselineFrames,
                Ratio(SimBytes, SimFrames, BaselineBytes, BaselineFrames),
                Ratio(SimCount, SimFrames, BaselineCount, BaselineFrames), Ranks, ByteExcess, CountExcess);
        }
    }
}
