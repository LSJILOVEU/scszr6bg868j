namespace WarCore.Sim
{
    // This is the legacy radial-edge square root, independent of the SimMath phase tables.
    internal static class GeometrySinTable
    {
        [ImmutableTable(0x9B598C62F03A0860UL)]
        private static readonly long[] Values = Build();

        static GeometrySinTable()
        {
        }

        // Query providers initialize the table during construction, before any warmed query path.
        internal static void Initialize()
        {
        }

        private static long[] Build()
        {
            var values = new long[2001];
            for (int cosine = -1000; cosine <= 1000; cosine++)
            {
                ulong radicandHigh = (ulong)(1000000 - cosine * cosine);
                values[cosine + 1000] = (long)new FixedUInt128(0, radicandHigh).Sqrt();
            }

            return values;
        }

        internal static long Sin(int cosine) => Values[cosine + 1000];
    }
}
