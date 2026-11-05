using System;

namespace WarCore.Presentation
{
    public readonly struct BridgeCommand
    {
        public long AtTick { get; }
        public int Squad { get; }
        public long XMm { get; }
        public long ZMm { get; }

        private BridgeCommand(long atTick, int squad, long xMm, long zMm)
        {
            if (atTick < 1)
                throw new ArgumentOutOfRangeException(nameof(atTick));
            AtTick = atTick;
            Squad = squad;
            XMm = xMm;
            ZMm = zMm;
        }

        public static BridgeCommand MoveTo(long atTick, int squad, long xMm, long zMm)
            => new BridgeCommand(atTick, squad, xMm, zMm);
    }
}
