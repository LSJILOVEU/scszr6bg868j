using System;

namespace WarCore.Sim
{
    // Squared values retain all 32 fractional bits until the caller explicitly narrows them.
    public readonly struct SimWide : IComparable<SimWide>, IEquatable<SimWide>
    {
        private readonly FixedInt128 value;

        internal FixedInt128 Raw => value;

        internal SimWide(FixedInt128 raw)
        {
            value = raw;
        }

        public SimFix ToSimFix() => SimFix.FromRaw(value.Shift16ToLong());
        public SimFix Sqrt() => SimFix.FromRaw(value.SqrtToLong());
        public uint RawWord(int index) => value.Magnitude.Word(index);
        public int Sign => value.Sign;

        public int CompareTo(SimWide other)
        {
            if (Sign != other.Sign)
                return Sign < other.Sign ? -1 : 1;
            int order = value.Magnitude.CompareTo(other.value.Magnitude);
            return Sign < 0 ? -order : order;
        }

        public bool Equals(SimWide other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is SimWide other && Equals(other);
        public override int GetHashCode() => value.Magnitude.Low.GetHashCode() ^ value.Magnitude.High.GetHashCode() ^ Sign;
        public static bool operator <(SimWide a, SimWide b) => a.CompareTo(b) < 0;
        public static bool operator >(SimWide a, SimWide b) => a.CompareTo(b) > 0;
        public static bool operator <=(SimWide a, SimWide b) => a.CompareTo(b) <= 0;
        public static bool operator >=(SimWide a, SimWide b) => a.CompareTo(b) >= 0;
        public static bool operator ==(SimWide a, SimWide b) => a.Equals(b);
        public static bool operator !=(SimWide a, SimWide b) => !a.Equals(b);
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

        internal SimWideVec3(FixedInt128 x, FixedInt128 y, FixedInt128 z)
        {
            X = new SimWide(x);
            Y = new SimWide(y);
            Z = new SimWide(z);
        }
    }
}
