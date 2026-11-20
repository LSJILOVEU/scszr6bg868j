using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ED-6a fixtures: the repository maps as documents, the M6 empty map, a cartridge whose layer has a combat body, and a
    // synthetic description that fills every optional entry field.
    internal static class Ed6aFixtures
    {
        internal const long M = Ml4Maps.M;
        internal const string MapDir = "Assets/WarCore/Maps/";
        internal const string DoorPath = MapDir + "door-demo.map.json";
        internal const string DoorTerrainPath = MapDir + "door-terrain-demo.map.json";
        internal const string LanesPath = MapDir + "lanes-playtest.map.json";
        internal const string M6Path = MapDir + "m6-demo.map.json";
        internal const string PlatePath = MapDir + "plate-demo.map.json";

        internal static readonly string[] AllMaps =
        {
            DoorPath, DoorTerrainPath, LanesPath, M6Path, MapDir + "m7-demo.map.json", PlatePath, MapDir + "terrain-demo.map.json",
        };

        internal static readonly string[] Kinds = { "wall", "point", "base", "unit", "plate", "cutout" };

        internal static MapDocument Open(string path) => MapDocument.Open(File.ReadAllBytes(path));

        internal static MapDocument Open(MapDesc desc) => MapDocument.Open(MapDescJson.Write(desc));

        internal static MapDesc Desc(MapDocument doc) => MapDescJson.Read(doc.Save());

        // The M6 demo's settings: 80 x 40, floor 0, ceiling 4 m, seed 41, teamIdle, layer rwr-m6, no heights, no surfaces.
        internal static MapNewSettings M6Settings() => new MapNewSettings
        {
            Id = "m6-demo", Content = "none", Pack = "rwr-m6", Width = 80, Depth = 40, FloorRaw = 0, CeilingRaw = 262144, Seed = 41,
            CrowdPass = "teamIdle",
        };

        // An empty flat map on the given layer.
        internal static MapDocument Empty(string pack, int width = 30, int depth = 20) => MapDocument.New(new MapNewSettings
        {
            Id = "ed6a", Content = "none", Pack = pack, Width = width, Depth = depth, FloorRaw = 0, CeilingRaw = 4 * M, Seed = 7,
        });

        internal static MapWallInfo Wall(int x, int z, int w, int d, long heightRaw = 2 * M) =>
            new MapWallInfo("wall", "walls", x, z, w, d, heightRaw, false, 0);

        internal static MapPointInfo Point(long x, long z) => new MapPointInfo(x, z, 4000, 0, true, x, z, false, 0, false, 0);

        internal static MapBaseInfo Base(int team, long x, long z) => new MapBaseInfo(team, x, z, false, 0);

        internal static MapUnitInfo Unit(long x, long z, int team = 1, int squad = 1) =>
            new MapUnitInfo("rwr.soldier-1", team, squad, x, z, false, false, 0);

        internal static MapPlateInfo Flat(int x, int z, int w, int d, long yRaw) =>
            new MapPlateInfo(x, z, w, d, false, yRaw, "", 0, 0, M / 2, "");

        internal static MapCutoutInfo Cut(ulong plate, int x, int z, int w, int d) => new MapCutoutInfo(plate, x, z, w, d);

        // A 12 x 8 description (all grass, surface table grass and mud) with origin (-5, -3) whose entries fill every optional field once, each next to an entry that
        // leaves it empty: wall 1 raised (baseRaw), wall 2 on the floor; plate 1 flat with a surface, plate 2 sloped without;
        // cutout 1 in plate 1; point 1 on plate 1 with a spawn on plate 1, point 2 with a spawn on the ground, point 3 without
        // spawn; base 1 on plate 1, base 2 on the ground; unit 1 on plate 1, unit 2 at negative millimetres on the ground. Owners
        // ["hand", "stamp:s"]: wall 2 is stamp-owned with a src, point 2 hand-owned, the rest unowned.
        internal static MapDesc Synthetic()
        {
            var plates = new[]
            {
                new MapPlate(1, new MapXZ(0, 0), new MapXZ(4, 3), 2 * M, null, M / 2, "grass"),
                new MapPlate(2, new MapXZ(7, 4), new MapXZ(2, 3), null, new MapSlope("z", M, 2 * M), M / 2),
            };
            var cutouts = new[] { new MapCutout(1, 1, new MapXZ(3, 2), new MapXZ(1, 1)) };
            var walls = new[]
            {
                new MapWall(1, "wall", "walls", new MapXZ(10, 0), new MapXZ(1, 2), 2 * M, null, null, M),
                new MapWall(2, "wall", "walls", new MapXZ(10, 5), new MapXZ(1, 2), 2 * M, 1, new MapSrc("s", "wall", 1)),
            };
            var points = new[]
            {
                new MapPoint(1, new MapMm(-4500, -2500), 1000, 0, new MapMm(-3500, -1500), null, null, 1, 1),
                new MapPoint(2, new MapMm(1500, 1500), 2000, 1, new MapMm(2500, 1500), 0),
                new MapPoint(3, new MapMm(3500, 2500), 3000, 2),
            };
            var bases = new[] { new MapBase(1, 1, new MapMm(-4500, -1500), null, null, 1), new MapBase(2, 2, new MapMm(5500, 3500)) };
            var units = new[]
            {
                new MapUnit(1, "rwr.soldier-1", 1, 1, new MapMm(-2500, -2500), false, null, null, 1),
                new MapUnit(2, "rwr.soldier-2", 2, 2, new MapMm(-1500, -500), true),
            };
            var desc = new MapDesc("ed6a-synthetic", "none", "rwr-m6", new MapXZ(12, 8), new MapXZ(-5, -3), 0, 4 * M,
                new[] { new MapLayer("walls", 1) }, null, new MapSurfaces(new[] { "grass", "mud" }, Rows(12, 8, 'A')), plates, cutouts, walls, points, bases, units, new MapRun(3, "none"),
                new[] { MapEdit.Hand, "stamp:s" }, null, null, new[] { new MapStamp("s", "wall", 1, 2) }, new MapSrc[0],
                new MapNext(3, 4, 3, 3, 3, 2));
            return MapDescJson.Read(MapDescJson.Write(desc));
        }

        private static string[] Rows(int width, int depth, char c)
        {
            var rows = new string[depth];
            for (int z = 0; z < depth; z++) rows[z] = new string(c, width);
            return rows;
        }

        // Bit (x, z) of a baked map's passability bitmap: true when walkable.
        internal static bool Passable(WcMap map, int x, int z)
        {
            int i = z * (int)map.Width + x;
            return (map.Passability[i >> 3] & (1 << (i & 7))) != 0;
        }

        internal static WcMap Baked(MapDocument doc) => WcMapReader.Read(MapBake.Run(doc.Save()).WcMapBytes);

        // Stamps the plan into the document's description directly through MapEdit, for byte comparisons with the facade.
        internal static byte[] StampedBytes(MapDocument doc, Func<MapDesc, MapStampPlan> plan)
        {
            var edit = new MapEdit(Desc(doc));
            edit.Stamp(plan(edit.Current));
            return MapDescJson.Write(edit.Current);
        }

        internal static List<string> Strings(IReadOnlyList<MapEntryRef> refs)
        {
            var list = new List<string>();
            foreach (MapEntryRef r in refs) list.Add(r.Kind + " " + r.Id);
            return list;
        }

        internal static void SameBytes(MapDocument a, MapDocument b) => Assert.That(a.Save(), Is.EqualTo(b.Save()));
    }

    // A throw-away cartridge: a copy of every rwr layer plus "ed6a-body", which extends rwr and adds a combat body (the ML-3
    // test values: height 1.75 m, eye 1.5 m, muzzle 1.5 m). WARCORE_CONTENT_DIRS points at it until Dispose restores both
    // catalog variables (Ed0Dirs).
    internal sealed class Ed6aBodyCartridge : IDisposable
    {
        internal const string Layer = "ed6a-body";
        private readonly Ed0Dirs dirs = new Ed0Dirs();

        internal Ed6aBodyCartridge()
        {
            string dir = dirs.Dir("Body");
            foreach (string file in Directory.GetFiles(FileContentSource.RwrDirectory, "*.json"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(dir, Layer + ".json"), "{ \"id\": \"" + Layer + "\", \"extends\": \"rwr\", "
                + "\"combat\": { \"body\": { \"height\": 1.75, \"eye\": 1.5, \"muzzle\": 1.5 } } }");
            Ed0Dirs.UseList(dir);
        }

        internal CombatBody Body => ContentCatalog.Find().Load(Layer).Body.Value;

        public void Dispose() => dirs.Dispose();
    }
}
