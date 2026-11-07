using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Fixtures of the E2 map gate tests. Every parameter that changes a result is written here, not taken from a default.
    internal sealed class E2Map
    {
        internal const string Soldier = "rwr.soldier-1";
        internal string Pack = "rwr-m6";
        internal int Width = 20, Depth = 10;
        internal byte LayerBit = 1;
        internal int[][] Heights;
        internal MapSurfaces Surfaces;
        internal readonly List<MapWall> Walls = new List<MapWall>();
        internal readonly List<MapPoint> Points = new List<MapPoint>();
        internal readonly List<MapBase> Bases = new List<MapBase>();
        internal readonly List<MapUnit> Units = new List<MapUnit>();

        // A whole-cell wall box on the "walls" layer, 2 m tall; ids follow the insertion order from 1.
        internal E2Map Wall(int x, int z, int sx, int sz, long heightRaw = 131072)
        {
            Walls.Add(new MapWall((ulong)Walls.Count + 1, "wall", "walls", new MapXZ(x, z), new MapXZ(sx, sz), heightRaw));
            return this;
        }

        internal E2Map Point(int id, long xMm, long zMm, MapMm? spawn = null)
        {
            Points.Add(new MapPoint(id, new MapMm(xMm, zMm), 2000, 0, spawn));
            return this;
        }

        internal E2Map Base(int id, int team, long xMm, long zMm)
        {
            Bases.Add(new MapBase(id, team, new MapMm(xMm, zMm)));
            return this;
        }

        internal E2Map Unit(ulong key, long xMm, long zMm, bool reserve = false, string type = Soldier)
        {
            Units.Add(new MapUnit(key, type, 1, 1, new MapMm(xMm, zMm), reserve));
            return this;
        }

        internal MapDesc Desc()
        {
            ulong nextWall = (ulong)Walls.Count + 1, nextUnit = Units.Count == 0 ? 1 : Units.Max(u => u.Key) + 1;
            int nextPoint = Points.Count == 0 ? 1 : Points.Max(p => p.Id) + 1, nextBase = Bases.Count == 0 ? 1 : Bases.Max(b => b.Id) + 1;
            return new MapDesc("e2", "none", Pack, new MapXZ(Width, Depth), new MapXZ(0, 0), 0, 262144,
                new[] { new MapLayer("walls", LayerBit) }, Heights, Surfaces, Walls.ToArray(), Points.ToArray(), Bases.ToArray(),
                Units.ToArray(), new MapRun(41, "none"), new[] { "hand" }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(nextWall, nextPoint, nextBase, nextUnit));
        }

        internal byte[] Bytes() => MapDescJson.Write(Desc());
    }

    internal static class E2Fixtures
    {
        internal const long S = SimFix.Scale;
        internal const string TerrainPath = "Assets/WarCore/Maps/terrain-demo.map.json";
        internal const string M6Path = "Assets/WarCore/Maps/m6-demo.map.json";
        internal const string M7Path = "Assets/WarCore/Maps/m7-demo.map.json";

        // The same text as TerrainDemoMap.PackJson (asserted by E2GateTests), written out so the fixture cannot drift.
        internal const string TerrainPackJson = "{ \"id\": \"rwr-terrain-demo\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"grass\" }, { \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }, { \"id\": \"road\", \"speedMilli\": 1250 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 } }";
        // Mud blocks instead of slowing.
        internal const string MudBlockedPackJson = "{ \"id\": \"rwr-terrain-demo\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"grass\" }, { \"id\": \"mud\", \"passable\": false }, { \"id\": \"road\", \"speedMilli\": 1250 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 } }";
        // The walkable slope limit drops from 3/4 to 2/5, below the ridge's 1/2.
        internal const string SteepPackJson = "{ \"id\": \"rwr-terrain-demo\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"grass\" }, { \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }, { \"id\": \"road\", \"speedMilli\": 1250 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 2, \"maxWalkableDen\": 5 } }";

        internal static ContentPack TerrainPack(string json = TerrainPackJson) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["rwr-terrain-demo"] = json }, FileContentSource.Rwr()), "rwr-terrain-demo");

        internal static ContentPack M6Pack() => WarCore.Content.Rwr.RwrPack.Load("rwr-m6");

        internal static MapDesc TerrainDesc() => MapDescJson.Read(File.ReadAllBytes(TerrainPath));

        // The terrain demo description with its points, bases and surface rows replaced; everything else as on disk.
        internal static byte[] TerrainVariant(MapPoint[] points, MapBase[] bases, string[] rows = null)
        {
            MapDesc d = TerrainDesc();
            MapSurfaces surfaces = rows == null ? d.Surfaces : new MapSurfaces(d.Surfaces.Names.ToArray(), rows);
            return MapDescJson.Write(new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(),
                d.Heights.Select(r => r.ToArray()).ToArray(), surfaces, d.Walls.ToArray(), points, bases, d.Units.ToArray(), d.Run,
                d.Owners.ToArray(), null, null, d.Stamps.ToArray(), d.Removed.ToArray(), d.Next));
        }

        // Anchors of TerrainDemoMap: spawns at cells (3,16) and (44,16), control points at (22,16) and (38,8), cell centres.
        internal static readonly MapPoint CrestPoint = new MapPoint(1, new MapMm(22500, 16500), 6000, 0);
        internal static readonly MapPoint MudPoint = new MapPoint(2, new MapMm(38500, 8500), 6000, 0);
        internal static readonly MapBase WestBase = new MapBase(1, 1, new MapMm(3500, 16500));
        internal static readonly MapBase EastBase = new MapBase(2, 2, new MapMm(44500, 16500));

        // Wp22TerrainTests.Anchors_AMudBandThatBlocks_CutsTheOnlyRoad: surface column x 41..42 is mud ('B'), only the crest point.
        internal static byte[] MudBand()
        {
            MapDesc d = TerrainDesc();
            var rows = new string[d.Size.Z];
            for (int z = 0; z < rows.Length; z++)
            {
                char[] row = d.Surfaces.Rows[z].ToCharArray();
                row[41] = 'B'; row[42] = 'B';
                rows[z] = new string(row);
            }
            return TerrainVariant(new[] { CrestPoint }, new[] { WestBase, EastBase }, rows);
        }

        internal static byte[] TerrainDemo() => TerrainVariant(new[] { CrestPoint, MudPoint }, new[] { WestBase, EastBase });

        // Wp22: a spawn inside the wall cell (6,10).
        internal static byte[] WallAnchor() =>
            TerrainVariant(new[] { CrestPoint, MudPoint }, new[] { new MapBase(1, 1, new MapMm(6500, 10500)), EastBase });

        // Wp22: a control point on the plateau cell (20,25) that the cliffs cut off.
        internal static byte[] Plateau() =>
            TerrainVariant(new[] { new MapPoint(1, new MapMm(20500, 25500), 6000, 0) }, new[] { WestBase, EastBase });

        internal static SimVec3 Mm(long xMm, long zMm) => new SimVec3(SimFix.Ratio(xMm, 1000), SimFix.FromRaw(0), SimFix.Ratio(zMm, 1000));
    }
}
