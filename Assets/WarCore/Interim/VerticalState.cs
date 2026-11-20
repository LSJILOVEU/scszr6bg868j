using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    // Which layer a unit stands on (0 = the ground, 1..N = floor plates in baked order, the LayeredNav layers) and the layer of
    // its last move target. It exists only in worlds whose map has plates, so every other world keeps its exact snapshots and
    // hashes. Air (65535) is reserved for units in the air: no entry point accepts it yet, a snapshot that holds it is read.
    public readonly struct VerticalState : IStateHash
    {
        public const string ComponentId = "interim.vertical-state";
        public const ushort Air = 65535;
        public ushort Layer { get; }
        public ushort TargetLayer { get; }
        public VerticalState(int layer, int targetLayer)
        {
            Layer = Checked(layer, nameof(layer));
            TargetLayer = Checked(targetLayer, nameof(targetLayer));
        }

        private VerticalState(ushort layer, ushort targetLayer, bool raw) { Layer = layer; TargetLayer = targetLayer; }

        internal static VerticalState Raw(ushort layer, ushort targetLayer) => new VerticalState(layer, targetLayer, true);

        // A layer number accepted on input: 0 to PlateField.MaxPlates.
        internal static ushort Checked(int layer, string name)
        {
            if (layer < 0 || layer > PlateField.MaxPlates) throw new ArgumentOutOfRangeException(name, "A layer is 0 to 4096.");
            return (ushort)layer;
        }

        internal static bool Readable(int value) => value >= 0 && value <= PlateField.MaxPlates || value == Air;

        public VerticalState WithTarget(int targetLayer) => new VerticalState(Layer, Checked(targetLayer, nameof(targetLayer)), true);

        public void WriteState(StateHash hash) { hash.Add((int)Layer); hash.Add((int)TargetLayer); }
    }

    public sealed class VerticalStateSerializer : IStableComponentSerializer<VerticalState>
    {
        public int TypeId => 2010;
        public int Version => 1;
        public void Write(SimStateWriter writer, in VerticalState value)
        {
            writer.Int32(value.Layer); writer.Int32(value.TargetLayer);
        }
        public VerticalState Read(SimStateReader reader)
        {
            int layer = reader.Int32(), target = reader.Int32();
            SimStateReader.Require(VerticalState.Readable(layer) && VerticalState.Readable(target), "Invalid vertical state.");
            return VerticalState.Raw((ushort)layer, (ushort)target);
        }
    }
}
