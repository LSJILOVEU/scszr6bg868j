using System.Collections.Generic;
using System.IO;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-E1g: the build code. The map file is what this code makes through the MapDocument facade; the heights of the walls are
    // worked out here from the height table, never copied. Paths are relative to the project root, as in the other map tests.
    internal static class MapE1gBuild
    {
        internal const string Id = "emmental-hamlet-160-ground";
        internal const string Pack = "rwr-field";
        internal const string DemName = "emmental.dem.json";
        internal const string MapsDir = "Assets/WarCore/Maps/";
        internal const string DemPath = MapsDir + DemName;
        internal const string GenPath = MapsDir + Id + ".gen.json";
        internal const string MapPath = MapsDir + Id + ".map.json";

        internal const int Side = 160;
        internal const long CeilingRaw = 2097152;
        internal const ulong Seed = 41;

        // The tile's fingerprint (heights only), and the sha-256 of the whole file (credit text included).
        internal const string DemFingerprint = "a1790885063c9b461fa646c72e965b2780b720e546205ae352f03e2f5bcd06df";
        internal const string DemFileSha = "a1549ed473faacfe05ed6e59b013fad3923cff934ad054223e336c31631dd18f";
        internal const long DemFileBytes = 1530913;
        internal const string DemMetaGuid = "a9a74680a9474a5cbdf4118165648ab7";

        // The generation spec as the card gives it; GenText is its canonical form, which is what the file must hold.
        private const string GenSource = "{ \"version\": 1, \"width\": 160, \"depth\": 160, \"pack\": \"rwr-field\", \"seed\": 0, \"baseMm\": 0, "
            + "\"base\": { \"tile\": \"emmental.dem.json\", \"sha256\": \"" + DemFingerprint + "\", \"x0Mm\": 296000, \"z0Mm\": 346000, "
            + "\"scaleMilli\": 1000, \"exaggerationMilli\": 1000, \"datumMm\": 60 }, \"ground\": \"grass\", \"stamps\": [] }";

        internal static string GenText() => MapGenSpec.Read(GenSource).Write();

        internal static string ReadDem() => File.ReadAllText(DemPath);

        // A new map, then the tile as the ground. Nothing else.
        internal static MapTerrainResult Ground(string gen, string dem)
        {
            MapDocument fresh = MapDocument.New(new MapNewSettings
            {
                Id = Id, Content = "rwr", Pack = Pack, Width = Side, Depth = Side, FloorRaw = 0, CeilingRaw = CeilingRaw, Seed = Seed,
                CrowdPass = "teamIdle", Heights = true, HeightRaw = 0, Surface = "grass",
            });
            return fresh.WithTerrain(gen, dem);
        }

        // Highest height point under the footprint, both far edges included (the rule of MapGate).
        internal static long HighestUnder(IReadOnlyList<IReadOnlyList<int>> heights, int x, int z, int w, int d)
        {
            long top = long.MinValue;
            for (int j = z; j <= z + d; j++)
                for (int i = x; i <= x + w; i++) top = System.Math.Max(top, heights[j][i]);
            return top;
        }

        // The height of a wall on the floor: its top is flat, wanted above the highest point under it, made even.
        internal static long WallHeight(IReadOnlyList<IReadOnlyList<int>> heights, MapE1gLayout.Piece p)
        {
            long h = HighestUnder(heights, p.X, p.Z, p.W, p.D) + p.WantedRaw;
            return h % 2 == 0 ? h : h + 1;
        }

        internal static MapDocument Walls(MapDocument doc)
        {
            IReadOnlyList<IReadOnlyList<int>> heights = doc.Desc.Heights;
            foreach (MapE1gLayout.Piece p in MapE1gLayout.Pieces)
                doc = doc.WithWallAdded(new MapWallInfo("wall", "walls", p.X, p.Z, p.W, p.D, WallHeight(heights, p), false, 0));
            return doc;
        }

        internal static MapDocument PointsAndBases(MapDocument doc)
        {
            foreach (MapE1gLayout.Spot s in MapE1gLayout.Points)
                doc = doc.WithPointAdded(new MapPointInfo(s.XMm, s.ZMm, MapE1gLayout.PointRadiusMm, s.InitialOwner, true, s.SpawnXMm, s.SpawnZMm,
                    false, 0, false, 0));
            for (int k = 0; k < MapE1gLayout.Bases.Length; k++)
                doc = doc.WithBaseAdded(new MapBaseInfo(k + 1, MapE1gLayout.Bases[k][0], MapE1gLayout.Bases[k][1], false, 0));
            return doc;
        }

        internal static MapDocument Soldiers(MapDocument doc)
        {
            for (int team = 1; team <= 2; team++)
            {
                long x = MapE1gLayout.Bases[team - 1][0], z = MapE1gLayout.Bases[team - 1][1];
                doc = doc.WithSquad(MapE1gLayout.Soldier(team), team, team, x, z, MapE1gLayout.OnField, false);
                doc = doc.WithSquad(MapE1gLayout.Soldier(team), team, team, x, z, MapE1gLayout.Reserve, true);
            }
            return doc;
        }

        internal static MapDocument Build() => Build(GenText(), ReadDem());

        internal static MapDocument Build(string gen, string dem) => Soldiers(PointsAndBases(Walls(Ground(gen, dem).Document)));
    }
}
