using System;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Reproducible oriented-box samples. Every request goes through the public SpatialQueries
    // factories. Groups: dense local scenes, the full world, world-limit values, exact contacts
    // built from small Pythagorean yaws (faces, edges and corners land on integer points), and
    // near-surface queries around arbitrary yaws.
    internal sealed class BoxInputs
    {
        internal const long World = 1L << 30;
        internal const int Groups = 5;
        private A1MathInputs random;

        internal BoxInputs(ulong seed)
        {
            random = new A1MathInputs(seed);
        }

        internal readonly struct Sample
        {
            internal readonly SpatialBody Body;
            internal readonly SpatialRequest Request;
            internal readonly string Source;

            internal Sample(SpatialBody body, SpatialRequest request, string source)
            {
                Body = body;
                Request = request;
                Source = source;
            }
        }

        internal static SimVec3 V(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        private static SimFix F(long raw) => SimFix.FromRaw(raw);
        private long Signed(long limit) => limit <= 0 ? 0 : (long)(random.Next() % (ulong)(limit * 2 + 1)) - limit;
        private long Positive(long limit) => limit <= 0 ? 0 : (long)(random.Next() % (ulong)(limit + 1));
        private int Pick(int count) => (int)(random.Next() % (ulong)count);
        private static long ClampWorld(long value) => Math.Max(-World, Math.Min(World, value));
        private static SimVec3 ClampWorld(SimVec3 v) => V(ClampWorld(v.X.Raw), ClampWorld(v.Y.Raw), ClampWorld(v.Z.Raw));

        internal BoxYaw Yaw(int mode)
        {
            switch (mode % 6)
            {
                case 0:
                    return BoxYaw.Identity;
                case 1:
                    return BoxYaw.QuarterTurns(Pick(4));
                case 2:
                    return BoxYaw.FromGenerator(4095 - Pick(3), Pick(2) == 0 ? 1 : -1);
                case 3:
                    return SmallYaw(8);
                default:
                    while (true)
                    {
                        int m = (int)Signed(4096);
                        int n = (int)Signed(4096);
                        if ((m != 0 || n != 0) && (long)m * m + (long)n * n <= BoxYaw.MaxDenominator)
                        {
                            BoxYaw yaw = BoxYaw.FromGenerator(m, n);
                            if (yaw.Denominator <= BoxYaw.MaxDenominator)
                                return yaw;
                        }
                    }
            }
        }

        private BoxYaw SmallYaw(int limit)
        {
            while (true)
            {
                int m = (int)Signed(limit);
                int n = (int)Signed(limit);
                if (m != 0 || n != 0)
                    return BoxYaw.FromGenerator(m, n);
            }
        }

        internal Collider Box(long size, int index)
        {
            long limit = Math.Min(size, Collider.BoxHalfExtentLimit);
            long hx = index % 11 == 0 ? 0 : Positive(limit);
            long hz = index % 13 == 0 ? 0 : Positive(limit);
            long height = index % 17 == 0 ? 0 : Positive(Math.Min(2 * size, 32768L * SimFix.Scale));
            if (index % 29 == 0)
                height++;
            return Collider.Box(F(hx), F(Math.Min(height, 32768L * SimFix.Scale)), F(hz), Yaw(index / 3));
        }

        internal Sample Random(SpatialQueryKind kind, int index)
        {
            int group = index % Groups;
            if (group == 3)
                return Contact(kind, index);
            if (group == 4)
                return Near(kind, index);
            long size = group == 0 ? 32 * SimFix.Scale : World;
            SimVec3 position = group == 2 ? V(Edge(), Edge(), Edge()) : V(Signed(size), Signed(size), Signed(size));
            SimVec3 origin = group == 2 ? V(Edge(), Edge(), Edge()) : V(Signed(size), Signed(size), Signed(size));
            Collider collider = group == 2 && index % 4 == 0
                ? Collider.Box(F(Collider.BoxHalfExtentLimit), F(32768L * SimFix.Scale), F(Collider.BoxHalfExtentLimit), Yaw(index))
                : Box(size, index);
            return Make(kind, position, collider, origin, Signed(size), Signed(size), Signed(size), Positive(size), Positive(size), Positive(size),
                (int)Signed(1000), "group" + group + ":" + index);
        }

        private long Edge()
        {
            switch (Pick(7))
            {
                case 0: return -World;
                case 1: return -World + 1;
                case 2: return World;
                case 3: return World - 1;
                default: return Signed(1);
            }
        }

        private static readonly int[] Cosines = { -1000, -999, -707, -1, 0, 1, 500, 707, 999, 1000 };

        internal Sample Make(SpatialQueryKind kind, SimVec3 position, Collider collider, SimVec3 origin,
            long dx, long dy, long dz, long radius, long range, long halfHeight, int cosine, string source)
        {
            var body = new SpatialBody(new EntityId(0, 1), position, collider);
            if (Pick(3) == 0)
                cosine = Cosines[Pick(Cosines.Length)];
            SpatialRequest request;
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    request = SpatialQueries.Sphere(origin, F(radius));
                    break;
                case SpatialQueryKind.Cylinder:
                    request = SpatialQueries.Cylinder(origin, F(radius), F(halfHeight));
                    break;
                case SpatialQueryKind.Sector:
                    if (dx == 0 && dz == 0)
                        dx = 1;
                    request = SpatialQueries.Sector(origin, F(radius), V(ClampWorld(dx), 0, ClampWorld(dz)), cosine, F(halfHeight));
                    break;
                case SpatialQueryKind.Capsule:
                    SimVec3 end = ClampWorld(V(origin.X.Raw + dx / 2, origin.Y.Raw + dy / 2, origin.Z.Raw + dz / 2));
                    request = SpatialQueries.Capsule(origin, end, F(radius));
                    break;
                default:
                    if (dx == 0 && dy == 0 && dz == 0)
                        dy = -1;
                    request = SpatialQueries.Ray(origin, V(ClampWorld(dx), ClampWorld(dy), ClampWorld(dz)), F(range));
                    break;
            }
            return new Sample(body, request, source);
        }

        // Queries around a random box at roughly its own scale: many hits, misses and near touches.
        private Sample Near(SpatialQueryKind kind, int index)
        {
            long size = index % 3 == 0 ? 4 * SimFix.Scale : index % 3 == 1 ? 64 * SimFix.Scale : 4096L * SimFix.Scale;
            Collider collider = Box(size, index);
            SimVec3 position = V(Signed(World / 2), Signed(World / 2), Signed(World / 2));
            long reach = 2 * size + 2;
            SimVec3 origin = ClampWorld(position + V(Signed(reach), Signed(reach), Signed(reach)));
            return Make(kind, position, collider, origin, Signed(2 * reach), Signed(2 * reach), Signed(2 * reach),
                Positive(size), Positive(Math.Min(World, 3 * reach)), Positive(size), (int)Signed(1000), "near:" + index);
        }

        // Exact contacts: with c | hx and c | hz every face point j*(b, a) + kx*(a, -b) is an integer.
        private Sample Contact(SpatialQueryKind kind, int index)
        {
            BoxYaw yaw = Pick(4) == 0 ? BoxYaw.QuarterTurns(Pick(4)) : SmallYaw(6);
            long a = yaw.Cos;
            long b = yaw.Sin;
            long c = yaw.Denominator;
            long kx = index % 7 == 0 ? 0 : 1 + Positive(1 << 10);
            long kz = index % 9 == 0 ? 0 : 1 + Positive(1 << 10);
            long hy = index % 11 == 0 ? 0 : Positive(1 << 20);
            var collider = Collider.Box(F(c * kx), F(2 * hy), F(c * kz), yaw);
            SimVec3 center = V(Signed(1L << 28), Signed(1L << 28), Signed(1L << 28));
            int su = Pick(2) == 0 ? 1 : -1;
            int sv = Pick(2) == 0 ? 1 : -1;
            int feature = Pick(3);
            long j = feature == 0 ? Signed(kz) : sv * kz;
            // Face (feature 0), edge-on corner column (1) or a corner diagonal (2).
            long px = su * kx * a + j * b;
            long pz = -su * kx * b + j * a;
            long nu = feature == 2 ? 3 : 1;
            long nv = feature == 2 ? 4 : 0;
            long g = Positive(1 << 8);
            // Outward offset: nu*g along u (length c) and nv*g along v.
            long ox = g * (su * nu * a + sv * nv * b);
            long oz = g * (-su * nu * b + sv * nv * a);
            long gap = feature == 2 ? 5 * g * c : g * c;
            long y = Pick(3) == 0 ? (Pick(2) == 0 ? hy : -hy) : Signed(hy);
            int delta = Pick(3) - 1;
            SimVec3 surface = V(center.X.Raw + px, center.Y.Raw + y, center.Z.Raw + pz);
            SimVec3 outside = V(surface.X.Raw + ox, surface.Y.Raw, surface.Z.Raw + oz);
            var body = new SpatialBody(new EntityId(0, 1), center, collider);
            string source = "contact:" + kind + ":f" + feature + ":" + index;
            SpatialRequest request;
            long radius = Math.Max(0, gap + delta);
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    if (Pick(4) == 0)
                    {
                        // Vertical contact above or below the cap.
                        long lift = Positive(1 << 12);
                        SimVec3 cap = V(surface.X.Raw, center.Y.Raw + (Pick(2) == 0 ? hy + lift : -hy - lift), surface.Z.Raw);
                        request = SpatialQueries.Sphere(cap, F(Math.Max(0, lift + delta)));
                    }
                    else
                        request = SpatialQueries.Sphere(outside, F(radius));
                    break;
                case SpatialQueryKind.Cylinder:
                {
                    long hh = Positive(1 << 12);
                    long cy = Pick(2) == 0 ? outside.Y.Raw : center.Y.Raw + (Pick(2) == 0 ? 1 : -1) * (hy + hh + Pick(3) - 1);
                    request = SpatialQueries.Cylinder(V(outside.X.Raw, cy, outside.Z.Raw), F(radius), F(hh));
                    break;
                }
                case SpatialQueryKind.Sector:
                {
                    long dx = -ox;
                    long dz = -oz;
                    if (Pick(3) == 0)
                    {
                        // Point the axis along the face so the contact is through a radial edge.
                        dx = b * (Pick(2) == 0 ? 1 : -1);
                        dz = a * (dx > 0 == b > 0 ? 1 : -1);
                    }
                    if (dx == 0 && dz == 0)
                        dx = 1;
                    request = SpatialQueries.Sector(outside, F(radius), V(dx, 0, dz), Cosines[Pick(Cosines.Length)], F(Positive(1 << 12)));
                    break;
                }
                case SpatialQueryKind.Capsule:
                {
                    // A sweep parallel to the face at the contact gap, or straight at the surface.
                    long run = Positive(1 << 12);
                    SimVec3 end = Pick(2) == 0
                        ? V(outside.X.Raw + run * b, outside.Y.Raw, outside.Z.Raw + run * a)
                        : V(surface.X.Raw - ox / 2, surface.Y.Raw, surface.Z.Raw - oz / 2);
                    request = SpatialQueries.Capsule(Pick(2) == 0 ? outside : V(outside.X.Raw - run * b, outside.Y.Raw, outside.Z.Raw - run * a), end, F(Math.Max(0, gap / 2 + delta)));
                    break;
                }
                default:
                {
                    long dx = -ox;
                    long dz = -oz;
                    long dy = 0;
                    SimVec3 origin = outside;
                    if (Pick(3) == 0)
                    {
                        // Graze along the face plane from beyond its edge.
                        long slide = kz + 1 + Positive(16);
                        origin = V(center.X.Raw + su * kx * a + slide * b, surface.Y.Raw, center.Z.Raw - su * kx * b + slide * a);
                        dx = -b;
                        dz = -a;
                    }
                    else if (dx == 0 && dz == 0)
                        dy = surface.Y.Raw >= center.Y.Raw ? -1 : 1;
                    long travel = Math.Max(1, OracleLength(dx, dy, dz));
                    request = SpatialQueries.Ray(origin, V(dx, dy, dz), F(Math.Max(0, travel + delta * Positive(2))));
                    break;
                }
            }
            return new Sample(body, request, source);
        }

        private static long OracleLength(long x, long y, long z)
            => (long)Oracle.OracleBox.FloorSqrt((System.Numerics.BigInteger)x * x + (System.Numerics.BigInteger)y * y + (System.Numerics.BigInteger)z * z);
    }
}
