using System.Collections.Generic;
using System.Reflection;
using System.Text;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // Small hand-written descriptions for the E1b tests. Every value that changes behaviour is written here, not taken from defaults.
    internal static class E1bSamples
    {
        internal const string TerrainPack = "rwr-terrain-demo";
        // 96 m wide so the two soldiers stay beyond the 35 m sight of rwr.json for the 90 ticks the terrain cases run.
        internal const int Width = 96, Depth = 8;
        // Height field of the terrain cases: 1 m everywhere except the last point column (2 m), so cells x < 95 are flat at 1 m.
        internal const int PlateauRaw = 65536, EdgeRaw = 131072;

        internal static string Heights()
        {
            var rows = new List<string>();
            for (int j = 0; j <= Depth; j++)
            {
                var row = new List<string>();
                for (int i = 0; i <= Width; i++) row.Add((i == Width ? EdgeRaw : PlateauRaw).ToString());
                rows.Add("[" + string.Join(", ", row) + "]");
            }
            return "[" + string.Join(", ", rows) + "]";
        }

        // Grass everywhere, road on row z = 4.
        internal static string Surfaces()
        {
            var rows = new List<string>();
            for (int z = 0; z < Depth; z++) rows.Add("\"" + new string(z == 4 ? 'B' : 'A', Width) + "\"");
            return "{ \"names\": [\"grass\", \"road\"], \"rows\": [" + string.Join(", ", rows) + "] }";
        }

        internal static string Unit(ulong key, string type, int team, int squad, long x, long z, bool reserve) =>
            "{ \"key\": " + key + ", \"type\": \"" + type + "\", \"team\": " + team + ", \"squad\": " + squad
            + ", \"at\": [" + x + ", " + z + "], \"reserve\": " + (reserve ? "true" : "false") + " }";

        internal static string Desc(string pack, string heights, string surfaces, string points, string bases, string units,
            ulong seed, string crowdPass, int nextPoint, int nextBase, ulong nextUnit)
        {
            var b = new StringBuilder();
            b.Append("{ \"format\": \"warcore.map\", \"version\": 1, \"id\": \"e1b\", \"content\": \"rwr\", \"pack\": \"").Append(pack).Append("\", ");
            b.Append("\"size\": [").Append(Width).Append(", ").Append(Depth).Append("], \"origin\": [0, 0], \"floorRaw\": 0, \"ceilingRaw\": 262144, ");
            b.Append("\"layers\": [ { \"name\": \"walls\", \"bit\": 1 } ], ");
            if (heights != null) b.Append("\"heights\": ").Append(heights).Append(", ");
            if (surfaces != null) b.Append("\"surfaces\": ").Append(surfaces).Append(", ");
            b.Append("\"walls\": [], \"points\": [").Append(points).Append("], \"bases\": [").Append(bases).Append("], ");
            b.Append("\"units\": [").Append(units).Append("], ");
            b.Append("\"run\": { \"seed\": ").Append(seed).Append(", \"crowdPass\": \"").Append(crowdPass).Append("\" }, ");
            b.Append("\"owners\": [\"hand\"], \"stamps\": [], \"removed\": [], ");
            b.Append("\"next\": { \"wall\": 1, \"point\": ").Append(nextPoint).Append(", \"base\": ").Append(nextBase)
                .Append(", \"unit\": ").Append(nextUnit).Append(" } }\n");
            return b.ToString();
        }

        // One control point mid-map, a base at each end, one soldier per team (both deployed).
        internal const string OnePoint = "{ \"id\": 1, \"at\": [48500, 4500], \"radiusMm\": 3000, \"initialOwner\": 0 }";
        internal const string TwoBases = "{ \"id\": 1, \"team\": 1, \"at\": [1500, 4500] }, { \"id\": 2, \"team\": 2, \"at\": [94500, 4500] }";
        internal static readonly string TwoSoldiers = Unit(1, "rwr.soldier-1", 1, 1, 3500, 4500, false) + ", "
            + Unit(2, "rwr.soldier-2", 2, 2, 92500, 4500, false);

        internal static string TerrainCase(bool heights, bool surfaces) =>
            Desc(TerrainPack, heights ? Heights() : null, surfaces ? Surfaces() : null, OnePoint, TwoBases, TwoSoldiers, 41, "none", 2, 3, 3);

        internal static SimSession Build(WorldRecipe recipe, byte[] map, bool reverse) =>
            (SimSession)typeof(WorldBuilder).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { recipe, map, reverse });
    }
}
