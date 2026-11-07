using System.Collections.Generic;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;

namespace WarCore.Tests
{
    // Stamp plans of the E3 edit tests, written out value by value.
    internal static class E3Plans
    {
        // Stamp s1 of acceptance 1: cells x, z in [0, 3] get "road", corners i, j in [0, 4] get 1000, walls src 1 at (0, 0)
        // and src 2 at (2, 2), point src 1 at cell (2, 0), base src 1 at cell (0, 2), unit src 1 at cell (1, 1).
        internal static MapStampPlan S1() => new MapStampPlan("s1", Corners(0, 4, 0, 4, 1000), Cells(0, 3, 0, 3, "road"),
            new[] { new MapStampEntry<MapWall>(1, WallAt(0, 0)), new MapStampEntry<MapWall>(2, WallAt(2, 2)) },
            new[] { new MapStampEntry<MapPoint>(1, PointAt(2500, 500)) }, new[] { new MapStampEntry<MapBase>(1, BaseAt(500, 2500)) },
            new[] { new MapStampEntry<MapUnit>(1, UnitAt(1500, 1500)) });

        // Stamp s2 of acceptance 4: cells x in [2, 5], z in [0, 3] get "mud", corners i in [2, 6], j in [0, 4] get 2000.
        internal static MapStampPlan S2() => new MapStampPlan("s2", Corners(2, 6, 0, 4, 2000), Cells(2, 5, 0, 3, "mud"),
            new MapStampEntry<MapWall>[0], new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]);

        internal static MapHeightWrite[] Corners(int i0, int i1, int j0, int j1, int raw)
        {
            var list = new List<MapHeightWrite>();
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++) list.Add(new MapHeightWrite(i, j, raw));
            return list.ToArray();
        }

        internal static MapSurfaceWrite[] Cells(int x0, int x1, int z0, int z1, string name)
        {
            var list = new List<MapSurfaceWrite>();
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) list.Add(new MapSurfaceWrite(x, z, name));
            return list.ToArray();
        }
    }
}
