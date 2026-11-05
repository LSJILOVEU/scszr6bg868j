using System;

namespace WarCore.Sim
{
    // Byte-sized so the box fields fit the former padding; hashes and snapshots still write the int value.
    public enum ShapeKind : byte
    {
        VerticalCapsule,
        OrientedBox
    }

    // Box yaw as a Pythagorean generator (m, n): cos = (m^2-n^2)/c, sin = 2mn/c, c = m^2+n^2.
    // The integer rotation is exactly orthonormal; only the set of representable angles is quantized.
    // The angle is 2*atan2(n, m), matching Unity Euler(0, angle, 0): local +X maps to (cos, 0, -sin)
    // and local +Z to (sin, 0, cos). Canonical form: gcd(m, n) = 1 and m > 0, or (m, n) = (0, 1).
    public readonly struct BoxYaw : IEquatable<BoxYaw>
    {
        public const int MaxDenominator = 1 << 24;
        // Offsetting m makes default(BoxYaw) the identity generator (1, 0).
        private readonly short mMinusOne;
        private readonly short n;
        public int M => mMinusOne + 1;
        public int N => n;
        public long Cos => (long)M * M - (long)N * N;
        public long Sin => 2L * M * N;
        public long Denominator => (long)M * M + (long)N * N;
        public static BoxYaw Identity => default;

        private BoxYaw(int m, int n)
        {
            mMinusOne = (short)(m - 1);
            this.n = (short)n;
        }

        public static bool IsCanonical(int m, int n)
        {
            if (m < 0 || m > 4096 || n < -4096 || n > 4096 || (m == 0 && n != 1))
                return false;
            return (long)m * m + (long)n * n <= MaxDenominator && Gcd(m, n) == 1;
        }

        // Accepts any nonzero generator in range and reduces it; the represented angle is unchanged.
        public static BoxYaw FromGenerator(int m, int n)
        {
            if (m < -4096 || m > 4096 || n < -4096 || n > 4096 || (m == 0 && n == 0))
                throw new ArgumentOutOfRangeException(nameof(m), "Yaw generator components must be in [-4096, 4096] and not both zero.");
            int g = Gcd(m, n);
            m /= g;
            n /= g;
            if (m < 0 || (m == 0 && n < 0))
            {
                m = -m;
                n = -n;
            }
            if (!IsCanonical(m, n))
                throw new ArgumentOutOfRangeException(nameof(m), "Yaw denominator m^2+n^2 exceeds 2^24.");
            return new BoxYaw(m, n);
        }

        // Exact quarter turns: 0, 90, 180 and 270 degrees.
        public static BoxYaw QuarterTurns(int turns)
        {
            int k = turns % 4;
            if (k < 0)
                k += 4;
            return k == 0 ? Identity : k == 1 ? new BoxYaw(1, 1) : k == 2 ? new BoxYaw(0, 1) : new BoxYaw(1, -1);
        }

        private static int Gcd(int a, int b)
        {
            a = a < 0 ? -a : a;
            b = b < 0 ? -b : b;
            while (b != 0)
            {
                int r = a % b;
                a = b;
                b = r;
            }
            return a;
        }

        public bool Equals(BoxYaw other) => mMinusOne == other.mMinusOne && n == other.n;
        public override bool Equals(object obj) => obj is BoxYaw other && Equals(other);
        public override int GetHashCode() => (mMinusOne << 16) ^ (ushort)n;
    }

    public struct Position : IStateHash
    {
        private SimVec3 value;
        public SimVec3 Value
        {
            get => value;
            set
            {
                value.ValidateWorld();
                this.value = value;
            }
        }

        public Position(SimVec3 value)
        {
            value.ValidateWorld();
            this.value = value;
        }

        public void WriteState(StateHash hash)
        {
            Value.WriteState(hash);
        }
    }

    public readonly struct Collider : IStateHash
    {
        // Field order keeps the struct at 32 bytes: the box fields occupy the former padding.
        // XOR preserves bit 0 for default(Collider) while still allowing an explicit zero layer.
        private readonly uint layerXorDefault;
        private readonly int halfDepthRaw;
        private readonly BoxYaw yaw;
        private readonly ShapeKind shapeKind;
        private readonly bool isStatic;
        private readonly SimFix radius;
        private readonly SimFix height;
        // Box half extents are capped so the world AABB half width hx+hz stays within the capsule radius bound.
        internal const long BoxHalfExtentLimit = 1L << 29;
        internal const long HeightLimit = 32768L * SimFix.Scale;
        public uint Layer => layerXorDefault ^ 1u;
        public bool IsStatic => isStatic;
        public ShapeKind ShapeKind => shapeKind;
        // VerticalCapsule: radius. OrientedBox: half extent along the box-local X axis.
        public SimFix Radius => radius;
        // Full Y extent for both shapes; geometry uses floor(Height.Raw / 2) above and below the centre.
        public SimFix Height => height;
        // OrientedBox: half extent along the box-local Z axis; zero for capsules.
        public SimFix HalfDepth => SimFix.FromRaw(halfDepthRaw);
        // OrientedBox orientation; identity for capsules.
        public BoxYaw Yaw => yaw;

        // Position is the centre; Height includes both hemispheres, keeping posture data unambiguous.
        // Preserve odd raw heights. Geometry uses floor(Height.Raw / 2) at both ends so posture feet stay exact.
        public Collider(SimFix radius, SimFix height, ShapeKind shapeKind = ShapeKind.VerticalCapsule)
            : this(radius, height, 1u, shapeKind)
        {
        }

        // Capsules only; boxes are created with Collider.Box.
        public Collider(SimFix radius, SimFix height, uint layer, ShapeKind shapeKind = ShapeKind.VerticalCapsule, bool isStatic = false)
        {
            SpatialRequest.ValidateDistance(radius);
            if (height.Raw < radius.Raw * 2 || height.Raw > 32768 * SimFix.Scale || shapeKind != ShapeKind.VerticalCapsule)
                throw new ArgumentOutOfRangeException(nameof(height));
            this.radius = radius;
            this.height = height;
            this.shapeKind = shapeKind;
            layerXorDefault = layer ^ 1u;
            this.isStatic = isStatic;
            halfDepthRaw = 0;
            yaw = default;
        }

        private Collider(SimFix halfX, SimFix height, SimFix halfZ, BoxYaw yaw, uint layer, bool isStatic)
        {
            if (halfX.Raw < 0 || halfX.Raw > BoxHalfExtentLimit || halfZ.Raw < 0 || halfZ.Raw > BoxHalfExtentLimit)
                throw new ArgumentOutOfRangeException(nameof(halfX), "Box half extents must be in [0, 8192] metres.");
            if (height.Raw < 0 || height.Raw > 32768 * SimFix.Scale)
                throw new ArgumentOutOfRangeException(nameof(height));
            radius = halfX;
            this.height = height;
            halfDepthRaw = (int)halfZ.Raw;
            this.yaw = yaw;
            shapeKind = ShapeKind.OrientedBox;
            layerXorDefault = layer ^ 1u;
            this.isStatic = isStatic;
        }

        // A yawed box centred on Position: |local x| <= halfX, |local z| <= halfZ, |y| <= floor(height / 2).
        public static Collider Box(SimFix halfX, SimFix height, SimFix halfZ, BoxYaw yaw, uint layer = 1u, bool isStatic = false)
            => new Collider(halfX, height, halfZ, yaw, layer, isStatic);

        // Copies every field of an already validated collider; callers validate only what they change.
        // Tick-end synchronization calls WithLayer/WithStatic for every live body, so no shape is rebuilt here.
        private Collider(in Collider source, uint layer, bool isStatic, SimFix height)
        {
            layerXorDefault = layer ^ 1u;
            halfDepthRaw = source.halfDepthRaw;
            yaw = source.yaw;
            shapeKind = source.shapeKind;
            this.isStatic = isStatic;
            radius = source.radius;
            this.height = height;
        }

        public Collider WithLayer(uint layer) => new Collider(in this, layer, isStatic, height);
        public Collider WithStatic(bool isStatic) => new Collider(in this, Layer, isStatic, height);

        // Capsules keep height >= 2 * radius; boxes accept any height up to the shared limit.
        public Collider WithHeight(SimFix height)
        {
            long minimum = shapeKind == ShapeKind.VerticalCapsule ? radius.Raw * 2 : 0;
            if (height.Raw < minimum || height.Raw > 32768 * SimFix.Scale)
                throw new ArgumentOutOfRangeException(nameof(height));
            return new Collider(in this, Layer, isStatic, height);
        }

        // Half extents of the world-axis bounding box; box widths are rounded outward.
        public SimVec3 WorldHalfExtents
        {
            get
            {
                long x = SpatialBox.ExtentX(in this);
                long z = SpatialBox.ExtentZ(in this);
                return new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(height.Raw / 2), SimFix.FromRaw(z));
            }
        }

        public void WriteState(StateHash hash)
        {
            hash.Add((int)ShapeKind);
            Radius.WriteState(hash);
            Height.WriteState(hash);
            if (!hash.IgnoreColliderLayers)
                hash.Add((ulong)Layer);
            if (!hash.IgnoreColliderStatic)
                hash.Add(IsStatic);
            // Capsule bytes are unchanged; only boxes append their extra fields.
            if (shapeKind == ShapeKind.OrientedBox)
            {
                // The legacy compatibility encodings never defined box bytes.
                if (hash.IgnoreColliderLayers || hash.IgnoreColliderStatic)
                    throw new SimBoundaryException("M0C_SHAPE_LEGACY_HASH");
                hash.Add((long)halfDepthRaw);
                hash.Add(yaw.M);
                hash.Add(yaw.N);
            }
        }
    }

    public readonly struct SpatialBody
    {
        public EntityId Entity
        {
            get;
        }

        public SimVec3 Position
        {
            get;
        }

        public Collider Collider
        {
            get;
        }

        public SpatialBody(EntityId entity, SimVec3 position, Collider collider)
        {
            position.ValidateWorld();
            if (entity.Id < 0 || entity.Generation == 0)
                throw new ArgumentException("A valid entity is required.");
            Entity = entity;
            Position = position;
            Collider = collider;
        }

        // Index columns contain already validated bodies; assembling a value cannot change them.
        internal static SpatialBody FromValidated(EntityId entity, SimVec3 position, Collider collider)
            => new SpatialBody(entity, position, collider, true);

        private SpatialBody(EntityId entity, SimVec3 position, Collider collider, bool validated)
        {
            Entity = entity;
            Position = position;
            Collider = collider;
        }
    }

    public readonly struct RayHit
    {
        public EntityId Entity
        {
            get;
        }

        public SimFix Distance
        {
            get;
        }

        public RayHit(EntityId entity, SimFix distance)
        {
            Entity = entity;
            Distance = distance;
        }
    }

    public interface ISpatialQuery
    {
        int Sphere(SimVec3 center, SimFix radius, Span<EntityId> results);
        int Cylinder(SimVec3 center, SimFix radius, SimFix halfHeight, Span<EntityId> results);
        int Sector(SimVec3 center, SimFix radius, SimVec3 direction, int cosineHalfAnglePermille, SimFix halfHeight, Span<EntityId> results);
        int Capsule(SimVec3 start, SimVec3 end, SimFix radius, Span<EntityId> results);
        int Ray(SimVec3 origin, SimVec3 direction, SimFix maxDistance, Span<RayHit> results);
        int Sphere(SimVec3 center, SimFix radius, uint layerMask, Span<EntityId> results);
        int Cylinder(SimVec3 center, SimFix radius, SimFix halfHeight, uint layerMask, Span<EntityId> results);
        int Sector(SimVec3 center, SimFix radius, SimVec3 direction, int cosineHalfAnglePermille, SimFix halfHeight, uint layerMask, Span<EntityId> results);
        int Capsule(SimVec3 start, SimVec3 end, SimFix radius, uint layerMask, Span<EntityId> results);
        int Ray(SimVec3 origin, SimVec3 direction, SimFix maxDistance, uint layerMask, Span<RayHit> results);
    }

    public enum SpatialQueryKind
    {
        Sphere,
        Cylinder,
        Sector,
        Capsule,
        Ray
    }

    public readonly struct SpatialRequest
    {
        // The complemented representation also makes a zero-initialized request select all layers.
        private readonly uint excludedLayers;
        public uint LayerMask => ~excludedLayers;

        public SpatialQueryKind Kind
        {
            get;
        }

        public SimVec3 Origin
        {
            get;
        }

        public SimVec3 Direction
        {
            get;
        }

        public SimVec3 Minimum
        {
            get;
        }

        public SimVec3 Maximum
        {
            get;
        }

        public SimFix Radius
        {
            get;
        }

        public SimFix Range
        {
            get;
        }

        public SimFix HalfHeight
        {
            get;
        }

        public int Cosine
        {
            get;
        }

        internal SpatialRequest(SpatialQueryKind kind, SimVec3 origin, SimFix radius, SimVec3 direction, SimFix range, SimFix halfHeight, int cosine, uint layerMask = uint.MaxValue)
        {
            origin.ValidateWorld();
            ValidateDistance(radius);
            ValidateDistance(range);
            ValidateDistance(halfHeight);
            Kind = kind;
            Origin = origin;
            Radius = radius;
            Direction = direction;
            Range = range;
            HalfHeight = halfHeight;
            Cosine = cosine;
            excludedLayers = ~layerMask;
            SimFix extent = kind == SpatialQueryKind.Ray ? range : radius;
            SimFix y = kind == SpatialQueryKind.Cylinder || kind == SpatialQueryKind.Sector ? halfHeight : extent;
            SimVec3 end = kind == SpatialQueryKind.Capsule ? origin + direction : origin;
            Minimum = new SimVec3(SimFix.FromRaw(SimLimits.Min(origin.X.Raw, end.X.Raw) - extent.Raw), SimFix.FromRaw(SimLimits.Min(origin.Y.Raw, end.Y.Raw) - y.Raw), SimFix.FromRaw(SimLimits.Min(origin.Z.Raw, end.Z.Raw) - extent.Raw));
            Maximum = new SimVec3(SimFix.FromRaw(SimLimits.Max(origin.X.Raw, end.X.Raw) + extent.Raw), SimFix.FromRaw(SimLimits.Max(origin.Y.Raw, end.Y.Raw) + y.Raw), SimFix.FromRaw(SimLimits.Max(origin.Z.Raw, end.Z.Raw) + extent.Raw));
        }

        public static void ValidateDistance(SimFix value)
        {
            SpatialWidth.AssertWorldLimit(SimVec3.WorldLimitMetres);
            if (value.Raw < 0 || value.Raw > SimVec3.WorldLimitMetres * SimFix.Scale)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    // Public validation is shared; the test provider keeps an independent geometric oracle.
    public abstract class SpatialQueryBase : ISpatialQuery
    {
        protected SpatialQueryBase()
        {
            GeometrySinTable.Initialize();
        }

        protected abstract int Execute(SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits);
        public int Sphere(SimVec3 center, SimFix radius, Span<EntityId> results) => Sphere(center, radius, uint.MaxValue, results);
        public int Cylinder(SimVec3 center, SimFix radius, SimFix halfHeight, Span<EntityId> results) => Cylinder(center, radius, halfHeight, uint.MaxValue, results);
        public int Sector(SimVec3 center, SimFix radius, SimVec3 direction, int cosineHalfAnglePermille, SimFix halfHeight, Span<EntityId> results) => Sector(center, radius, direction, cosineHalfAnglePermille, halfHeight, uint.MaxValue, results);
        public int Capsule(SimVec3 start, SimVec3 end, SimFix radius, Span<EntityId> results) => Capsule(start, end, radius, uint.MaxValue, results);
        public int Ray(SimVec3 origin, SimVec3 direction, SimFix maxDistance, Span<RayHit> results) => Ray(origin, direction, maxDistance, uint.MaxValue, results);

        public int Sphere(SimVec3 center, SimFix radius, uint layerMask, Span<EntityId> results) => Execute(new SpatialRequest(SpatialQueryKind.Sphere, center, radius, default, default, default, 0, layerMask), results, default);
        public int Cylinder(SimVec3 center, SimFix radius, SimFix halfHeight, uint layerMask, Span<EntityId> results) => Execute(new SpatialRequest(SpatialQueryKind.Cylinder, center, radius, default, default, halfHeight, 0, layerMask), results, default);
        public int Sector(SimVec3 center, SimFix radius, SimVec3 direction, int cosineHalfAnglePermille, SimFix halfHeight, uint layerMask, Span<EntityId> results)
        {
            direction.ValidateWorld();
            if (direction.Y.Raw != 0 || (direction.X.Raw == 0 && direction.Z.Raw == 0))
                throw new ArgumentException("Sector direction must be horizontal and nonzero.");
            if (cosineHalfAnglePermille < -1000 || cosineHalfAnglePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(cosineHalfAnglePermille));
            return Execute(new SpatialRequest(SpatialQueryKind.Sector, center, radius, direction, default, halfHeight, cosineHalfAnglePermille, layerMask), results, default);
        }

        public int Capsule(SimVec3 start, SimVec3 end, SimFix radius, uint layerMask, Span<EntityId> results)
        {
            start.ValidateWorld();
            end.ValidateWorld();
            SimVec3 delta = end - start;
            ulong limit = SimVec3.WorldLimitMetres * SimFix.Scale;
            if (delta.LengthSquared.Raw.Magnitude.CompareTo(FixedUInt128.Multiply(limit, limit)) > 0)
                throw new ArgumentOutOfRangeException(nameof(end), "Sweep length exceeds 16384 metres.");
            return Execute(new SpatialRequest(SpatialQueryKind.Capsule, start, radius, delta, default, default, 0, layerMask), results, default);
        }

        public int Ray(SimVec3 origin, SimVec3 direction, SimFix maxDistance, uint layerMask, Span<RayHit> results)
        {
            direction.ValidateWorld();
            if (direction.LengthSquared.Sign == 0)
                throw new ArgumentException("Ray direction must be nonzero.");
            return Execute(new SpatialRequest(SpatialQueryKind.Ray, origin, default, direction, maxDistance, default, 0, layerMask), default, results);
        }

        protected internal static bool Add(in SpatialBody body, in SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits, ref int count)
        {
            if (!SpatialGeometry.Intersects(body, request, out SimFix distance, out bool enteredNarrowPhase))
                return enteredNarrowPhase;
            if (count == (request.Kind == SpatialQueryKind.Ray ? hits.Length : entities.Length))
                throw new ArgumentException("Query output buffer is too small.");
            if (request.Kind == SpatialQueryKind.Ray)
                hits[count] = new RayHit(body.Entity, distance);
            else
                entities[count] = body.Entity;
            count++;
            return enteredNarrowPhase;
        }

        protected internal static void Sort(in SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits, int count, EntityStore store = null, Span<ulong> sortKeys = default)
        {
            if (store != null && store.IdentityMode == IdentityMode.StableWorld && sortKeys.Length >= count)
            {
                // Copy each key once; heap comparisons then read adjacent scratch columns instead of random entity slots.
                if (request.Kind == SpatialQueryKind.Ray)
                {
                    for (int i = 0; i < count; i++)
                        sortKeys[i] = Key(store,hits[i].Entity);
                    SortStableRays(hits,sortKeys,count);
                }
                else
                {
                    for (int i = 0; i < count; i++)
                        sortKeys[i] = Key(store,entities[i]);
                    SortStableEntities(entities,sortKeys,count);
                }
                return;
            }
            // In-place heapsort bounds work even when a large query returns most of the world.
            for (int root = count / 2 - 1; root >= 0; root--)
                Sift(request, entities, hits, root, count, store);
            for (int end = count - 1; end > 0; end--)
            {
                Swap(request, entities, hits, 0, end);
                Sift(request, entities, hits, 0, end, store);
            }
        }

        private static void SortStableEntities(Span<EntityId> entities, Span<ulong> keys, int count)
        {
            for (int root = count/2-1; root >= 0; root--)
                SiftStableEntity(entities,keys,root,count,entities[root],keys[root]);
            for (int end = count-1; end > 0; end--)
            {
                EntityId value = entities[end];
                ulong key = keys[end];
                entities[end] = entities[0];
                keys[end] = keys[0];
                SiftStableEntity(entities,keys,0,end,value,key);
            }
        }
        private static void SiftStableEntity(Span<EntityId> entities, Span<ulong> keys, int root, int count, EntityId value, ulong key)
        {
            // Move children into the hole instead of swapping both records at every level.
            while (root < count/2)
            {
                int child = root*2+1;
                if (child+1 < count && keys[child] < keys[child+1])
                    child++;
                if (key >= keys[child])
                    break;
                entities[root] = entities[child];
                keys[root] = keys[child];
                root = child;
            }
            entities[root] = value;
            keys[root] = key;
        }
        private static void SortStableRays(Span<RayHit> hits, Span<ulong> keys, int count)
        {
            for (int root = count/2-1; root >= 0; root--)
                SiftStableRay(hits,keys,root,count,hits[root],keys[root]);
            for (int end = count-1; end > 0; end--)
            {
                RayHit value = hits[end];
                ulong key = keys[end];
                hits[end] = hits[0];
                keys[end] = keys[0];
                SiftStableRay(hits,keys,0,end,value,key);
            }
        }
        private static bool RayBefore(RayHit a, ulong aKey, RayHit b, ulong bKey)
            => a.Distance.Raw < b.Distance.Raw || (a.Distance.Raw == b.Distance.Raw && aKey < bKey);
        private static void SiftStableRay(Span<RayHit> hits, Span<ulong> keys, int root, int count, RayHit value, ulong key)
        {
            while (root < count/2)
            {
                int child = root*2+1;
                if (child+1 < count && RayBefore(hits[child],keys[child],hits[child+1],keys[child+1]))
                    child++;
                if (!RayBefore(value,key,hits[child],keys[child]))
                    break;
                hits[root] = hits[child];
                keys[root] = keys[child];
                root = child;
            }
            hits[root] = value;
            keys[root] = key;
        }

        private static int Compare(in SpatialRequest q, Span<EntityId> entities, Span<RayHit> hits, int a, int b, EntityStore store)
        {
            if (q.Kind != SpatialQueryKind.Ray)
                return Key(store, entities[a]).CompareTo(Key(store, entities[b]));
            int distance = hits[a].Distance.CompareTo(hits[b].Distance);
            return distance == 0 ? Key(store, hits[a].Entity).CompareTo(Key(store, hits[b].Entity)) : distance;
        }

        private static ulong Key(EntityStore store, EntityId entity) => store == null ? (ulong)entity.Id : store.StableAt(entity.Id).Value;

        private static void Swap(in SpatialRequest q, Span<EntityId> entities, Span<RayHit> hits, int a, int b)
        {
            if (q.Kind == SpatialQueryKind.Ray)
            {
                RayHit value = hits[a];
                hits[a] = hits[b];
                hits[b] = value;
            }
            else
            {
                EntityId value = entities[a];
                entities[a] = entities[b];
                entities[b] = value;
            }
        }

        private static void Sift(in SpatialRequest q, Span<EntityId> entities, Span<RayHit> hits, int root, int count, EntityStore store)
        {
            while (root < count / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && Compare(q, entities, hits, child, child + 1, store) < 0)
                    child++;
                if (Compare(q, entities, hits, root, child, store) >= 0)
                    break;
                Swap(q, entities, hits, root, child);
                root = child;
            }
        }
    }
}
