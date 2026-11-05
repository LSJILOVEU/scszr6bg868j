using System.Numerics;
using WarCore.Sim;

namespace WarCore.Tests.Oracle
{
    // Independent oriented-box reference. The box is a world-frame set with exact rational axes
    // u = (a, -b)/c and v = (b, a)/c: |(p-c).u| <= hx, |(p-c).v| <= hz, |py-cy| <= floor(H/2).
    // It uses arbitrary-precision rationals and different algorithms from the product: distances
    // by nearest rectangle edge, rays by face-plane candidates plus containment, sweeps by the 27
    // region quadratics plus breakpoints, and sector edges by line candidates plus containment.
    // It never calls product geometry or the scaled box-local integer frame.
    internal static class OracleBox
    {
        internal readonly struct Q
        {
            internal readonly BigInteger N;
            internal readonly BigInteger D;

            internal Q(BigInteger n, BigInteger d)
            {
                if (d.Sign < 0)
                {
                    n = -n;
                    d = -d;
                }
                BigInteger g = BigInteger.GreatestCommonDivisor(n, d);
                if (!g.IsZero && !g.IsOne)
                {
                    n /= g;
                    d /= g;
                }
                N = n;
                D = d;
            }

            internal static Q Of(BigInteger value) => new Q(value, BigInteger.One);
            internal int Sign => N.Sign;
            public static Q operator +(Q a, Q b) => new Q(a.N * b.D + b.N * a.D, a.D * b.D);
            public static Q operator -(Q a, Q b) => new Q(a.N * b.D - b.N * a.D, a.D * b.D);
            public static Q operator -(Q a) => new Q(-a.N, a.D);
            public static Q operator *(Q a, Q b) => new Q(a.N * b.N, a.D * b.D);
            public static Q operator /(Q a, Q b) => new Q(a.N * b.D, a.D * b.N);
            internal int CompareTo(Q other) => (N * other.D).CompareTo(other.N * D);
            internal static Q Min(Q a, Q b) => a.CompareTo(b) <= 0 ? a : b;
            internal static Q Max(Q a, Q b) => a.CompareTo(b) >= 0 ? a : b;
        }

        private readonly struct Box
        {
            internal readonly Q Cx, Cy, Cz, Ux, Uz, Vx, Vz, Hx, Hy, Hz;

            internal Box(in SpatialBody body)
            {
                BoxYaw yaw = body.Collider.Yaw;
                BigInteger m = yaw.M;
                BigInteger n = yaw.N;
                BigInteger a = m * m - n * n;
                BigInteger b = 2 * m * n;
                BigInteger c = m * m + n * n;
                Cx = Q.Of(body.Position.X.Raw);
                Cy = Q.Of(body.Position.Y.Raw);
                Cz = Q.Of(body.Position.Z.Raw);
                Ux = new Q(a, c);
                Uz = new Q(-b, c);
                Vx = new Q(b, c);
                Vz = new Q(a, c);
                Hx = Q.Of(body.Collider.Radius.Raw);
                Hy = Q.Of(body.Collider.Height.Raw / 2);
                Hz = Q.Of(body.Collider.HalfDepth.Raw);
            }

            internal Q ProjectU(Q x, Q z) => (x - Cx) * Ux + (z - Cz) * Uz;
            internal Q ProjectV(Q x, Q z) => (x - Cx) * Vx + (z - Cz) * Vz;

            internal bool Contains2D(Q x, Q z)
                => Within(ProjectU(x, z), Hx) && Within(ProjectV(x, z), Hz);

            internal bool Contains(Q x, Q y, Q z) => Contains2D(x, z) && Within(y - Cy, Hy);

            // Corners in order (-,-), (+,-), (+,+), (-,+).
            internal void Corner(int index, out Q x, out Q z)
            {
                Q su = index == 1 || index == 2 ? Hx : -Hx;
                Q sv = index >= 2 ? Hz : -Hz;
                x = Cx + su * Ux + sv * Vx;
                z = Cz + su * Uz + sv * Vz;
            }
        }

        private static bool Within(Q value, Q extent) => value.CompareTo(-extent) >= 0 && value.CompareTo(extent) <= 0;

        // Closest rectangle point: the query point itself when inside, else the nearest of four edges.
        private static Q Closest2D(in Box box, Q x, Q z, out Q px, out Q pz)
        {
            px = x;
            pz = z;
            if (box.Contains2D(x, z))
                return Q.Of(0);
            Q best = default;
            bool found = false;
            for (int i = 0; i < 4; i++)
            {
                box.Corner(i, out Q ax, out Q az);
                box.Corner((i + 1) % 4, out Q bx, out Q bz);
                Q ex = bx - ax;
                Q ez = bz - az;
                Q length2 = ex * ex + ez * ez;
                Q t = Q.Of(0);
                if (length2.Sign > 0)
                    t = Q.Max(Q.Of(0), Q.Min(Q.Of(1), ((x - ax) * ex + (z - az) * ez) / length2));
                Q cx = ax + t * ex;
                Q cz = az + t * ez;
                Q d2 = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (!found || d2.CompareTo(best) < 0)
                {
                    found = true;
                    best = d2;
                    px = cx;
                    pz = cz;
                }
            }
            return best;
        }

        private static Q GapY(in Box box, Q y)
        {
            Q d = y - box.Cy;
            if (d.Sign < 0)
                d = -d;
            return d.CompareTo(box.Hy) > 0 ? d - box.Hy : Q.Of(0);
        }

        private static Q Distance2(in Box box, Q x, Q y, Q z)
        {
            Q g = GapY(box, y);
            return Closest2D(box, x, z, out _, out _) + g * g;
        }

        // Newton iteration from above; stops before the first increase, giving floor(sqrt(value)).
        internal static BigInteger FloorSqrt(BigInteger value)
        {
            if (value.Sign <= 0)
                return BigInteger.Zero;
            // Byte length bounds the bit length from above (portable to the Unity profile).
            BigInteger root = BigInteger.One << ((value.ToByteArray().Length * 8 + 1) / 2);
            while (true)
            {
                BigInteger next = (root + value / root) >> 1;
                if (next >= root)
                    return root;
                root = next;
            }
        }

        private static long FloorSqrt(Q value) => (long)FloorSqrt(BigInteger.Divide(value.N, value.D));
        private static Q P(long value) => Q.Of(value);

        internal static long ReferenceDistance(in SpatialBody body, SimVec3 point)
        {
            var box = new Box(body);
            return FloorSqrt(Distance2(box, P(point.X.Raw), P(point.Y.Raw), P(point.Z.Raw)));
        }

        internal static bool Intersects(in SpatialBody body, in SpatialRequest q, out SimFix distance)
        {
            distance = default;
            var box = new Box(body);
            Q ox = P(q.Origin.X.Raw);
            Q oy = P(q.Origin.Y.Raw);
            Q oz = P(q.Origin.Z.Raw);
            Q r = P(q.Radius.Raw);
            switch (q.Kind)
            {
                case SpatialQueryKind.Sphere:
                    return Distance2(box, ox, oy, oz).CompareTo(r * r) <= 0;
                case SpatialQueryKind.Cylinder:
                    return OverlapY(box, q) && Closest2D(box, ox, oz, out _, out _).CompareTo(r * r) <= 0;
                case SpatialQueryKind.Sector:
                    return Sector(box, q);
                case SpatialQueryKind.Capsule:
                    return Sweep(box, q);
                default:
                    return Ray(box, q, out distance);
            }
        }

        private static bool OverlapY(in Box box, in SpatialRequest q)
        {
            Q d = P(q.Origin.Y.Raw) - box.Cy;
            if (d.Sign < 0)
                d = -d;
            return d.CompareTo(box.Hy + P(q.HalfHeight.Raw)) <= 0;
        }

        // Earliest t >= 0 with origin + t*direction inside the closed box, from face-plane candidates.
        private static bool FirstEntry(in Box box, Q ox, Q oy, Q oz, Q dx, Q dy, Q dz, out Q best)
        {
            best = default;
            if (box.Contains(ox, oy, oz))
            {
                best = Q.Of(0);
                return true;
            }
            bool found = false;
            Q pu = box.ProjectU(ox, oz);
            Q pv = box.ProjectV(ox, oz);
            Q du = dx * box.Ux + dz * box.Uz;
            Q dv = dx * box.Vx + dz * box.Vz;
            Q py = oy - box.Cy;
            for (int face = 0; face < 6; face++)
            {
                Q along = face < 2 ? du : face < 4 ? dv : dy;
                if (along.Sign == 0)
                    continue;
                Q start = face < 2 ? pu : face < 4 ? pv : py;
                Q extent = face < 2 ? box.Hx : face < 4 ? box.Hz : box.Hy;
                Q t = ((face % 2 == 0 ? extent : -extent) - start) / along;
                if (t.Sign < 0 || (found && t.CompareTo(best) >= 0))
                    continue;
                if (box.Contains(ox + t * dx, oy + t * dy, oz + t * dz))
                {
                    found = true;
                    best = t;
                }
            }
            return found;
        }

        private static bool Ray(in Box box, in SpatialRequest q, out SimFix distance)
        {
            distance = default;
            Q dx = P(q.Direction.X.Raw);
            Q dy = P(q.Direction.Y.Raw);
            Q dz = P(q.Direction.Z.Raw);
            if (!FirstEntry(box, P(q.Origin.X.Raw), P(q.Origin.Y.Raw), P(q.Origin.Z.Raw), dx, dy, dz, out Q t))
                return false;
            Q travel2 = t * t * (dx * dx + dy * dy + dz * dz);
            Q range = P(q.Range.Raw);
            if (travel2.CompareTo(range * range) > 0)
                return false;
            distance = SimFix.FromRaw(FloorSqrt(travel2));
            return true;
        }

        // Candidates: both endpoints, every face-crossing time in [0,1], and for each of the 27 region
        // patterns the clamped minimizer of its quadratic. The exact minimum is among them.
        private static bool Sweep(in Box box, in SpatialRequest q)
        {
            Q ox = P(q.Origin.X.Raw);
            Q oy = P(q.Origin.Y.Raw);
            Q oz = P(q.Origin.Z.Raw);
            Q sx = P(q.Direction.X.Raw);
            Q sy = P(q.Direction.Y.Raw);
            Q sz = P(q.Direction.Z.Raw);
            Q r2 = P(q.Radius.Raw) * P(q.Radius.Raw);
            Q[] start = { box.ProjectU(ox, oz), box.ProjectV(ox, oz), oy - box.Cy };
            Q[] along = { sx * box.Ux + sz * box.Uz, sx * box.Vx + sz * box.Vz, sy };
            Q[] extent = { box.Hx, box.Hz, box.Hy };
            var candidates = new System.Collections.Generic.List<Q> { Q.Of(0), Q.Of(1) };
            for (int axis = 0; axis < 3; axis++)
                if (along[axis].Sign != 0)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Q t = (P(side) * extent[axis] - start[axis]) / along[axis];
                        if (t.Sign >= 0 && t.CompareTo(Q.Of(1)) <= 0)
                            candidates.Add(t);
                    }
            for (int pattern = 0; pattern < 27; pattern++)
            {
                Q numerator = Q.Of(0);
                Q denominator = Q.Of(0);
                int code = pattern;
                for (int axis = 0; axis < 3; axis++, code /= 3)
                {
                    int side = code % 3 - 1;
                    if (side == 0)
                        continue;
                    Q offset = start[axis] - P(side) * extent[axis];
                    numerator += offset * along[axis];
                    denominator += along[axis] * along[axis];
                }
                if (denominator.Sign == 0)
                    continue;
                Q t = Q.Max(Q.Of(0), Q.Min(Q.Of(1), -numerator / denominator));
                candidates.Add(t);
            }
            foreach (Q t in candidates)
                if (Distance2(box, ox + t * sx, oy + t * sy, oz + t * sz).CompareTo(r2) <= 0)
                    return true;
            return false;
        }

        private static bool Sector(in Box box, in SpatialRequest q)
        {
            if (!OverlapY(box, q))
                return false;
            Q ox = P(q.Origin.X.Raw);
            Q oz = P(q.Origin.Z.Raw);
            Q r = P(q.Radius.Raw);
            Q near2 = Closest2D(box, ox, oz, out Q px, out Q pz);
            if (near2.CompareTo(r * r) > 0)
                return false;
            if (near2.Sign == 0 || q.Cosine == -1000)
                return true;
            // The exact cosine wedge is homogeneous, so clear the common denominator first.
            Q fx = px - ox;
            Q fz = pz - oz;
            BigInteger vx = fx.N * fz.D;
            BigInteger vz = fz.N * fx.D;
            BigInteger dx = q.Direction.X.Raw;
            BigInteger dz = q.Direction.Z.Raw;
            BigInteger dot = vx * dx + vz * dz;
            BigInteger lhs = dot * dot * 1000000;
            BigInteger rhs = (vx * vx + vz * vz) * (dx * dx + dz * dz) * q.Cosine * q.Cosine;
            int comparison = lhs.CompareTo(rhs);
            bool inside = q.Cosine >= 0 ? dot.Sign >= 0 && comparison >= 0 : dot.Sign >= 0 || comparison <= 0;
            if (inside)
                return true;
            // Edge directions use the frozen floor(sqrt((10^6 - cos^2) * 2^64)) sine, recomputed here.
            BigInteger sin = FloorSqrt((1000000 - (BigInteger)q.Cosine * q.Cosine) * (BigInteger.One << 64));
            BigInteger cos = (BigInteger)q.Cosine << 32;
            return EdgeReaches(box, ox, oz, dx * cos - dz * sin, dz * cos + dx * sin, r)
                || EdgeReaches(box, ox, oz, dx * cos + dz * sin, dz * cos - dx * sin, r);
        }

        // Smallest s >= 0 with apex + s*e in the rectangle, from its four boundary lines; then s*|e| <= R.
        private static bool EdgeReaches(in Box box, Q ox, Q oz, BigInteger ex, BigInteger ez, Q r)
        {
            Q qx = Q.Of(ex);
            Q qz = Q.Of(ez);
            Q pu = box.ProjectU(ox, oz);
            Q pv = box.ProjectV(ox, oz);
            Q du = qx * box.Ux + qz * box.Uz;
            Q dv = qx * box.Vx + qz * box.Vz;
            bool found = false;
            Q best = default;
            for (int line = 0; line < 4; line++)
            {
                Q along = line < 2 ? du : dv;
                if (along.Sign == 0)
                    continue;
                Q start = line < 2 ? pu : pv;
                Q extent = line < 2 ? box.Hx : box.Hz;
                Q s = ((line % 2 == 0 ? extent : -extent) - start) / along;
                if (s.Sign < 0 || (found && s.CompareTo(best) >= 0))
                    continue;
                if (box.Contains2D(ox + s * qx, oz + s * qz))
                {
                    found = true;
                    best = s;
                }
            }
            return found && (best * best * (qx * qx + qz * qz)).CompareTo(r * r) <= 0;
        }
    }
}
