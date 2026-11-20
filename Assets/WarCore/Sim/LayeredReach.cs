using System;

namespace WarCore.Sim
{
    // ML-11a: the walking cost from every node of a LayeredNav to one target node, found once (the graph never changes).
    //
    // The costs are LayeredPathfinder's: entering a node costs 1000 x its cost on an orthogonal step or a seam and 1414 x its
    // cost on a diagonal step (diagonals only inside one layer, both orthogonal cells of that layer walkable). The table is
    // filled from the target outwards: a settled node v gives each neighbour u the cost of stepping from u into v plus v's own
    // cost to the target. The neighbour relation is the pathfinder's and is symmetric (a diagonal's two orthogonal cells are
    // the same pair in both directions; a seam is listed on both of its nodes), so CostFrom(n) equals the total of
    // LayeredPathfinder.Find(n, target) wherever that finds a route, and is -1 everywhere else.
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
            // A node is pushed again whenever its cost falls: at most once per edge into it, plus the target.
            var heap = new long[8L * count + nav.Seams.Length + 1 > int.MaxValue ? int.MaxValue : 8 * count + nav.Seams.Length + 1];
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
            }
        }

        public int Target { get; }

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
