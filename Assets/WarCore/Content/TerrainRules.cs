using System;
using System.IO;
using System.Text;
using WarCore.Sim;

namespace WarCore.Content
{
    // One surface type a map can name. A blocked surface (Passable false) is stored as speed 0, cost 0.
    // SpeedMilli is thousandths (1000 = unchanged); Cost is the path-cost tier (1 = baseline).
    public readonly struct SurfaceDef
    {
        public const int MaxSpeedMilli = 2000, MaxCost = 255;
        public string Id { get; }
        public bool Passable { get; }
        public int SpeedMilli { get; }
        public int Cost { get; }

        public SurfaceDef(string id, bool passable, int speedMilli, int cost)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Surface id is required.", nameof(id));
            if (passable && (speedMilli < 1 || speedMilli > MaxSpeedMilli)) throw new ArgumentOutOfRangeException(nameof(speedMilli));
            if (passable && (cost < 1 || cost > MaxCost)) throw new ArgumentOutOfRangeException(nameof(cost));
            Id = id; Passable = passable;
            SpeedMilli = passable ? speedMilli : 0;
            Cost = passable ? cost : 0;
        }
    }

    // The terrain rules of one content pack: the walkable-slope ratio and the surface table (kept ordinal-sorted by id).
    public sealed class TerrainRules
    {
        public string PackId { get; }
        public int MaxWalkableNum { get; }
        public int MaxWalkableDen { get; }
        private readonly SurfaceDef[] surfaces;

        public TerrainRules(string packId, int maxWalkableNum, int maxWalkableDen, SurfaceDef[] surfaces)
        {
            if (maxWalkableDen < 1 || maxWalkableDen > HeightField.MaxWalkableDenominator
                || maxWalkableNum < 0 || maxWalkableNum > HeightField.MaxSlopeNumerator * maxWalkableDen)
                throw new ArgumentOutOfRangeException(nameof(maxWalkableNum));
            PackId = packId; MaxWalkableNum = maxWalkableNum; MaxWalkableDen = maxWalkableDen;
            this.surfaces = (SurfaceDef[])(surfaces ?? throw new ArgumentNullException(nameof(surfaces))).Clone();
            Array.Sort(this.surfaces, (a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            for (int i = 1; i < this.surfaces.Length; i++)
                if (this.surfaces[i - 1].Id == this.surfaces[i].Id) throw new ArgumentException("Duplicate surface id.", nameof(surfaces));
        }

        public int SurfaceCount => surfaces.Length;
        public SurfaceDef Surface(int index) => surfaces[index];

        public bool TryFind(string id, out SurfaceDef def)
        {
            foreach (SurfaceDef s in surfaces)
                if (string.Equals(s.Id, id, StringComparison.Ordinal)) { def = s; return true; }
            def = default;
            return false;
        }

        // Normalised bytes the TerrainTables digest covers: slope ratio, then each surface in id order.
        internal byte[] Canonical()
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(MaxWalkableNum); w.Write(MaxWalkableDen); w.Write(surfaces.Length);
            foreach (SurfaceDef s in surfaces)
            {
                byte[] id = new UTF8Encoding(false, true).GetBytes(s.Id);
                w.Write(id.Length); w.Write(id);
                w.Write(s.Passable); w.Write(s.SpeedMilli); w.Write(s.Cost);
            }
            w.Flush();
            return stream.ToArray();
        }
    }
}
