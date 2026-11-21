using System;
using System.Collections.Generic;
using System.Linq;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // TRV-1a fixtures: descriptions for the ramp prefab and the door hints. Heights are absolute raw; 1 m = 65536.
    internal static class Trv1aMaps
    {
        internal const long M = SimFix.Scale;
        internal const long Step = LayeredNav.StepRaw;   // 19660, 0.3 m

        // Flat ground at 0 unless heights are given; the pack is rwr-m6 (no slope rule: walkable 2/1, plate slopes 5/3).
        internal static MapDesc Flat(int width, int depth, params MapPlate[] plates) => Ml4Maps.Make(0, width, depth, null, plates);

        internal static MapDesc Ground(int width, int depth, Func<int, int, long> height, long ceilingRaw, MapPlate[] plates = null, MapWall[] walls = null) =>
            Ml4Maps.Make(0, width, depth, Heights(width, depth, height), plates ?? new MapPlate[0], walls: walls, ceilingRaw: ceilingRaw);

        internal static int[][] Heights(int width, int depth, Func<int, int, long> height) =>
            Enumerable.Range(0, depth + 1).Select(j => Enumerable.Range(0, width + 1).Select(i => (int)height(i, j)).ToArray()).ToArray();

        internal static MapStampPlan Ramp(MapDesc d, int x, int z, int width, MapSide side, long doorTop, long num = 3, long den = 4) =>
            MapPrefabs.Ramp(d, "ramp", new MapXZ(x, z), width, side, doorTop, num, den);

        internal static MapPlate Plate(MapStampPlan plan) => plan.Plates.Single().Template;

        // The plate's top points along its slope axis, from its minimum edge (the table PlateSpan.TopPoint builds).
        internal static long[] Points(MapPlate p)
        {
            int length = p.Slope.Axis == "x" ? p.Size.X : p.Size.Z;
            return Enumerable.Range(0, length + 1).Select(k => p.Slope.FromRaw + Floor((p.Slope.ToRaw - p.Slope.FromRaw) * k, length)).ToArray();
        }

        private static long Floor(long a, long b) => a / b - ((a % b != 0 && (a < 0) != (b < 0)) ? 1 : 0);

        internal static MapDesc Stamp(MapDesc d, MapStampPlan plan)
        {
            var edit = new MapEdit(d);
            edit.Stamp(plan);
            return edit.Current;
        }

        internal static ContentPack Pack => RwrPack.Load("rwr-m6");

        internal static LayeredNav Nav(MapDesc d) => WcMapLayeredNav.Build(Ml4Maps.Bake(d), Pack);

        internal static IReadOnlyList<MapHint> Hints(MapDesc d) => MapGate.Hints(Ml4Maps.Bytes(d), Pack);

        internal static int Node(LayeredNav nav, int layer, int x, int z)
        {
            if (!nav.TryNode(layer, x, z, out int node)) throw new InvalidOperationException("no node at layer " + layer + " (" + x + ", " + z + ")");
            return node;
        }

        internal static long Cost(LayeredNav nav, int from, int to)
        {
            var finder = new LayeredPathfinder(nav);
            return finder.Find(from, to) == GridPathStatus.Found ? finder.TotalCostWide : -1;
        }
    }
}
