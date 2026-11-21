namespace WarCore.Content.MapTools
{
    // ED-3a integer noise for terrain stamps: SplitMix64's finaliser over a mixed key. No System.Random, so the same seed gives
    // the same terrain on every platform.
    public static class MapNoise
    {
        // finalise(seed + a * 0x9E3779B97F4A7C15 + b * 0xBF58476D1CE4E5B9 + c * 0x94D049BB133111EB), all wrapping as ulong.
        public static ulong Hash(ulong seed, long a, long b, long c)
        {
            unchecked
            {
                ulong z = seed + (ulong)a * 0x9E3779B97F4A7C15UL + (ulong)b * 0xBF58476D1CE4E5B9UL + (ulong)c * 0x94D049BB133111EBUL;
                z ^= z >> 30;
                z *= 0xBF58476D1CE4E5B9UL;
                z ^= z >> 27;
                z *= 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return z;
            }
        }

        // An integer in [lo, hi] (lo <= hi): lo + Hash(seed, k, a, b) mod (hi - lo + 1).
        public static long Pick(ulong seed, long k, long a, long b, long lo, long hi)
        {
            unchecked
            {
                ulong span = (ulong)(hi - lo) + 1UL;
                return lo + (long)(Hash(seed, k, a, b) % span);
            }
        }
    }
}
