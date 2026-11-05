using System;

namespace WarCore.Sim
{
    // Derived, immutable terrain for one map under one rule set: final walkable cells, per-cell path cost and speed,
    // and the height field (null on a map without one). Built by content code; read by movement and pathfinding.
    // Nothing here is per-tick state, so it is never serialised; Digest names the exact (map, rules) pair.
    public sealed class TerrainTables
    {
        // Bump when the derivation or the digest layout changes.
        public const int AlgorithmVersion = 1;

        private readonly byte[] cost;
        private readonly ushort[] speedMilli;
        private readonly byte[] surfaceCells;
        private readonly string[] surfaceNames;

        public HeightField Ground { get; }
        public NavGrid Nav { get; }
        public ulong Digest { get; }
        public int SurfaceCount => surfaceNames.Length;

        // cost / speedMilli are 0 exactly on cells the NavGrid blocks and at least 1 elsewhere (1000 = unchanged speed).
        // surfaceNames / surfaceCells are the map's own table (empty when the map has none); rulesCanonical is the
        // normalised rule set the tables were derived with.
        public TerrainTables(HeightField ground, NavGrid nav, byte[] cost, ushort[] speedMilli,
            string[] surfaceNames, byte[] surfaceCells, byte[] rulesCanonical)
        {
            Nav = nav ?? throw new ArgumentNullException(nameof(nav));
            if (cost == null || speedMilli == null || surfaceNames == null || surfaceCells == null || rulesCanonical == null)
                throw new ArgumentNullException();
            if (cost.Length != nav.Count || speedMilli.Length != nav.Count)
                throw new ArgumentException("Tables differ from the grid.");
            if (surfaceCells.Length != (surfaceNames.Length == 0 ? 0 : nav.Count))
                throw new ArgumentException("Surface cells differ from the grid.");
            if (ground != null && (ground.Width != nav.Width || ground.Depth != nav.Depth
                || ground.OriginX != nav.OriginX || ground.OriginZ != nav.OriginZ))
                throw new ArgumentException("Height field differs from the grid.");
            for (int i = 0; i < nav.Count; i++)
            {
                bool walkable = nav.IsPassable(i % nav.Width, i / nav.Width);
                if (walkable != (cost[i] != 0) || walkable != (speedMilli[i] != 0))
                    throw new ArgumentException("Cost and speed must be zero exactly on blocked cells.");
            }
            Ground = ground;
            this.cost = (byte[])cost.Clone();
            this.speedMilli = (ushort[])speedMilli.Clone();
            this.surfaceNames = (string[])surfaceNames.Clone();
            this.surfaceCells = (byte[])surfaceCells.Clone();
            var hash = new StateHash();
            hash.Add(AlgorithmVersion);
            hash.Add(ground == null ? 0UL : ground.Digest);
            hash.Add(nav.Digest);
            hash.Add(surfaceNames.Length);
            foreach (string name in surfaceNames) hash.Add(name);
            hash.Add(surfaceCells.Length);
            foreach (byte b in surfaceCells) hash.Add(b);
            hash.Add(rulesCanonical.Length);
            foreach (byte b in rulesCanonical) hash.Add(b);
            foreach (byte b in this.cost) hash.Add(b);
            foreach (ushort s in this.speedMilli) hash.Add((int)s);
            Digest = hash.Value;
        }

        public byte CostAt(int key) => cost[key];
        public ushort SpeedMilliAt(int key) => speedMilli[key];
        public string SurfaceName(int index) => surfaceNames[index];
        // Index into the map's own name table; only valid when SurfaceCount > 0.
        public byte SurfaceIndexAt(int key) => surfaceCells[key];
    }
}
