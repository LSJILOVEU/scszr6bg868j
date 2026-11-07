using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // Hand-written JSON fixtures of the E3 edit tests. Every value that changes a result is written here: an 8 x 6 map, flat
    // heights, all "grass" (table grass, mud, road), one "walls" layer, no slope anywhere near the 2/1 limit.
    internal static class E3Fixtures
    {
        internal const int W = 8, D = 6;
        internal const long WallHeight = 131072;
        internal const string Soldier = "rwr.soldier-1";

        internal static string Map(string walls = "", string units = "", string owners = "\"hand\"", string stamps = "",
            string removed = "", string next = "1, 1, 1, 1", int originX = 0, int originZ = 0)
        {
            string[] n = next.Split(',');
            var sb = new StringBuilder();
            sb.Append("{\"format\": \"warcore.map\", \"version\": 1, \"id\": \"e3\", \"content\": \"rwr\", \"pack\": \"rwr-m6\",");
            sb.Append(" \"size\": [" + W + ", " + D + "], \"origin\": [" + originX + ", " + originZ + "],");
            sb.Append(" \"floorRaw\": 0, \"ceilingRaw\": 262144, \"layers\": [{\"name\": \"walls\", \"bit\": 1}],");
            sb.Append(" \"heights\": [");
            for (int j = 0; j <= D; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append('[');
                for (int i = 0; i <= W; i++) sb.Append(i == 0 ? "0" : ", 0");
                sb.Append(']');
            }
            sb.Append("], \"surfaces\": {\"names\": [\"grass\", \"mud\", \"road\"], \"rows\": [");
            for (int z = 0; z < D; z++) sb.Append(z == 0 ? "\"AAAAAAAA\"" : ", \"AAAAAAAA\"");
            sb.Append("]}, \"walls\": [" + walls + "], \"points\": [], \"bases\": [], \"units\": [" + units + "],");
            sb.Append(" \"run\": {\"seed\": 7, \"crowdPass\": \"none\"}, \"owners\": [" + owners + "],");
            sb.Append(" \"stamps\": [" + stamps + "], \"removed\": [" + removed + "],");
            sb.Append(" \"next\": {\"wall\": " + n[0].Trim() + ", \"point\": " + n[1].Trim() + ", \"base\": " + n[2].Trim()
                + ", \"unit\": " + n[3].Trim() + "}}");
            return sb.ToString();
        }

        internal static string Plain() => Map();

        // A stamps row (s1, wall, 5 -> id 3) with no wall carrying that src and no tombstone.
        internal static string RowWithoutWall() => Map(stamps: Row("s1", "wall", 5, 3), next: "4, 1, 1, 1");

        internal static string ThreeWalls() => Map(walls: WallJson(1, 5, 4, 0) + ", " + WallJson(2, 6, 4, 0) + ", " + WallJson(3, 7, 4, 0),
            next: "4, 1, 1, 1");

        internal const string WallLimit = "18446744073709551615";

        internal static string WallPoolFull() => Map(next: WallLimit + ", 1, 1, 1");

        // origin (-4, -4); a hand unit at (-1, -1) mm, which is cell (3, 3).
        internal static string NegativeOrigin() => Map(units: UnitJson(1, -1, -1, 0), next: "1, 1, 1, 2", originX: -4, originZ: -4);

        // 64 owners: hand, stamp:s1, o2 ... o63.
        internal static string Owners64()
        {
            var sb = new StringBuilder("\"hand\", \"stamp:s1\"");
            for (int n = 2; n < 64; n++) sb.Append(", \"o" + n + "\"");
            return Map(owners: sb.ToString());
        }

        // Read accepts these; MapEdit rejects each with MAPEDIT_LINK wall.
        internal static string LinkSrcRepeated() => Map(walls: WallJson(1, 0, 0, null, Src("s1", "wall", 5)) + ", "
            + WallJson(2, 1, 0, null, Src("s1", "wall", 5)), next: "3, 1, 1, 1");

        internal static string LinkRowNotBelowNext() => Map(stamps: Row("s1", "wall", 7, 3), next: "1, 1, 1, 1");

        internal static string LinkIdTaken() => Map(walls: WallJson(2, 0, 0, null, Src("s1", "wall", 8)),
            stamps: Row("s1", "wall", 7, 2), next: "3, 1, 1, 1");

        internal static string LinkRowIdRepeated() => Map(stamps: Row("s1", "wall", 7, 2) + ", " + Row("s1", "wall", 8, 2),
            next: "3, 1, 1, 1");

        // Same id across kinds is not a conflict: wall 1 without src, unit 1 with the src of the unit row id 1.
        internal static string LinkAcrossKinds() => Map(walls: WallJson(1, 0, 0), units: UnitJson(1, 500, 500, null, Src("s1", "unit", 4)),
            stamps: Row("s1", "unit", 4, 1), next: "2, 1, 1, 2");

        internal static readonly string[] All =
        {
            Plain(), RowWithoutWall(), ThreeWalls(), WallPoolFull(), NegativeOrigin(), Owners64(),
            LinkSrcRepeated(), LinkRowNotBelowNext(), LinkIdTaken(), LinkRowIdRepeated(), LinkAcrossKinds(),
        };

        internal static string Src(string instance, string kind, ulong src) =>
            "{\"instance\": \"" + instance + "\", \"kind\": \"" + kind + "\", \"src\": " + src + "}";

        internal static string Row(string instance, string kind, ulong src, ulong id) =>
            "{\"instance\": \"" + instance + "\", \"kind\": \"" + kind + "\", \"src\": " + src + ", \"id\": " + id + "}";

        internal static string WallJson(ulong id, int x, int z, int? owner = null, string src = null) =>
            "{\"id\": " + id + ", \"kind\": \"wall\", \"layer\": \"walls\", \"cell\": [" + x + ", " + z + "], \"size\": [1, 1],"
            + " \"heightRaw\": " + WallHeight + (owner.HasValue ? ", \"owner\": " + owner.Value : "")
            + (src != null ? ", \"src\": " + src : "") + "}";

        internal static string UnitJson(ulong key, long x, long z, int? owner = null, string src = null) =>
            "{\"key\": " + key + ", \"type\": \"" + Soldier + "\", \"team\": 1, \"squad\": 0, \"at\": [" + x + ", " + z + "],"
            + " \"reserve\": false" + (owner.HasValue ? ", \"owner\": " + owner.Value : "")
            + (src != null ? ", \"src\": " + src : "") + "}";

        internal static MapDesc Read(string json) => MapDescJson.Read(E1aSamples.Utf8(json));

        internal static MapWall WallAt(int x, int z, ulong id = 0) =>
            new MapWall(id, "wall", "walls", new MapXZ(x, z), new MapXZ(1, 1), WallHeight);

        internal static MapUnit UnitAt(long x, long z) => new MapUnit(0, Soldier, 1, 0, new MapMm(x, z), false);

        internal static MapPoint PointAt(long x, long z) => new MapPoint(0, new MapMm(x, z), 400, 0, new MapMm(x, z));

        internal static MapBase BaseAt(long x, long z) => new MapBase(0, 1, new MapMm(x, z));

        // Owner grid character of a height point or surface cell, read from the description itself ('.' when no grid).
        internal static char HeightChar(MapDesc d, int i, int j) => d.HeightOwner == null ? '.' : d.HeightOwner[j][i];
        internal static char SurfaceChar(MapDesc d, int x, int z) => d.SurfaceOwner == null ? '.' : d.SurfaceOwner[z][x];

        internal static string OwnerOf(MapDesc d, char c) =>
            c == '.' ? null : d.Owners["ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/".IndexOf(c)];

        internal static int CountHeights(MapDesc d, string owner)
        {
            int n = 0;
            for (int j = 0; j <= D; j++)
                for (int i = 0; i <= W; i++) if (OwnerOf(d, HeightChar(d, i, j)) == owner) n++;
            return n;
        }

        internal static int CountSurfaces(MapDesc d, string owner)
        {
            int n = 0;
            for (int z = 0; z < D; z++)
                for (int x = 0; x < W; x++) if (OwnerOf(d, SurfaceChar(d, x, z)) == owner) n++;
            return n;
        }

        internal static string SurfaceName(MapDesc d, int x, int z) =>
            d.Surfaces.Names["ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/".IndexOf(d.Surfaces.Rows[z][x])];

        // "Extract": the owner's height points (i, j, raw) and surface cells (x, z, name) as lines, then its walls, points,
        // bases and units as the canonical JSON the description writer gives them, in order, as UTF-8 bytes.
        internal static byte[] Extract(MapDesc d, string owner)
        {
            var sb = new StringBuilder();
            for (int j = 0; j <= D; j++)
                for (int i = 0; i <= W; i++)
                    if (OwnerOf(d, HeightChar(d, i, j)) == owner) sb.Append("h " + i + " " + j + " " + d.Heights[j][i] + "\n");
            for (int z = 0; z < D; z++)
                for (int x = 0; x < W; x++)
                    if (OwnerOf(d, SurfaceChar(d, x, z)) == owner) sb.Append("s " + x + " " + z + " " + SurfaceName(d, x, z) + "\n");
            var only = new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null,
                d.Walls.Where(w => EntryOwner(d, w.Owner) == owner).ToArray(), d.Points.Where(p => EntryOwner(d, p.Owner) == owner).ToArray(),
                d.Bases.Where(b => EntryOwner(d, b.Owner) == owner).ToArray(), d.Units.Where(u => EntryOwner(d, u.Owner) == owner).ToArray(),
                d.Run, d.Owners.ToArray(), null, null, new MapStamp[0], new MapSrc[0], d.Next);
            string text = MapDescJson.WriteText(only);
            int from = text.IndexOf("  \"walls\":", StringComparison.Ordinal);
            int to = text.IndexOf("  \"run\":", StringComparison.Ordinal);
            sb.Append(text, from, to - from);
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        private static string EntryOwner(MapDesc d, int? owner) => owner.HasValue ? d.Owners[owner.Value] : null;
    }
}
