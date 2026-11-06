using System;
using WarCore.Sim;

namespace WarCore.Presentation
{
    public readonly struct BridgeCommand
    {
        public long AtTick { get; }
        public int Squad { get; }
        public long XMm { get; }
        public long ZMm { get; }
        // MoveTo (the default) uses XMm/ZMm; Hold uses neither; Follow uses TargetKey (a UnitSpawn.Key).
        public SquadCommandKind Kind { get; }
        public ulong TargetKey { get; }

        private BridgeCommand(long atTick, int squad, long xMm, long zMm, SquadCommandKind kind = SquadCommandKind.MoveTo, ulong targetKey = 0)
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
            => new BridgeCommand(atTick, squad, 0, 0, SquadCommandKind.Hold);

        public static BridgeCommand Follow(long atTick, int squad, ulong targetKey)
            => new BridgeCommand(atTick, squad, 0, 0, SquadCommandKind.Follow, targetKey);
    }
}
