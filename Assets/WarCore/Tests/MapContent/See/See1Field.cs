using System;
using System.Linq;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // SEE-1 test grounds: flat 64 x 64 m maps holding only walls (cell x, cell z, cells wide, cells deep, height in metres).
    internal static class See1Field
    {
        private const long M = SimFix.Scale;
        // The default camera (yaw 45, pitch 45): looking back from a point the line rises 1 m for each 0.7071 m towards -x and
        // -z each.
        internal static readonly (float X, float Y, float Z) Forward = new IsoCamera(12f).Forward;

        internal static MapDesc Desc(params (int X, int Z, int W, int D, double H)[] walls)
        {
            var list = new MapWall[walls.Length];
            for (int i = 0; i < list.Length; i++)
                list[i] = new MapWall((ulong)(i + 1), "wall", "walls", new MapXZ(walls[i].X, walls[i].Z), new MapXZ(walls[i].W, walls[i].D),
                    (long)Math.Round(walls[i].H * M / 2) * 2); // a wall's raw height is even
            return new MapDesc("see-1-field", "none", "rwr-m6", new MapXZ(64, 64), new MapXZ(0, 0), 0, 16 * M,
                new[] { new MapLayer("walls", 1) }, null, null, list, new MapPoint[0], new MapBase[0], new MapUnit[0],
                new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext((ulong)list.Length + 1, 1, 1, 1));
        }

        internal static ViewMap Map(params (int X, int Z, int W, int D, double H)[] walls) =>
            ViewMap.Parse(MapBake.Run(MapDescJson.Write(Desc(walls))).WcMapBytes);

        // Ground T: building A on x 10..30, z 10..14, 6 m high; a pillar on x 40..41, z 10..11, 6 m high.
        internal static ViewMap T() => Map((10, 10, 20, 4, 6), (40, 10, 1, 1, 6));

        // The demo house (PlateDemoMap) with more walls and plates; the ceiling is raised to 16 m.
        internal static (ViewPlates Plates, ViewMap Map) House(MapWall[] walls = null, MapPlate[] plates = null)
        {
            MapDesc d = PlateDemoMap.Desc();
            byte[] bytes = MapBake.Run(MapDescJson.Write(new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, 16 * M,
                d.Layers.ToArray(), null, null, d.Plates.Concat(plates ?? new MapPlate[0]).ToArray(), d.Cutouts.ToArray(),
                d.Walls.Concat(walls ?? new MapWall[0]).ToArray(), d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(), d.Run,
                d.Owners.ToArray(), null, null, d.Stamps.ToArray(), d.Removed.ToArray(), new MapNext(21, 3, 3, 5, 20, 2)))).WcMapBytes;
            return (ViewPlates.From(WcMapReader.Read(bytes)), ViewMap.Parse(bytes));
        }

        // A single wall raised off the ground (bottom and height in metres).
        internal static ViewMap Raised(int x, int z, int w, int d, double bottom, double height)
        {
            MapDesc f = Desc();
            var wall = new MapWall(1, "wall", "walls", new MapXZ(x, z), new MapXZ(w, d), (long)Math.Round(height * M / 2) * 2, null, null,
                (long)Math.Round(bottom * M));
            return ViewMap.Parse(MapBake.Run(MapDescJson.Write(new MapDesc(f.Id, f.Content, f.Pack, f.Size, f.Origin, 0, 16 * M,
                f.Layers.ToArray(), null, null, new[] { wall }, f.Points.ToArray(), f.Bases.ToArray(), f.Units.ToArray(), f.Run,
                f.Owners.ToArray(), null, null, f.Stamps.ToArray(), f.Removed.ToArray(), new MapNext(2, 1, 1, 1)))).WcMapBytes);
        }

        internal static ViewEntity At(float x, float z, ulong key = 1, int team = 1, bool alive = true, float y = 0f, int layer = 0) =>
            new ViewEntity(key, 0, x, y, z, team: team, alive: alive, layer: layer);
    }
}
