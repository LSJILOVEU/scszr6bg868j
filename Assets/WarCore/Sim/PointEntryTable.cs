using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // TRV-7a: an entry of the map as one point sees it. Inner: the side nearer the point (by walking cost), outer the other.
    public readonly struct PointEntry
    {
        public readonly int Entry;
        public readonly int InnerNode, OuterNode, InnerLayer, OuterLayer;
        public readonly long InnerCost, OuterCost;
        public readonly SimVec3 InnerPoint, OuterPoint;

        public PointEntry(int entry, int innerNode, int outerNode, int innerLayer, int outerLayer, long innerCost, long outerCost,
            SimVec3 innerPoint, SimVec3 outerPoint)
        {
            Entry = entry; InnerNode = innerNode; OuterNode = outerNode; InnerLayer = innerLayer; OuterLayer = outerLayer;
            InnerCost = innerCost; OuterCost = outerCost; InnerPoint = innerPoint; OuterPoint = outerPoint;
        }
    }

    // TRV-7a: a cell to stand on to watch an entry. Point: the cell's middle on its layer (EntryTable.Centre). SeesOuter: from
    // there the entry's outer side is seen too.
    public readonly struct WatchCell
    {
        public readonly int Node, Layer;
        public readonly SimVec3 Point;
        public readonly bool SeesOuter;

        public WatchCell(int node, int layer, SimVec3 point, bool seesOuter)
        {
            Node = node; Layer = layer; Point = point; SeesOuter = seesOuter;
        }
    }

    // TRV-7a (K19): for each point (by its index in the match's points), the entries that belong to it and for each entry up to
    // `keep` watch cells. Built once from the walkable graph, the entry table, one walking-cost table per point and a sight test;
    // never changed and never saved or hashed.
    //
    // An entry belongs to a point when both side representatives reach the point at different costs and the nearer (inner) one
    // costs at most `reach`; ordered by (inner cost, entry number).
    //
    // A watch cell of an entry is a walkable node w that reaches the point at a cost of at most `reach` and below the outer
    // representative's cost (the point's side), whose cell middle is 2 to 10 m (both included) across the ground from the inner
    // representative's cell middle, that is not one of the entry's own nodes, and from which the inner representative is seen:
    // sees(middle of w, middle of inner). Order: the ones that also see the outer representative first, then the ones whose
    // distance (rounded down to whole raw) is nearer 5 m, then the smaller node. sees takes two points on the ground; lifting
    // them to the eye is the caller's (CombatSystem.Sees lifts both by the pack's eye height).
    public sealed class PointEntryTable
    {
        public const long MinRaw = 2 * SimFix.Scale, MaxRaw = 10 * SimFix.Scale, PreferRaw = 5 * SimFix.Scale;
        private const int Box = 10;

        private readonly PointEntry[][] points;
        private readonly WatchCell[][][] watches;

        public EntryTable Entries { get; }
        // How many times sees was asked while building.
        public long SightChecks { get; }
        public int PointCount => points.Length;

        private PointEntryTable(EntryTable entries, PointEntry[][] points, WatchCell[][][] watches, long sightChecks)
        {
            Entries = entries; this.points = points; this.watches = watches; SightChecks = sightChecks;
        }

        // reaches: by point index, null where nobody reaches the point (then it has no entries).
        public static PointEntryTable Build(LayeredNav nav, EntryTable entries, IReadOnlyList<LayeredReach> reaches, long reach, int keep,
            Func<SimVec3, SimVec3, bool> sees)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (reaches == null) throw new ArgumentNullException(nameof(reaches));
            if (sees == null) throw new ArgumentNullException(nameof(sees));
            if (reach < 0) throw new ArgumentOutOfRangeException(nameof(reach));
            if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));
            var points = new PointEntry[reaches.Count][];
            var watches = new WatchCell[reaches.Count][][];
            long checks = 0;
            for (int p = 0; p < reaches.Count; p++)
            {
                LayeredReach table = reaches[p];
                points[p] = table == null ? new PointEntry[0] : Of(nav, entries, table, reach);
                watches[p] = new WatchCell[points[p].Length][];
                for (int k = 0; k < points[p].Length; k++)
                    watches[p][k] = Watch(nav, entries, table, reach, keep, points[p][k], sees, ref checks);
            }
            return new PointEntryTable(entries, points, watches, checks);
        }

        public int EntryCount(int point) => points[point].Length;

        public PointEntry Entry(int point, int k) => points[point][k];

        public int WatchCount(int point, int k) => watches[point][k].Length;

        public WatchCell Watch(int point, int k, int j) => watches[point][k][j];

        // The entries that belong to the point whose cost table is given.
        public static PointEntry[] Of(LayeredNav nav, EntryTable entries, LayeredReach table, long reach)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (table == null) throw new ArgumentNullException(nameof(table));
            var list = new List<PointEntry>();
            for (int e = 0; e < entries.Count; e++)
            {
                EntryInfo info = entries[e];
                long a = table.CostFrom(info.NodeA), b = table.CostFrom(info.NodeB);
                if (a < 0 || b < 0 || a == b) continue;
                bool aInner = a < b;
                long inner = aInner ? a : b;
                if (inner > reach) continue;
                list.Add(aInner
                    ? new PointEntry(e, info.NodeA, info.NodeB, info.LayerA, info.LayerB, a, b, info.PointA, info.PointB)
                    : new PointEntry(e, info.NodeB, info.NodeA, info.LayerB, info.LayerA, b, a, info.PointB, info.PointA));
            }
            list.Sort((x, y) => x.InnerCost != y.InnerCost ? x.InnerCost.CompareTo(y.InnerCost) : x.Entry.CompareTo(y.Entry));
            return list.ToArray();
        }

        // The squared distance across the ground between two cell middles is inside [2 m, 10 m] (raw^2, both ends included).
        internal static bool InRing(long squared) => squared >= MinRaw * MinRaw && squared <= MaxRaw * MaxRaw;

        // |floor(sqrt(squared)) - 5 m| in raw.
        internal static long FromPreferred(long squared)
        {
            long d = Isqrt(squared) - PreferRaw;
            return d < 0 ? -d : d;
        }

        // The whole square root of a non-negative long, rounded down; integers only.
        internal static long Isqrt(long n)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            long result = 0, bit = 1L << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0)
            {
                if (n >= result + bit) { n -= result + bit; result = (result >> 1) + bit; }
                else result >>= 1;
                bit >>= 2;
            }
            return result;
        }

        // The watch cell order: seeing the outer side first, then nearer 5 m (FromPreferred), then the smaller node.
        internal static int Rank(bool outerA, long nearA, int nodeA, bool outerB, long nearB, int nodeB)
        {
            if (outerA != outerB) return outerA ? -1 : 1;
            if (nearA != nearB) return nearA < nearB ? -1 : 1;
            return nodeA.CompareTo(nodeB);
        }

        private static WatchCell[] Watch(LayeredNav nav, EntryTable entries, LayeredReach table, long reach, int keep, in PointEntry entry,
            Func<SimVec3, SimVec3, bool> sees, ref long checks)
        {
            int ix = nav.XOf(entry.InnerNode), iz = nav.ZOf(entry.InnerNode);
            var found = new List<(bool outer, long near, int node, SimVec3 point)>();
            for (int layer = 0; layer < nav.LayerCount; layer++)
                for (int z = iz - Box; z <= iz + Box; z++)
                    for (int x = ix - Box; x <= ix + Box; x++)
                    {
                        int w = nav.Node(layer, x, z);
                        if (w < 0 || !nav.IsPassable(w)) continue;
                        long cost = table.CostFrom(w);
                        if (cost < 0 || cost > reach || cost >= entry.OuterCost) continue;
                        long dx = (long)(x - ix) * SimFix.Scale, dz = (long)(z - iz) * SimFix.Scale;
                        long squared = dx * dx + dz * dz;
                        if (!InRing(squared)) continue;
                        if (entries.Owns(entry.Entry, w)) continue;
                        SimVec3 point = EntryTable.Centre(nav, w);
                        checks++;
                        if (!sees(point, entry.InnerPoint)) continue;
                        checks++;
                        found.Add((sees(point, entry.OuterPoint), FromPreferred(squared), w, point));
                    }
            found.Sort((a, b) => Rank(a.outer, a.near, a.node, b.outer, b.near, b.node));
            var result = new WatchCell[keep < found.Count ? keep : found.Count];
            for (int j = 0; j < result.Length; j++)
                result[j] = new WatchCell(found[j].node, nav.LayerOf(found[j].node), found[j].point, found[j].outer);
            return result;
        }
    }
}
