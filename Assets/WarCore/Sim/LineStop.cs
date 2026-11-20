namespace WarCore.Sim
{
    // ML-9: where a segment first stops on the ground (HeightField.TryFirstBelow) or in a floor plate (PlateField.TryFirstThrough).
    // Either may be null. Both tests run on the same segment; the smaller t wins (ground t = n / d with d <= 2^34, plate
    // t = TNum / TDen with TDen < 2^52: cross products below 2^86), a tie goes to the ground, and at is the winner's own point
    // as that test reports it (never recomputed, so a rounded point is never a new start). Without plates the result is the
    // ground's, without ground the plates'.
    public static class LineStop
    {
        public static bool TryFirst(HeightField ground, PlateField plates, SimVec3 from, SimVec3 to, out SimVec3 at)
        {
            at = default;
            long gn = 0, gd = 1;
            bool below = ground != null && ground.TryFirstBelow(from, to, out at, out gn, out gd);
            if (plates == null || !plates.TryFirstThrough(from, to, out PlateHit hit)) return below;
            if (below && FixedUInt128.Multiply((ulong)gn, (ulong)hit.TDen).CompareTo(FixedUInt128.Multiply((ulong)hit.TNum, (ulong)gd)) <= 0)
                return true;
            at = hit.At;
            return true;
        }
    }
}
