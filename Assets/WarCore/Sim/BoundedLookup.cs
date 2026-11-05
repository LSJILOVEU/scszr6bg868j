using System;

namespace WarCore.Sim
{
    // Fixed storage and backshift deletion keep repeated insert/remove cycles bounded.
    internal sealed class BoundedLookup<T> where T : unmanaged, IEquatable<T>
    {
        private readonly T[] keys;
        private readonly int[] values;
        private readonly bool[] used;
        private readonly int mask;
        private readonly int capacity;
        private int count;
        internal BoundedLookup(int capacity)
        {
            if (capacity < 1 || capacity > (1 << 27))
                throw new SimBoundaryException("Lookup capacity must be within 1..134217728.");
            this.capacity = capacity;
            int length = 2;
            while (length / 2 < capacity)
                length *= 2;
            keys = new T[length];
            values = new int[length];
            used = new bool[length];
            mask = length - 1;
        }
        private int Bucket(T key) => (int)(unchecked((uint)key.GetHashCode()) & (uint)mask);
        internal int Find(T key)
        {
            int bucket = Bucket(key);
            while (used[bucket])
            {
                if (keys[bucket].Equals(key))
                    return values[bucket];
                bucket = (bucket + 1) & mask;
            }
            return -1;
        }
        internal void Add(T key, int value)
        {
            if (count == capacity)
                throw new SimBoundaryException("Fixed lookup capacity exhausted.");
            int bucket = Bucket(key);
            while (used[bucket])
            {
                if (keys[bucket].Equals(key))
                    throw new SimBoundaryException("Duplicate lookup key.");
                bucket = (bucket + 1) & mask;
            }
            keys[bucket] = key;
            values[bucket] = value;
            used[bucket] = true;
            count++;
        }
        internal void Remove(T key)
        {
            int hole = Bucket(key);
            while (used[hole] && !keys[hole].Equals(key))
                hole = (hole + 1) & mask;
            if (!used[hole])
                return;
            int next = (hole + 1) & mask;
            while (used[next])
            {
                int home = Bucket(keys[next]);
                if (((next - home + used.Length) & mask) >= ((next - hole + used.Length) & mask))
                {
                    keys[hole] = keys[next];
                    values[hole] = values[next];
                    hole = next;
                }
                next = (next + 1) & mask;
            }
            used[hole] = false;
            keys[hole] = default;
            count--;
        }
        internal void Clear()
        {
            Array.Clear(used, 0, used.Length);
            count = 0;
        }
    }

    internal static class ValueOrder
    {
        internal static void Sort<T>(Span<T> items) where T : struct, IComparable<T>
        {
            for (int root = items.Length / 2 - 1; root >= 0; root--)
                Sift(items, root, items.Length);
            for (int end = items.Length - 1; end > 0; end--)
            {
                Swap(items, 0, end);
                Sift(items, 0, end);
            }
        }
        private static void Sift<T>(Span<T> items, int root, int length) where T : struct, IComparable<T>
        {
            while (root < length / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < length && items[child].CompareTo(items[child + 1]) < 0)
                    child++;
                if (items[root].CompareTo(items[child]) >= 0)
                    return;
                Swap(items, root, child);
                root = child;
            }
        }
        private static void Swap<T>(Span<T> items, int a, int b) where T : struct
        {
            T value = items[a];
            items[a] = items[b];
            items[b] = value;
        }
    }
}
