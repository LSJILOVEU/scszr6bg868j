using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // TRV-3: the link table of a LayeredNav (rule K16). Links are directed and listed in ascending (From, To) order, so
    // LinkStart indexes them by start node directly; InStart/InLinks index them by end node (link indices, ascending From
    // inside each end node). A link exists only where the two nodes have no walking step and no seam between them, so a
    // (From, To) pair names at most one way from one node to the other.
    public sealed partial class LayeredNav
    {
        public const int MaxLinks = 65535;
        public const string LinksTag = "nav.links.v1";

        internal readonly NavLink[] Links;   // null without a link table
        internal readonly int[] LinkStart;   // node -> first link starting there; LinkStart[Count] = Links.Length
        internal readonly int[] InStart;     // node -> first index in InLinks; InStart[Count] = Links.Length
        internal readonly int[] InLinks;     // link indices by end node

        // The same graph with this link table (replacing any it has). Every other table is shared, not rebuilt.
        private LayeredNav(LayeredNav source, IReadOnlyList<NavLink> links)
        {
            Ground = source.Ground; Width = source.Width; Depth = source.Depth; GroundCount = source.GroundCount; Count = source.Count;
            plates = source.plates; flatGroundRaw = source.flatGroundRaw; layerStart = source.layerStart; rectStart = source.rectStart;
            rectNode = source.rectNode; NodeX = source.NodeX; NodeZ = source.NodeZ; NodeLayer = source.NodeLayer; Cost = source.Cost;
            speed = source.speed; SeamStart = source.SeamStart; Seams = source.Seams;
            BuildLinks(links, out Links, out LinkStart, out InStart, out InLinks);
            Digest = Hash();
        }

        // This graph with the given link table; the result equals the constructor given the same tables and these links.
        public LayeredNav WithLinks(IReadOnlyList<NavLink> links) => new LayeredNav(this, links ?? throw new ArgumentNullException(nameof(links)));

        public bool HasLinks => Links != null;
        public int LinkCount => Links == null ? 0 : Links.Length;

        public NavLink Link(int index)
        {
            if (index < 0 || index >= LinkCount) throw new ArgumentOutOfRangeException(nameof(index));
            return Links[index];
        }

        // Links starting at the node are the indices LinkFromFirst .. LinkFromFirst + LinkFromCount - 1, ascending To.
        public int LinkFromFirst(int node)
        {
            Check(node);
            return Links == null ? 0 : LinkStart[node];
        }

        public int LinkFromCount(int node)
        {
            Check(node);
            return Links == null ? 0 : LinkStart[node + 1] - LinkStart[node];
        }

        // The k-th link ending at the node (a link index), in ascending order of its start node.
        public int LinkToCount(int node)
        {
            Check(node);
            return Links == null ? 0 : InStart[node + 1] - InStart[node];
        }

        public int LinkTo(int node, int k)
        {
            if (k < 0 || k >= LinkToCount(node)) throw new ArgumentOutOfRangeException(nameof(k));
            return InLinks[InStart[node] + k];
        }

        // The link from one node to another; false (index -1) when there is none.
        public bool TryLink(int from, int to, out int index)
        {
            index = -1;
            if (Links == null || from < 0 || from >= Count) return false;
            int lo = LinkStart[from], hi = LinkStart[from + 1] - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int at = Links[mid].To;
                if (at == to) { index = mid; return true; }
                if (at < to) lo = mid + 1; else hi = mid - 1;
            }
            return false;
        }

        private void BuildLinks(IReadOnlyList<NavLink> links, out NavLink[] table, out int[] start, out int[] inStart, out int[] inLinks)
        {
            if (links.Count > MaxLinks) throw new ArgumentException("More than " + MaxLinks + " links.", nameof(links));
            table = new NavLink[links.Count];
            start = new int[Count + 1];
            inStart = new int[Count + 1];
            for (int n = 0; n < table.Length; n++)
            {
                NavLink l = links[n];
                string at = "Link " + n + " (" + l.From + " -> " + l.To + "): ";
                if (l.From < 0 || l.From >= Count || l.To < 0 || l.To >= Count) throw new ArgumentException(at + "an end is not a node.", nameof(links));
                if (Cost[l.From] == 0 || Cost[l.To] == 0) throw new ArgumentException(at + "an end is not walkable.", nameof(links));
                if (n > 0)
                {
                    NavLink p = table[n - 1];
                    if (p.From == l.From && p.To == l.To) throw new ArgumentException(at + "repeats the link before it.", nameof(links));
                    if (p.From > l.From || (p.From == l.From && p.To > l.To)) throw new ArgumentException(at + "out of (from, to) order.", nameof(links));
                }
                if (l.Span < 1 || l.Span > NavLink.MaxSpan) throw new ArgumentException(at + "span must be 1 or 2.", nameof(links));
                if (l.PathCost < 0 || l.PathCost > NavLink.MaxPathCost) throw new ArgumentException(at + "path cost must be 0.." + NavLink.MaxPathCost + ".", nameof(links));
                if (l.Kind < 0) throw new ArgumentException(at + "kind must not be negative.", nameof(links));
                if (l.Ticks < 1) throw new ArgumentException(at + "ticks must be at least 1.", nameof(links));
                int dx = NodeX[l.From] - NodeX[l.To], dz = NodeZ[l.From] - NodeZ[l.To];
                if (dx < 0) dx = -dx;
                if (dz < 0) dz = -dz;
                // The octile distance between the cells must not exceed the span, so the pathfinder's heuristic stays consistent.
                if (1000L * (dx + dz) - 586L * (dx < dz ? dx : dz) > 1000L * l.Span) throw new ArgumentException(at + "the cells are farther apart than the span.", nameof(links));
                // Also refuses a link from a node to itself (same layer, dx = dz = 0).
                if (NodeLayer[l.From] == NodeLayer[l.To] && dx <= 1 && dz <= 1) throw new ArgumentException(at + "the ends are neighbours on one layer.", nameof(links));
                for (int s = SeamStart[l.From]; s < SeamStart[l.From + 1]; s++)
                    if (Seams[s] == l.To) throw new ArgumentException(at + "the ends are joined by a seam.", nameof(links));
                table[n] = l;
                start[l.From + 1]++;
                inStart[l.To + 1]++;
            }
            for (int node = 0; node < Count; node++) { start[node + 1] += start[node]; inStart[node + 1] += inStart[node]; }
            inLinks = new int[table.Length];
            var fill = (int[])inStart.Clone();
            for (int n = 0; n < table.Length; n++) inLinks[fill[table[n].To]++] = n;
        }

        private void HashLinks(StateHash hash)
        {
            hash.Add(LinksTag);
            hash.Add(Links.Length);
            foreach (NavLink l in Links)
            {
                hash.Add(l.From); hash.Add(l.To); hash.Add(l.Span); hash.Add(l.PathCost);
                hash.Add(l.Kind); hash.Add(l.Ticks); hash.Add(l.Cross); hash.Add(l.Gap);
            }
        }
    }
}
