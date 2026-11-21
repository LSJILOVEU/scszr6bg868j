using System;

namespace WarCore.Presentation
{
    public enum BridgeCommandKind { MoveTo, Hold, Follow, Blast }

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
        // BLAST-1: the blast kind (an id of the combat pack's blasts) a Blast command sets off at XMm/ZMm on Layer; null otherwise.
        public string BlastId { get; }

        private BridgeCommand(long atTick, int squad, long xMm, long zMm, BridgeCommandKind kind = BridgeCommandKind.MoveTo, ulong targetKey = 0, int layer = 0,
            string blastId = null)
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
            BlastId = blastId;
        }

        public static BridgeCommand MoveTo(long atTick, int squad, long xMm, long zMm)
            => new BridgeCommand(atTick, squad, xMm, zMm);

        public static BridgeCommand MoveTo(long atTick, int squad, long xMm, long zMm, int layer)
            => new BridgeCommand(atTick, squad, xMm, zMm, layer: layer);

        public static BridgeCommand Hold(long atTick, int squad)
            => new BridgeCommand(atTick, squad, 0, 0, BridgeCommandKind.Hold);

        public static BridgeCommand Follow(long atTick, int squad, ulong targetKey)
            => new BridgeCommand(atTick, squad, 0, 0, BridgeCommandKind.Follow, targetKey);

        // BLAST-1: sets off a blast of kind blastId on tick atTick at (xMm, zMm) on the ground (layer 0) or on a floor plate. The
        // session puts it on the ground or plate surface there and queues it before the tick's step, so it goes off on that tick;
        // no source, team 0. Like every command it takes the tick alone: a second command for the same tick is dropped.
        public static BridgeCommand Blast(long atTick, long xMm, long zMm, string blastId, int layer = 0)
        {
            if (string.IsNullOrEmpty(blastId)) throw new ArgumentException("A blast command needs a blast id.", nameof(blastId));
            return new BridgeCommand(atTick, 0, xMm, zMm, BridgeCommandKind.Blast, layer: layer, blastId: blastId);
        }
    }
}
