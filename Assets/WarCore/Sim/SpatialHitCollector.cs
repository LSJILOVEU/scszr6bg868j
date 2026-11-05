using System;

namespace WarCore.Sim
{
    internal enum SpatialResultMode
    {
        Any,
        Nearest,
        AllCapped
    }

    // The caller's span is the bounded max heap; no scratch capacity depends on the hit count.
    internal ref struct SpatialHitCollector
    {
        private readonly SpatialResultMode mode;
        private readonly EntityStore store;
        private readonly Span<ulong> keys;
        private ulong bestKey;
        // Keep the scalar beside the key: copying Best for every rejected hit is costly on Mono.
        private long bestDistance;
        private readonly SimVec3 referencePoint;
        private readonly Span<SpatialHit> output;
        private readonly int capacity;
        private long totalHits;
        private long written;
        // Deduplication and the int-sized store/span bound both narrowings, with wide counter arithmetic.
        internal int TotalHits => SimInteger.ToInt32(totalHits);
        internal int Written => SimInteger.ToInt32(written);
        internal SpatialHit Best { get; private set; }
        internal bool UseStableOrder => mode == SpatialResultMode.AllCapped && store != null;
        internal bool HasBest => mode == SpatialResultMode.Nearest && TotalHits != 0;

        internal SpatialHitCollector(SpatialResultMode mode, SimVec3 referencePoint, Span<SpatialHit> output, int k, EntityStore store = null, Span<ulong> sortKeys = default)
        {
            this.mode = mode;
            // Any exposes no identity and can share the legacy scalar path.
            this.store = mode != SpatialResultMode.Any && store != null && store.IdentityMode == IdentityMode.StableWorld ? store : null;
            keys = this.store == null ? default : sortKeys;
            bestKey = 0;
            bestDistance = 0;
            this.referencePoint = referencePoint;
            this.output = output;
            capacity = k < output.Length ? k : output.Length;
            totalHits = 0;
            written = 0;
            Best = default;
        }

        // The small dispatch can inline on both managed and AOT runtimes.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal bool Add(in SpatialBody body, in SpatialRequest request, out bool enteredNarrowPhase)
            => keys.IsEmpty ? AddLegacy(in body,in request,out enteredNarrowPhase)
                : AddStable(in body,in request,out enteredNarrowPhase);

        private bool AddLegacy(in SpatialBody body, in SpatialRequest request, out bool enteredNarrowPhase)
        {
            if (!SpatialGeometry.Intersects(in body, in request, out SimFix distance, out enteredNarrowPhase))
                return false;
            totalHits++;
            if (mode == SpatialResultMode.Any)
                return true;
            if (request.Kind != SpatialQueryKind.Ray)
                distance = SpatialModeMath.ReferenceDistance(in body, referencePoint);
            var hit = new SpatialHit(body.Entity, distance);
            if (mode == SpatialResultMode.Nearest)
            {
                if (TotalHits == 1 || Compare(hit, Best) < 0)
                    Best = hit;
                return false;
            }
            if (capacity == 0)
                return false;
            if (Written < capacity)
            {
                int child = Written;
                written++;
                output[child] = hit;
                while (child > 0)
                {
                    int parent = (int)(((long)child - 1) / 2);
                    if (Compare(output[parent], output[child]) >= 0)
                        break;
                    Swap(parent, child);
                    child = parent;
                }
            }
            else if (Compare(hit, output[0]) < 0)
            {
                output[0] = hit;
                Sift(0, Written);
            }
            return false;
        }

        internal SpatialQueryResult Finish()
        {
            if (!keys.IsEmpty)
                return FinishStable();
            for (long end = written - 1; end > 0; end--)
            {
                Swap(0, (int)end);
                Sift(0, (int)end);
            }
            return new SpatialQueryResult(Written, TotalHits);
        }

        private bool AddStable(in SpatialBody body, in SpatialRequest request, out bool enteredNarrowPhase)
        {
            if (!SpatialGeometry.Intersects(in body,in request,out SimFix distance,out enteredNarrowPhase))
                return false;
            totalHits++;
            if (mode == SpatialResultMode.Any)
                return true;
            if (request.Kind != SpatialQueryKind.Ray)
                distance = SpatialModeMath.ReferenceDistance(in body,referencePoint);
            var hit = new SpatialHit(body.Entity,distance);
            if (mode == SpatialResultMode.Nearest)
            {
                if (totalHits == 1 || distance.Raw <= bestDistance)
                {
                    ulong nearestKey = Key(body.Entity);
                    if (totalHits == 1 || distance.Raw < bestDistance || nearestKey < bestKey)
                    {
                        Best = hit;
                        bestKey = nearestKey;
                        bestDistance = distance.Raw;
                    }
                }
                return false;
            }
            if (capacity == 0)
                return false;
            ulong key = Key(body.Entity);
            if (Written < capacity)
            {
                int child = Written;
                written++;
                while (child > 0)
                {
                    int parent = (child-1)/2;
                    if (!BeforeStable(output[parent],keys[parent],hit,key))
                        break;
                    output[child] = output[parent];
                    keys[child] = keys[parent];
                    child = parent;
                }
                output[child] = hit;
                keys[child] = key;
            }
            else if (BeforeStable(hit,key,output[0],keys[0]))
                SiftStable(0,Written,hit,key);
            return false;
        }
        private static bool BeforeStable(SpatialHit a, ulong aKey, SpatialHit b, ulong bKey)
            => a.Distance.Raw < b.Distance.Raw || (a.Distance.Raw == b.Distance.Raw && aKey < bKey);
        private void SiftStable(int root, int count, SpatialHit value, ulong key)
        {
            while (root < count/2)
            {
                int child = root*2+1;
                if (child+1 < count && BeforeStable(output[child],keys[child],output[child+1],keys[child+1]))
                    child++;
                if (!BeforeStable(value,key,output[child],keys[child]))
                    break;
                output[root] = output[child];
                keys[root] = keys[child];
                root = child;
            }
            output[root] = value;
            keys[root] = key;
        }
        private SpatialQueryResult FinishStable()
        {
            for (int end = Written-1; end > 0; end--)
            {
                SpatialHit value = output[end];
                ulong key = keys[end];
                output[end] = output[0];
                keys[end] = keys[0];
                SiftStable(0,end,value,key);
            }
            return new SpatialQueryResult(Written,TotalHits);
        }

        private int Compare(SpatialHit a, SpatialHit b)
        {
            int distance = a.Distance.Raw.CompareTo(b.Distance.Raw);
            return distance == 0 ? Key(a.Entity).CompareTo(Key(b.Entity)) : distance;
        }

        private ulong Key(EntityId entity) => store == null ? (ulong)entity.Id : store.StableAt(entity.Id).Value;

        private void Swap(int a, int b)
        {
            SpatialHit value = output[a];
            output[a] = output[b];
            output[b] = value;
        }

        private void Sift(int root, int count)
        {
            // Test parenthood before multiplication so even an int-sized span cannot overflow.
            while (root < count / 2)
            {
                int child = (int)(root * 2L + 1);
                int right = (int)((long)child + 1);
                if (right < count && Compare(output[child], output[right]) < 0)
                    child = right;
                if (Compare(output[root], output[child]) >= 0)
                    return;
                Swap(root, child);
                root = child;
            }
        }
    }
}
