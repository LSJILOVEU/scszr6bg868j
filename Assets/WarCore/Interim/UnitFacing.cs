using WarCore.Sim;

namespace WarCore.Interim
{
    // One horizontal facing per unit, as a fixed-point direction (X, Z). It exists only in worlds whose recipe opens it,
    // so every other world keeps its exact snapshots and hashes. Nothing reads it yet.
    //
    // A direction vector rather than an angle: movement already yields a direction (delta.Normalized), and the arc and
    // line tests that come later are dot and cross products of vectors. An angle would need a sine and cosine table
    // and a wrap rule, and would turn every comparison back into a vector first. The writer keeps it a unit vector
    // (up to fixed-point rounding); a zero vector is never written, and Default is used until something does.
    public readonly struct UnitFacing : IStateHash
    {
        public const string ComponentId = "interim.facing";
        // +Z, what a unit faces until it moves or is aimed.
        public static UnitFacing Default => new UnitFacing(default, SimFix.FromInt(1));
        public SimFix X { get; }
        public SimFix Z { get; }
        public UnitFacing(SimFix x, SimFix z) { X = x; Z = z; }
        public void WriteState(StateHash hash) { hash.Add(X.Raw); hash.Add(Z.Raw); }
    }

    public sealed class UnitFacingSerializer : IStableComponentSerializer<UnitFacing>
    {
        public int TypeId => 2004;
        public int Version => 1;
        public void Write(SimStateWriter writer, in UnitFacing value) { writer.Int64(value.X.Raw); writer.Int64(value.Z.Raw); }
        public UnitFacing Read(SimStateReader reader) => new UnitFacing(SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()));
    }
}
