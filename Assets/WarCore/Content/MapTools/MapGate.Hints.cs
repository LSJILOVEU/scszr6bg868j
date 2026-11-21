using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // TRV-1a: one place where the ground cannot walk onto a plate through what looks like a doorway. Plate is the plate's id,
    // (CellX, CellZ) its cell, Side the side of that cell the ground cell lies on, GapRaw the larger height difference at the
    // two end points of the shared edge.
    public sealed class MapHint
    {
        public const string DoorGap = "HINT_DOOR_GAP";

        public string Code { get; }
        public ulong Plate { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public MapSide Side { get; }
        public long GapRaw { get; }

        public MapHint(string code, ulong plate, int cellX, int cellZ, MapSide side, long gapRaw)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Plate = plate; CellX = cellX; CellZ = cellZ; Side = side; GapRaw = gapRaw;
        }

        public override string ToString() => Code + " plate " + Plate + " cell (" + CellX + ", " + CellZ + ") " + Side + " gap " + GapRaw;
    }

    public static partial class MapGate
    {
        // The largest gap a door hint names (1.5 m); a larger one is a storey, not a doorway.
        public const long HintMaxGapRaw = 98304;

        // Door hints, apart from Check (whose result does not change): every walkable plate cell that no walkable ground cell
        // reaches over the ML-6 graph under the pack (orthogonal steps and seams; a diagonal step needs both orthogonal cells,
        // so it reaches nothing more), beside a walkable ground cell across one of its four edges, where the larger height
        // difference at the edge's two end points is in (LayeredNav.StepRaw, HintMaxGapRaw]. One hint per (cell, side), in
        // the order z, x, plate id, side. Empty when the description does not read, does not bake, names a surface the pack
        // lacks, or has no plates: Check reports those.
        public static IReadOnlyList<MapHint> Hints(byte[] descBytes, ContentPack pack)
        {
            if (descBytes == null) throw new ArgumentNullException(nameof(descBytes));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            var none = new List<MapHint>().AsReadOnly();
            MapDesc desc;
            try { desc = MapDescJson.Read(descBytes); }
            catch (InvalidDataException) { return none; }
            if (!string.Equals(pack.Id, desc.Pack, StringComparison.Ordinal))
                throw new ArgumentException("Content pack '" + pack.Id + "' is not the description's pack '" + desc.Pack + "'.", nameof(pack));
            if (desc.Plates.Count == 0) return none;
            if (desc.Surfaces != null && desc.Surfaces.Names.Any(name => pack.Terrain == null || !pack.Terrain.TryFind(name, out _))) return none;
            WcMap map = Bake(desc, new List<MapProblem>());
            if (map == null) return none;
            LayeredNav nav = WcMapLayeredNav.Build(map, pack);

            bool[] reached = Reached(nav);
            var hints = new List<MapHint>();
            for (int node = nav.GroundCount; node < nav.Count; node++)
            {
                if (!nav.IsPassable(node) || reached[node]) continue;
                int layer = nav.LayerOf(node), x = nav.XOf(node), z = nav.ZOf(node);
                foreach (MapSide side in new[] { MapSide.PlusX, MapSide.MinusX, MapSide.PlusZ, MapSide.MinusZ })
                {
                    int dx = side == MapSide.PlusX ? 1 : side == MapSide.MinusX ? -1 : 0;
                    int dz = side == MapSide.PlusZ ? 1 : side == MapSide.MinusZ ? -1 : 0;
                    if (!nav.TryNode(0, x + dx, z + dz, out int ground) || !nav.IsPassable(ground)) continue;
                    // The shared edge's two end points (height point coordinates).
                    int i0 = dx == 1 ? x + 1 : x, j0 = dz == 1 ? z + 1 : z;
                    int i1 = dx == 0 ? x + 1 : i0, j1 = dz == 0 ? z + 1 : j0;
                    long gap = Math.Max(Gap(nav, layer, i0, j0), Gap(nav, layer, i1, j1));
                    if (gap > LayeredNav.StepRaw && gap <= HintMaxGapRaw)
                        hints.Add(new MapHint(MapHint.DoorGap, map.Plates[layer - 1].Id, x, z, side, gap));
                }
            }
            return hints.OrderBy(h => h.CellZ).ThenBy(h => h.CellX).ThenBy(h => h.Plate).ThenBy(h => h.Side).ToList().AsReadOnly();
        }

        private static long Gap(LayeredNav nav, int layer, int i, int j)
        {
            nav.TryLayerHeight(layer, i, j, out long top);
            nav.TryLayerHeight(0, i, j, out long ground);
            return Math.Abs(top - ground);
        }

        // Nodes some walkable ground node reaches by orthogonal steps inside a layer and by seams.
        private static bool[] Reached(LayeredNav nav)
        {
            var reached = new bool[nav.Count];
            var queue = new Queue<int>();
            for (int node = 0; node < nav.GroundCount; node++)
                if (nav.IsPassable(node)) { reached[node] = true; queue.Enqueue(node); }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                int layer = nav.LayerOf(v), x = nav.XOf(v), z = nav.ZOf(v);
                for (int direction = 0; direction < 4; direction++)
                {
                    int dx = direction == 1 ? 1 : direction == 3 ? -1 : 0;
                    int dz = direction == 0 ? 1 : direction == 2 ? -1 : 0;
                    if (nav.TryNode(layer, x + dx, z + dz, out int u)) Visit(nav, reached, queue, u);
                }
                for (int k = 0; k < nav.SeamCount(v); k++) Visit(nav, reached, queue, nav.Seam(v, k));
            }
            return reached;
        }

        private static void Visit(LayeredNav nav, bool[] reached, Queue<int> queue, int node)
        {
            if (reached[node] || !nav.IsPassable(node)) return;
            reached[node] = true;
            queue.Enqueue(node);
        }
    }
}
