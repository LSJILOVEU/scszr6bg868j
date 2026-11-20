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
        // The layer of a MoveTo target: 0 = the ground, 1..N = a floor plate of the map (worlds with plates only).
        public int Layer { get; }

        private BridgeCommand(long atTick, int squad, long xMm, long zMm, BridgeCommandKind kind = BridgeCommandKind.MoveTo, ulong targetKey = 0, int layer = 0)
        {
            if (atTick < 1)
                throw new ArgumentOutOfRangeException(nameof(atTick));
            if (layer < 0 || layer > UnitSpawn.MaxLayer)
                throw new ArgumentOutOfRangeException(nameof(layer), "A layer is 0 to 4096.");
            Layer = layer;
            AtTick = atTick;
            Squad = squad;
            XMm = xMm;
            ZMm = zMm;
            Kind = kind;
            TargetKey = targetKey;
        }

        public static BridgeCommand MoveTo(long atTick, int squad, long xMm, long zMm)
            => new BridgeCommand(atTick, squad, xMm, zMm);

        public static BridgeCommand MoveTo(long atTick, int squad, long xMm, long zMm, int layer)
            => new BridgeCommand(atTick, squad, xMm, zMm, layer: layer);

        public static BridgeCommand Hold(long atTick, int squad)
            => new BridgeCommand(atTick, squad, 0, 0, BridgeCommandKind.Hold);

        public static BridgeCommand Follow(long atTick, int squad, ulong targetKey)
            => new BridgeCommand(atTick, squad, 0, 0, BridgeCommandKind.Follow, targetKey);
    }
}
