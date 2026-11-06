using System;

namespace WarCore.Presentation
{
    public enum BridgeCommandKind { MoveTo, Hold, Follow }

    public readonly struct BridgeCommand
    {
        public long AtTick { get; }
        public int Squad { get; }
        public long XMm { get; }
        public long ZMm { get; }
        // MoveTo (the default) uses XMm/ZMm; Hold uses neither; Follow uses TargetKey (a UnitSpawn.Key).
        public BridgeCommandKind Kind { get; }
        public ulong TargetKey { get; }

        private BridgeCommand(long atTick, int squad, long xMm, long zMm, BridgeCommandKind kind = BridgeCommandKind.MoveTo, ulong targetKey = 0)
        {
            if (atTick < 1)
                throw new ArgumentOutOfRangeException(nameof(atTick));
            AtTick = atTick;
            Squad = squad;
            XMm = xMm;
            ZMm = zMm;
            Kind = kind;
            TargetKey = targetKey;
        }

        public static BridgeCommand MoveTo(long atTick, int squad, long xMm, long zMm)
            => new BridgeCommand(atTick, squad, xMm, zMm);

        public static BridgeCommand Hold(long atTick, int squad)
            => new BridgeCommand(atTick, squad, 0, 0, BridgeCommandKind.Hold);

        public static BridgeCommand Follow(long atTick, int squad, ulong targetKey)
            => new BridgeCommand(atTick, squad, 0, 0, BridgeCommandKind.Follow, targetKey);
    }
}
