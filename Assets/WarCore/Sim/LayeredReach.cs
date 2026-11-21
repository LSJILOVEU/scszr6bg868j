using System;

namespace WarCore.Sim
{
    // ML-11a: the walking cost from every node of a LayeredNav to one target node, found once (the graph never changes).
    //
    // The costs are LayeredPathfinder's: entering a node costs 1000 x its cost on an orthogonal step or a seam and 1414 x its
    // cost on a diagonal step (diagonals only inside one layer, both orthogonal cells of that layer walkable). The table is
    // filled from the target outwards: a settled node v gives each neighbour u the cost of stepping from u into v plus v's own
    // cost to the target. Walking steps and seams are symmetric (a diagonal's two orthogonal cells are the same pair in both
    // directions; a seam is listed on both of its nodes); links have a direction (TRV-3, K16), so they are followed through
    // the index by end node: a link u -> v gives u the cost 1000 x Span x v's cost + PathCost plus v's cost. So CostFrom(n)
    // equals the total of LayeredPathfinder.Find(n, target) wherever that finds a route, and is -1 everywhere else.
    public sealed class LayeredReach
    {
        private readonly long[] costs;

        public LayeredReach(LayeredNav nav, int target)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            if (target < 0 || target >= nav.Count) throw new ArgumentOutOfRangeException(nameof(target));
            Target = target;
            int count = nav.Count;
            costs = new long[count];
            for (int i = 0; i < count; i++) costs[i] = -1;
            if (nav.Cost[target] == 0) return;
            // A node is pushed again whenever its cost falls: at most once per edge into it (eight cells, seams, links), plus
            // the target.
            long capacity = 8L * count + nav.Seams.Length + nav.LinkCount + 1;
            var heap = new long[capacity > int.MaxValue ? int.MaxValue : (int)capacity];
            HeapCapacity = heap.Length;
            var nodes = new int[heap.Length];
            int size = 0;
            costs[target] = 0;
            Push(heap, nodes, ref size, 0, target);
            while (size != 0)
            {
                long cost = heap[0]; int v = nodes[0];
                Pop(heap, nodes, ref size);
                if (cost != costs[v]) continue;
                int layer = nav.NodeLayer[v], x = nav.NodeX[v], z = nav.NodeZ[v];
                long enter = nav.Cost[v];
                for (int direction = 0; direction < 8; direction++)
                {
                    // N, E, S, W, NE, SE, SW, NW.
                    int dx = direction == 1 || direction == 4 || direction == 5 ? 1
                        : direction == 3 || direction == 6 || direction == 7 ? -1 : 0;
                    int dz = direction == 0 || direction == 4 || direction == 7 ? 1
                        : direction == 2 || direction == 5 || direction == 6 ? -1 : 0;
                    int u = Walkable(nav, layer, x + dx, z + dz);
                    if (u < 0) continue;
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal && (Walkable(nav, layer, x + dx, z) < 0 || Walkable(nav, layer, x, z + dz) < 0)) continue;
                    Relax(heap, nodes, ref size, u, cost + (diagonal ? 1414 : 1000) * enter);
                }
                for (int s = nav.SeamStart[v]; s < nav.SeamStart[v + 1]; s++)
                    Relax(heap, nodes, ref size, nav.Seams[s], cost + 1000 * enter);
                if (nav.Links == null) continue;
                for (int i = nav.InStart[v]; i < nav.InStart[v + 1]; i++)
                {
                    NavLink link = nav.Links[nav.InLinks[i]];
                    Relax(heap, nodes, ref size, link.From, cost + 1000L * link.Span * enter + link.PathCost);
                }
            }
        }

        public int Target { get; }

        // The heap's size (it never grows: every push fits). 0 when the target is not walkable. Diagnostics only.
        internal int HeapCapacity { get; }

        // ML-11b-1: which nodes can be walked between. Gives every node its island; a node nobody walks on has -1. Islands are
        // numbered from 0 in ascending order of their lowest node. Two nodes share an island when steps, seams and two-way links
        // join them (TRV-3 A5): a link joins only when the link back exists too. Then LayeredPathfinder.Find finds a route
        // between them in both directions; without links that is exactly when it finds one. Orthogonal steps are enough: a
        // diagonal step needs both orthogonal cells walkable, so it joins nothing that two orthogonal steps do not join. A node
        // reached only over a one-way link (a drop), or only round a ring of one-way links, is on another island; CostFrom
        // answers a route that needs one.
        public static int[] Islands(LayeredNav nav)
        {
            if (nav == null) throw new ArgumentNullException(nameof(nav));
            int count = nav.Count, next = 0;
            var islands = new int[count];
            for (int i = 0; i < count; i++) islands[i] = -1;
            var queue = new int[count];
            for (int start = 0; start < count; start++)
            {
                if (nav.Cost[start] == 0 || islands[start] >= 0) continue;
                int head = 0, tail = 0;
                islands[start] = next; queue[tail++] = start;
                while (head < tail)
                {
                    int v = queue[head++], layer = nav.NodeLayer[v], x = nav.NodeX[v], z = nav.NodeZ[v];
                    for (int direction = 0; direction < 4; direction++)
                    {
                        // N, E, S, W.
                        int u = Walkable(nav, layer, x + (direction == 1 ? 1 : direction == 3 ? -1 : 0), z + (direction == 0 ? 1 : direction == 2 ? -1 : 0));
                        if (u >= 0 && islands[u] < 0) { islands[u] = next; queue[tail++] = u; }
                    }
                    for (int s = nav.SeamStart[v]; s < nav.SeamStart[v + 1]; s++)
                    {
                        int u = nav.Seams[s];
                        if (islands[u] < 0) { islands[u] = next; queue[tail++] = u; }
                    }
                    if (nav.Links == null) continue;
                    for (int l = nav.LinkStart[v]; l < nav.LinkStart[v + 1]; l++)
                    {
                        int u = nav.Links[l].To;
                        if (islands[u] < 0 && nav.TryLink(u, v, out _)) { islands[u] = next; queue[tail++] = u; }
                    }
                }
                next++;
            }
            return islands;
        }

        // The cheapest walking cost from the node to the target; -1 when there is no route or the node is not walkable.
        public long CostFrom(int node) => costs[node];

        private void Relax(long[] heap, int[] nodes, ref int size, int u, long cost)
        {
            if (costs[u] >= 0 && cost >= costs[u]) return;
            costs[u] = cost;
            Push(heap, nodes, ref size, cost, u);
        }

        // The walkable node of (layer, x, z), or -1.
        private static int Walkable(LayeredNav nav, int layer, int x, int z)
        {
            int node = nav.Node(layer, x, z);
            return node >= 0 && nav.Cost[node] != 0 ? node : -1;
        }

        // A binary heap ordered by cost, then node (the order changes no result: the costs are unique).
        private static bool Before(long costA, int nodeA, long costB, int nodeB) => costA != costB ? costA < costB : nodeA < nodeB;

        private static void Push(long[] heap, int[] nodes, ref int size, long cost, int node)
        {
            int at = size++;
            while (at > 0)
            {
                int parent = (at - 1) / 2;
                if (!Before(cost, node, heap[parent], nodes[parent])) break;
                heap[at] = heap[parent]; nodes[at] = nodes[parent]; at = parent;
            }
            heap[at] = cost; nodes[at] = node;
        }

        private static void Pop(long[] heap, int[] nodes, ref int size)
        {
            long cost = heap[--size]; int node = nodes[size];
            if (size == 0) return;
            int at = 0;
            while (at * 2 + 1 < size)
            {
                int child = at * 2 + 1;
                if (child + 1 < size && Before(heap[child + 1], nodes[child + 1], heap[child], nodes[child])) child++;
                if (!Before(heap[child], nodes[child], cost, node)) break;
                heap[at] = heap[child]; nodes[at] = nodes[child]; at = child;
            }
            heap[at] = cost; nodes[at] = node;
        }
    }
}
