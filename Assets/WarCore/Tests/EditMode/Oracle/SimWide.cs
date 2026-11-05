// Frozen from 9ddad58395d40f5e53097d4d98c7ba0c1bcb647e before A1/A2.
// Keep this arithmetic unchanged so later product optimizations have an independent oracle.
// Only the public result carrier is unwrapped to raw long; no product type is required.
namespace WarCore.Tests.Oracle
{
    // Squared values retain all 32 fractional bits until the caller explicitly narrows them.
    public readonly struct SimWide
    {
        internal WideInt Raw
        {
            get;
        }

        internal SimWide(WideInt raw)
        {
            Raw = raw;
        }

        public long ToSimFixRaw() => (Raw / WideInt.From(65536)).ToLong();
        public long SqrtRaw() => Raw.Sqrt().ToLong();
        public uint RawWord(int index) => Raw.Word(index);
        public int Sign => Raw.Sign;
    }

    public readonly struct SimWideVec3
    {
        public SimWide X
        {
            get;
        }

        public SimWide Y
        {
            get;
        }

        public SimWide Z
        {
            get;
        }

        internal SimWideVec3(WideInt x, WideInt y, WideInt z)
        {
            X = new SimWide(x);
            Y = new SimWide(y);
            Z = new SimWide(z);
        }
    }
}
