using System;
using System.Reflection;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal sealed class A2SpatialInputs : SpatialQueryBase
    {
        internal const long World = 1L << 30;
        internal delegate bool Predicate(in SpatialBody body, in SpatialRequest request, out SimFix distance);
        internal delegate bool BoundsPredicate(in SpatialBody body, in SpatialRequest request);

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

        private SpatialRequest captured;
        private A1MathInputs random;

        internal A2SpatialInputs(ulong seed)
        {
            random = new A1MathInputs(seed);
        }

        internal static Predicate Product()
        {
            Type type = typeof(SimFix).Assembly.GetType("WarCore.Sim.SpatialGeometry", true);
            return (Predicate)type.GetMethod("Intersects", BindingFlags.Static | BindingFlags.Public).CreateDelegate(typeof(Predicate));
        }

        internal static BoundsPredicate Bounds()
        {
            Type type = typeof(SimFix).Assembly.GetType("WarCore.Sim.SpatialBounds", true);
            return (BoundsPredicate)type.GetMethod("RayMayHit", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(BoundsPredicate));
        }

        protected override int Execute(SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits)
        {
            captured = request;
            return 0;
        }

        internal static SimVec3 V(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        private long Signed(long limit) => (long)(random.Next() % (ulong)(limit * 2 + 1)) - limit;
        private long Positive(long limit) => (long)(random.Next() % (ulong)(limit + 1));

        internal Sample Make(SpatialQueryKind kind, SimVec3 position, long bodyRadius, long height, SimVec3 origin, SimVec3 direction, long radius, long range, long halfHeight, int cosine, string source)
        {
            var body = new SpatialBody(new EntityId(0, 1), position, new Collider(SimFix.FromRaw(bodyRadius), SimFix.FromRaw(height)));
            // Every request passes the existing public entry point, including capsule length validation.
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    Sphere(origin, SimFix.FromRaw(radius), default);
                    break;
                case SpatialQueryKind.Cylinder:
                    Cylinder(origin, SimFix.FromRaw(radius), SimFix.FromRaw(halfHeight), default);
                    break;
                case SpatialQueryKind.Sector:
                    Sector(origin, SimFix.FromRaw(radius), direction, cosine, SimFix.FromRaw(halfHeight), default);
                    break;
                case SpatialQueryKind.Capsule:
                    Capsule(origin, origin + direction, SimFix.FromRaw(radius), default);
                    break;
                default:
                    Ray(origin, direction, SimFix.FromRaw(range), default);
                    break;
            }

            return new Sample(body, captured, source);
        }

        internal Sample Vector(VectorJson row)
        {
            VectorJson b = row["body"];
            VectorJson q = row["query"];
            SpatialQueryKind kind = (SpatialQueryKind)Enum.Parse(typeof(SpatialQueryKind), row["kind"].Text, true);
            return Make(kind, MathVectorChecks.Vector(b["position"].Integers()), b["radius"].Integer, b["height"].Integer,
                MathVectorChecks.Vector(q["origin"].Integers()), MathVectorChecks.Vector(q["direction"].Integers()),
                q["radius"].Integer, q["range"].Integer, q["halfHeight"].Integer, (int)q["cosinePermille"].Integer, "vector:" + row["note"].Text);
        }

        private long Edge()
        {
            switch (random.Next() % 7)
            {
                case 0:
                    return -World;
                case 1:
                    return -World + 1;
                case 2:
                    return World;
                case 3:
                    return World - 1;
                default:
                    return Signed(1);
            }
        }

        internal Sample Random(SpatialQueryKind kind, int index)
        {
            int group = index % 4;
            if (group == 3)
                return Tangent(kind, index);
            long size = group == 0 ? 32 * 65536 : World;
            SimVec3 position = group == 2 ? V(Edge(), Edge(), Edge()) : V(Signed(size), Signed(size), Signed(size));
            SimVec3 origin = group == 2 ? V(Edge(), Edge(), Edge()) : V(Signed(size), Signed(size), Signed(size));
            long bodyRadius = Positive(size);
            long half = bodyRadius + Positive(size - bodyRadius);
            long height = half * 2;
            if (half < World && index % 3 == 0)
                height++;
            long radius = Positive(size);
            long halfHeight = Positive(size);
            long range = Positive(size);
            SimVec3 direction = V(Signed(size), kind == SpatialQueryKind.Sector ? 0 : Signed(size), Signed(size));
            if (direction.Equals(default(SimVec3)))
                direction = V(1, 0, 0);
            if (kind == SpatialQueryKind.Capsule)
            {
                long dx = Signed(size / 2);
                long dy = Signed(size / 2);
                long dz = Signed(size / 2);
                SimVec3 end = V(Math.Max(-World, Math.Min(World, origin.X.Raw + dx)),
                    Math.Max(-World, Math.Min(World, origin.Y.Raw + dy)), Math.Max(-World, Math.Min(World, origin.Z.Raw + dz)));
                direction = end - origin;
            }

            int cosine = (int)Signed(1000);
            return Make(kind, position, bodyRadius, height, origin, direction, radius, range, halfHeight, cosine,
                group == 0 ? "local" : group == 1 ? "world" : "world-edge");
        }

        private Sample Tangent(SpatialQueryKind kind, int index)
        {
            long k = 1 + Positive(World / 32 - 1);
            long margin = index / 4 % 3 - 1;
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    return Make(kind, V(3 * k + margin, 0, 4 * k), 2 * k, 4 * k, default, default, 3 * k, 0, 0, 0, "tangent+-1");
                case SpatialQueryKind.Cylinder:
                    return Make(kind, default, 5 * k, 10 * k, V(7 * k + margin, 6 * k, 0), default, 4 * k, 0, 2 * k, 0, "tangent+-1");
                case SpatialQueryKind.Sector:
                    long a = 1 + Positive(World / 32 - 1);
                    return Make(kind, V(3 * a - 4 * k, 0, 4 * a + 3 * k), 5 * k + margin, 2 * (5 * k + margin), default, V(World, 0, 0), 20 * a, 0, 0, 600, "tangent+-1");
                case SpatialQueryKind.Capsule:
                    return Make(kind, V(13 * k + margin, 0, 4 * k), 0, 0, default, V(10 * k, 0, 0), 5 * k, 0, 0, 0, "tangent+-1");
                default:
                    return Make(kind, V(10 * k, 0, k + margin), k, 2 * k, default, V((index & 4) == 0 ? 1 : World, 0, 0), 0, 20 * k, 0, 0, "tangent+-1");
            }
        }
    }
}
