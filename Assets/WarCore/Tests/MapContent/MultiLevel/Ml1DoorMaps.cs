using System.IO;
using System.Linq;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // ML-1 demo maps. Each stored description is exactly what the door prefab stamps onto its base description; the
    // tests compare the file with this rebuild, so a map and the prefab cannot drift apart.
    internal static class Ml1DoorMaps
    {
        internal const string DoorPath = "Assets/WarCore/Maps/door-demo.map.json";
        internal const string TerrainPath = "Assets/WarCore/Maps/door-terrain-demo.map.json";
        internal const string TerrainSourcePath = "Assets/WarCore/Maps/terrain-demo.map.json";
        internal const long Metre = 65536;

        // door-demo: flat 60 x 40 m, one 3 m wall across the map at x = 30 with a 2-cell doorway at z = 19..20. Bases and
        // points stand at z = 32, so the straight line between the two sides hits a frame and the way across is the doorway.
        internal const int WallX = 30, DoorZ = 19, DoorWidth = 2;

        internal static MapDesc Door()
        {
            var units = new MapUnit[12];
            for (int i = 0; i < 6; i++)
            {
                units[i] = new MapUnit((ulong)(i + 1), "rwr.soldier-1", 1, 1, new MapMm(2000 + 2000 * (i % 3), 29000 + 6000 * (i / 3)), false);
                units[i + 6] = new MapUnit((ulong)(i + 7), "rwr.soldier-1", 2, 2, new MapMm(58000 - 2000 * (i % 3), 29000 + 6000 * (i / 3)), false);
            }
            var start = new MapDesc("door-demo", "none", "rwr-m6", new MapXZ(60, 40), new MapXZ(0, 0), 0, 4 * Metre,
                new[] { new MapLayer("walls", 1) }, null, null, new MapWall[0],
                new[]
                {
                    new MapPoint(1, new MapMm(15000, 32000), 6000, 0, new MapMm(15000, 32000)),
                    new MapPoint(2, new MapMm(45000, 32000), 6000, 0, new MapMm(45000, 32000)),
                },
                new[] { new MapBase(1, 1, new MapMm(4000, 32000)), new MapBase(2, 2, new MapMm(56000, 32000)) },
                units, new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(1, 3, 3, 13));
            var edit = new MapEdit(start);
            edit.Stamp(MapPrefabs.Door(start, "door", new MapXZ(WallX, 0), false, 40, DoorZ, DoorWidth, 3 * Metre, "walls", "wall"));
            return edit.Current;
        }

        // door-terrain-demo: terrain-demo with a 7 m wall across the map at z = 4 and a 2-cell doorway on the hill's west
        // slope at x = 13..14, whose two cells differ in ground height by 0.5 m.
        internal const int SlopeWallZ = 4, SlopeDoorX = 13;

        internal static MapDesc DoorTerrain()
        {
            MapDesc t = MapDescJson.Read(File.ReadAllBytes(TerrainSourcePath));
            var start = new MapDesc("door-terrain-demo", t.Content, t.Pack, t.Size, t.Origin, t.FloorRaw, t.CeilingRaw, t.Layers.ToArray(),
                t.Heights.Select(r => r.ToArray()).ToArray(), t.Surfaces, t.Walls.ToArray(), t.Points.ToArray(), t.Bases.ToArray(),
                t.Units.ToArray(), t.Run, t.Owners.ToArray(), t.HeightOwner?.ToArray(), t.SurfaceOwner?.ToArray(), t.Stamps.ToArray(),
                t.Removed.ToArray(), t.Next);
            var edit = new MapEdit(start);
            edit.Stamp(MapPrefabs.Door(start, "door", new MapXZ(0, SlopeWallZ), true, t.Size.X, SlopeDoorX, DoorWidth, 7 * Metre, "walls", "wall"));
            return edit.Current;
        }
    }
}
