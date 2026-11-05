using System;

namespace WarCore.Sim
{
    public readonly struct SimVec3 : IStateHash, IEquatable<SimVec3>
    {
        public const int WorldLimitMetres = 16384;
        public SimFix X
        {
            get;
        }

        public SimFix Y
        {
            get;
        }

        public SimFix Z
        {
            get;
        }

        // Vectors may be differences of two world positions, so bounds apply at world/query inputs.
        public SimVec3(SimFix x, SimFix y, SimFix z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static SimVec3 Metres(long x, long y, long z) => new SimVec3(SimFix.FromInt(x), SimFix.FromInt(y), SimFix.FromInt(z));
        public void ValidateWorld()
        {
            SpatialWidth.AssertWorldLimit(WorldLimitMetres);
            long limit = WorldLimitMetres * SimFix.Scale;
            if (X.Raw < -limit || X.Raw > limit || Y.Raw < -limit || Y.Raw > limit || Z.Raw < -limit || Z.Raw > limit)
                throw new ArgumentOutOfRangeException(nameof(SimVec3), "World position exceeds 16384 metres.");
        }

        public static SimVec3 operator +(SimVec3 x, SimVec3 y) => new SimVec3(x.X + y.X, x.Y + y.Y, x.Z + y.Z);
        public static SimVec3 operator -(SimVec3 x, SimVec3 y) => new SimVec3(x.X - y.X, x.Y - y.Y, x.Z - y.Z);
        // The scalar is dimensionless: preserve the wide product and check only the narrowed result.
        // The stricter area guard on SimFix * SimFix remains unchanged.
        public static SimVec3 operator *(SimVec3 value, SimFix scalar) => new SimVec3(
            SimFix.FromRaw(Product(value.X.Raw, scalar.Raw).Shift16ToLong()),
            SimFix.FromRaw(Product(value.Y.Raw, scalar.Raw).Shift16ToLong()),
            SimFix.FromRaw(Product(value.Z.Raw, scalar.Raw).Shift16ToLong()));
        public static SimVec3 operator *(SimFix scalar, SimVec3 value) => value * scalar;
        // Truncate each exact component midpoint toward zero, including opposite long endpoints.
        public static SimVec3 Midpoint(SimVec3 x, SimVec3 y) => new SimVec3(
            SimFix.FromRaw(FixedInt128.Midpoint(x.X.Raw, y.X.Raw)),
            SimFix.FromRaw(FixedInt128.Midpoint(x.Y.Raw, y.Y.Raw)),
            SimFix.FromRaw(FixedInt128.Midpoint(x.Z.Raw, y.Z.Raw)));
        internal static FixedInt128 Product(long x, long y) => FixedInt128.Product(x, y);
        public static SimWide Dot(SimVec3 x, SimVec3 y) => new SimWide(Product(x.X.Raw, y.X.Raw) + Product(x.Y.Raw, y.Y.Raw) + Product(x.Z.Raw, y.Z.Raw));
        public SimWide LengthSquared => Dot(this, this);
        // Keep the existing wide API; callers choose explicitly when to lose the low 16 bits.
        public static SimFix DotFix(SimVec3 x, SimVec3 y) => Dot(x, y).ToSimFix();
        public static SimFix DistanceSquared(SimVec3 x, SimVec3 y) => (x - y).LengthSquared.ToSimFix();
        // Integer square root rounds down; an unrepresentable signed length has the G25 overflow.
        public SimFix Length => LengthSquared.Sqrt();
        public SimVec3 Normalized
        {
            get
            {
                FixedUInt128 squared = LengthSquared.Raw.Magnitude;
                // Zero has no direction. Returning zero avoids a divisor trap without inventing an axis.
                if (squared.IsZero)
                    return default;
                return new SimVec3(NormalizeComponent(X.Raw, squared), NormalizeComponent(Y.Raw, squared),
                    NormalizeComponent(Z.Raw, squared));
            }
        }

        private static SimFix NormalizeComponent(long value, FixedUInt128 squaredLength)
        {
            // floor(sqrt(floor(x*x*Scale*Scale / lengthSquared))) equals truncation of the
            // exact normalized magnitude. Dividing by a rounded length would distort tiny vectors.
            FixedUInt128 square = FixedUInt128.Multiply(FixedInt128.Abs(value), FixedInt128.Abs(value));
            GeometryUInt192 scaled = GeometryUInt192.Scale32(square);
            // World-range vectors fit the 128/64 divider. Full long inputs need at most 159 bits.
            ulong ratio = scaled.High == 0 && squaredLength.High == 0
                ? new FixedUInt128(scaled.Low, scaled.Middle).DivideToUInt64(squaredLength.Low)
                : scaled.Divide(squaredLength).Low;
            return SimFix.FromRaw(FixedInt128.ToLong(new FixedUInt128(ratio).Sqrt(), value < 0));
        }
        public static SimWideVec3 Cross(SimVec3 x, SimVec3 y) => new SimWideVec3(Product(x.Y.Raw, y.Z.Raw) - Product(x.Z.Raw, y.Y.Raw), Product(x.Z.Raw, y.X.Raw) - Product(x.X.Raw, y.Z.Raw), Product(x.X.Raw, y.Y.Raw) - Product(x.Y.Raw, y.X.Raw));
        public bool Equals(SimVec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is SimVec3 other && Equals(other);
        public override int GetHashCode() => SimInteger.Int32Bits(unchecked((uint)X.GetHashCode() * 397u ^ (uint)Y.GetHashCode() * 31u ^ (uint)Z.GetHashCode()));
        public void WriteState(StateHash hash)
        {
            X.WriteState(hash);
            Y.WriteState(hash);
            Z.WriteState(hash);
        }
    }
}
