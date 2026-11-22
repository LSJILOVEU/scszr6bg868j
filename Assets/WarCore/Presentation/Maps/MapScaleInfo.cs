namespace WarCore.Presentation
{
    // SCALE-2b: the standard parts of a description's content layer (section "scale", or the defaults without one) in plain
    // values for the editor. Heights and widths raw (65536 = 1 m), already converted and padded by the content side; the
    // stair step in millimetres as written. MapDocument.Scale gives it.
    public sealed class MapScaleInfo
    {
        public long StoreyRaw { get; }
        public long PlateRaw { get; }
        public long LowWallRaw { get; }
        public long ChestWallRaw { get; }
        public long HighWallRaw { get; }
        public long WindowSillRaw { get; }
        public long WindowTopRaw { get; }
        public long RailingRaw { get; }
        public long LaneRaw { get; }
        public long SidewalkRaw { get; }
        public long AlleyRaw { get; }
        public int StairRiseMm { get; }
        public int StairRunMm { get; }

        internal MapScaleInfo(long storeyRaw, long plateRaw, long lowWallRaw, long chestWallRaw, long highWallRaw, long windowSillRaw,
            long windowTopRaw, long railingRaw, long laneRaw, long sidewalkRaw, long alleyRaw, int stairRiseMm, int stairRunMm)
        {
            StoreyRaw = storeyRaw; PlateRaw = plateRaw; LowWallRaw = lowWallRaw; ChestWallRaw = chestWallRaw; HighWallRaw = highWallRaw;
            WindowSillRaw = windowSillRaw; WindowTopRaw = windowTopRaw; RailingRaw = railingRaw; LaneRaw = laneRaw;
            SidewalkRaw = sidewalkRaw; AlleyRaw = alleyRaw; StairRiseMm = stairRiseMm; StairRunMm = stairRunMm;
        }
    }
}
