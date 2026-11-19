using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // ML-6: the walkable graph of a map with floor plates. Immutable, built once per (map, rules).
    //
    // Layers: 0 is the ground; 1..N are the plates in the order given (ids rising from 1, i.e. the baked order), the same
    // numbers as the vertical state (K3). Nodes: ground node = ground cell key z * Width + x, every cell numbered, walkable
    // or not; then each plate's solid cells, layer by layer, row-major (x first) inside the plate. Cutout cells get no node.
    //
    // Neighbours inside a layer are the eight cells of that layer (a plate's own rectangle and solid cells only). Layers
    // meet only at seams (K10): two walkable nodes on different layers whose cells share an edge, where at both end points
    // of that edge the two layers' heights differ by at most StepRaw. Ground height is the height field's point, or the
    // given flat height on a map without one; a plate's height is its own top point table (the one PlateField uses, K11).
    // Seams are listed per node in ascending order of the other node and are only made between walkable nodes.
    public sealed class LayeredNav
    {
        public const int AlgorithmVersion = 1;
        // 0.3 m (19660.8 raw) rounded down: a difference <= StepRaw is exactly a difference of at most 0.3 m in whole raw.
        public const long StepRaw = 19660;
        public const int MaxPlates = PlateField.MaxPlates, MaxPerCell = PlateField.MaxPerCell;

        public TerrainTables Ground { get; }
        public int Width { get; }
        public int Depth { get; }
        public int GroundCount { get; }
        public int Count { get; }
        public int LayerCount => plates.Length + 1;
        public ulong Digest { get; }

        private readonly PlateSlab[] plates;
        private readonly long flatGroundRaw;
        private readonly int[] layerStart;   // first node of layer l; layerStart[LayerCount] = Count
        private readonly int[] rectStart;    // plate k's rectangle cells start here in rectNode
        private readonly int[] rectNode;     // rectangle cell -> node, or -1 off the solid cells
        internal readonly int[] NodeX, NodeZ, NodeLayer;
        internal readonly byte[] Cost;
        private readonly ushort[] speed;
        internal readonly int[] SeamStart;   // node -> first index in Seams; SeamStart[Count] = Seams.Length
        internal readonly int[] Seams;

        // ground: the ground's derived tables. flatGroundRaw: the ground height on a map without a height field (ignored when
        // ground.Ground is set). costs[k], speeds[k]: plate k's cells, row-major, 0 exactly on the cells that are not walkable.
        public LayeredNav(TerrainTables ground, long flatGroundRaw, IReadOnlyList<PlateSlab> plates,
            IReadOnlyList<byte[]> costs, IReadOnlyList<ushort[]> speeds)
        {
            Ground = ground ?? throw new ArgumentNullException(nameof(ground));
            if (plates == null) throw new ArgumentNullException(nameof(plates));
            if (costs == null) throw new ArgumentNullException(nameof(costs));
            if (speeds == null) throw new ArgumentNullException(nameof(speeds));
            if (plates.Count > MaxPlates) throw new ArgumentException("More than " + MaxPlates + " plates.", nameof(plates));
            if (costs.Count != plates.Count || speeds.Count != plates.Count) throw new ArgumentException("One cost and one speed table per plate.", nameof(costs));
            if (ground.Ground == null && (flatGroundRaw <= -HeightField.HeightLimitRaw || flatGroundRaw >= HeightField.HeightLimitRaw))
                throw new ArgumentOutOfRangeException(nameof(flatGroundRaw));
            NavGrid nav = ground.Nav;
            Width = nav.Width; Depth = nav.Depth; GroundCount = nav.Count;
            this.flatGroundRaw = ground.Ground == null ? flatGroundRaw : 0;
            this.plates = new PlateSlab[plates.Count];
            var perCell = new int[GroundCount];
            long plateNodes = 0;
            for (int k = 0; k < this.plates.Length; k++)
            {
                PlateSlab p = plates[k] ?? throw new ArgumentNullException(nameof(plates));
                if (p.Id == 0 || (k > 0 && p.Id <= this.plates[k - 1].Id)) throw new ArgumentException("Plate ids must rise from 1.", nameof(plates));
                if (p.X < 0 || p.Z < 0 || p.X + p.Width > Width || p.Z + p.Depth > Depth) throw new ArgumentException("Plate " + p.Id + " leaves the map.", nameof(plates));
                byte[] c = costs[k] ?? throw new ArgumentNullException(nameof(costs));
                ushort[] s = speeds[k] ?? throw new ArgumentNullException(nameof(speeds));
                if (c.Length != p.Width * p.Depth || s.Length != p.Width * p.Depth) throw new ArgumentException("Plate " + p.Id + " table size.", nameof(costs));
                foreach (int h in p.Points)
                    if (h <= -HeightField.HeightLimitRaw || h >= HeightField.HeightLimitRaw) throw new ArgumentException("Plate " + p.Id + " height.", nameof(plates));
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                    {
                        int at = j * p.Width + i;
                        if ((c[at] != 0) != (s[at] != 0)) throw new ArgumentException("Plate " + p.Id + ": cost and speed must be zero together.", nameof(costs));
                        if (!p.IsSolid(i, j) && c[at] != 0) throw new ArgumentException("Plate " + p.Id + ": a cell that is not solid is walkable.", nameof(costs));
                        if (s[at] > TerrainTables.MaxSpeedMilli) throw new ArgumentException("Plate " + p.Id + ": speed above " + TerrainTables.MaxSpeedMilli + ".", nameof(speeds));
                        if (!p.IsSolid(i, j)) continue;
                        plateNodes++;
                        if (++perCell[(p.Z + j) * Width + p.X + i] > MaxPerCell)
                            throw new ArgumentException("More than " + MaxPerCell + " plates in one cell.", nameof(plates));
                    }
                this.plates[k] = p;
            }
            if (plateNodes > GroundCount) throw new ArgumentException("More plate cells than ground cells.", nameof(plates));
            Count = GroundCount + (int)plateNodes;

            layerStart = new int[LayerCount + 1];
            rectStart = new int[this.plates.Length + 1];
            for (int k = 0; k < this.plates.Length; k++) rectStart[k + 1] = rectStart[k] + this.plates[k].Width * this.plates[k].Depth;
            rectNode = new int[rectStart[this.plates.Length]];
            NodeX = new int[Count]; NodeZ = new int[Count]; NodeLayer = new int[Count];
            Cost = new byte[Count]; speed = new ushort[Count];
            for (int key = 0; key < GroundCount; key++)
            {
                NodeX[key] = key % Width; NodeZ[key] = key / Width;
                Cost[key] = ground.CostAt(key); speed[key] = ground.SpeedMilliAt(key);
            }
            int next = GroundCount;
            for (int k = 0; k < this.plates.Length; k++)
            {
                PlateSlab p = this.plates[k];
                layerStart[k + 1] = next;
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                    {
                        int at = j * p.Width + i;
                        if (!p.IsSolid(i, j)) { rectNode[rectStart[k] + at] = -1; continue; }
                        rectNode[rectStart[k] + at] = next;
                        NodeX[next] = p.X + i; NodeZ[next] = p.Z + j; NodeLayer[next] = k + 1;
                        Cost[next] = costs[k][at]; speed[next] = speeds[k][at];
                        next++;
                    }
            }
            layerStart[LayerCount] = Count;
            BuildSeams(perCell, out SeamStart, out Seams);
            Digest = Hash();
        }

        public bool IsPassable(int node) => node >= 0 && node < Count && Cost[node] != 0;
        public byte CostAt(int node) => Cost[Check(node)];
        public ushort SpeedMilliAt(int node) => speed[Check(node)];
        public int LayerOf(int node) => NodeLayer[Check(node)];
        public int XOf(int node) => NodeX[Check(node)];
        public int ZOf(int node) => NodeZ[Check(node)];
        public int SeamCount(int node) => SeamStart[Check(node) + 1] - SeamStart[node];

        // The k-th seam of the node, in ascending order of the other node.
        public int Seam(int node, int k)
        {
            if (k < 0 || k >= SeamCount(node)) throw new ArgumentOutOfRangeException(nameof(k));
            return Seams[SeamStart[node] + k];
        }

        // The node of map cell (x, z) on a layer; false off the map, off the plate or on a cutout cell.
        public bool TryNode(int layer, int x, int z, out int node)
        {
            node = Node(layer, x, z);
            return node >= 0;
        }

        // The layer's height at map height point (i, j), i in 0..Width, j in 0..Depth; false off the map or off the plate.
        public bool TryLayerHeight(int layer, int i, int j, out long heightRaw)
        {
            heightRaw = 0;
            if (layer < 0 || layer >= LayerCount || i < 0 || j < 0 || i > Width || j > Depth) return false;
            if (layer == 0)
            {
                heightRaw = Ground.Ground != null ? Ground.Ground.VertexRaw(i, j) : flatGroundRaw;
                return true;
            }
            PlateSlab p = plates[layer - 1];
            if (i < p.X || j < p.Z || i > p.X + p.Width || j > p.Z + p.Depth) return false;
            heightRaw = p.TopRaw(i - p.X, j - p.Z);
            return true;
        }

        // Node of (layer, x, z) or -1.
        internal int Node(int layer, int x, int z)
        {
            if (layer < 0 || layer >= LayerCount || x < 0 || z < 0 || x >= Width || z >= Depth) return -1;
            if (layer == 0) return z * Width + x;
            PlateSlab p = plates[layer - 1];
            int lx = x - p.X, lz = z - p.Z;
            if (lx < 0 || lz < 0 || lx >= p.Width || lz >= p.Depth) return -1;
            return rectNode[rectStart[layer - 1] + lz * p.Width + lx];
        }

        private int Check(int node) => node >= 0 && node < Count ? node : throw new ArgumentOutOfRangeException(nameof(node));

        private long HeightAt(int layer, int i, int j)
        {
            if (!TryLayerHeight(layer, i, j, out long h)) throw new InvalidOperationException("Seam point off its layer.");
            return h;
        }

        private static long Abs(long v) => v < 0 ? -v : v;

        private void BuildSeams(int[] perCell, out int[] start, out int[] seams)
        {
            // Plates solid in each cell, ascending layer.
            var cellStart = new int[GroundCount + 1];
            for (int c = 0; c < GroundCount; c++) cellStart[c + 1] = cellStart[c] + perCell[c];
            var cellLayers = new int[cellStart[GroundCount]];
            var fill = (int[])cellStart.Clone();
            for (int node = GroundCount; node < Count; node++)
            {
                int cell = NodeZ[node] * Width + NodeX[node];
                cellLayers[fill[cell]++] = NodeLayer[node];
            }
            // Every seam is found from its plate side: a plate node looks at the ground and every other plate on each of its
            // four edges; a ground node never looks, so its seams are added from the plate side as well.
            var pairs = new List<long>();
            for (int a = GroundCount; a < Count; a++)
            {
                if (Cost[a] == 0) continue;
                int x = NodeX[a], z = NodeZ[a], la = NodeLayer[a];
                for (int direction = 0; direction < 4; direction++)
                {
                    // N, E, S, W.
                    int dx = direction == 1 ? 1 : direction == 3 ? -1 : 0;
                    int dz = direction == 0 ? 1 : direction == 2 ? -1 : 0;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= Width || nz >= Depth) continue;
                    // The shared edge's two end points (height point coordinates).
                    int i0 = dx == 1 ? x + 1 : x, j0 = dz == 1 ? z + 1 : z;
                    int i1 = dx == 0 ? x + 1 : i0, j1 = dz == 0 ? z + 1 : j0;
                    int cell = nz * Width + nx;
                    for (int m = -1; m < cellStart[cell + 1] - cellStart[cell]; m++)
                    {
                        int lb = m < 0 ? 0 : cellLayers[cellStart[cell] + m];
                        if (lb == la) continue;
                        int b = Node(lb, nx, nz);
                        if (Cost[b] == 0) continue;
                        if (Abs(HeightAt(la, i0, j0) - HeightAt(lb, i0, j0)) > StepRaw) continue;
                        if (Abs(HeightAt(la, i1, j1) - HeightAt(lb, i1, j1)) > StepRaw) continue;
                        pairs.Add(((long)a << 32) | (uint)b);
                        if (lb == 0) pairs.Add(((long)b << 32) | (uint)a);
                    }
                }
            }
            pairs.Sort();
            start = new int[Count + 1];
            seams = new int[pairs.Count];
            for (int n = 0; n < pairs.Count; n++)
            {
                start[(int)(pairs[n] >> 32) + 1]++;
                seams[n] = (int)(pairs[n] & 0xFFFFFFFF);
            }
            for (int node = 0; node < Count; node++) start[node + 1] += start[node];
        }

        private ulong Hash()
        {
            var hash = new StateHash();
            hash.Add(AlgorithmVersion);
            hash.Add(StepRaw);
            hash.Add(Ground.Digest);
            hash.Add(Ground.Ground == null);
            if (Ground.Ground == null) hash.Add(flatGroundRaw);
            hash.Add(plates.Length);
            for (int k = 0; k < plates.Length; k++)
            {
                PlateSlab p = plates[k];
                hash.Add(p.Id); hash.Add(p.X); hash.Add(p.Z); hash.Add(p.Width); hash.Add(p.Depth);
                foreach (int h in p.Points) hash.Add(h);
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++) hash.Add(p.IsSolid(i, j));
            }
            for (int node = GroundCount; node < Count; node++) { hash.Add(Cost[node]); hash.Add((int)speed[node]); }
            hash.Add(Seams.Length);
            foreach (int s in SeamStart) hash.Add(s);
            foreach (int s in Seams) hash.Add(s);
            return hash.Value;
        }
    }
}
