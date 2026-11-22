namespace WarCore.Tests
{
    // MAP-E1g: the placement list of the ground-only hamlet (160 x 160 window of the Emmental tile): nine solid houses and 53
    // pieces of 2 m cover, in the order of the art list (solids first, then cover). One wall per piece. Cells are map cells
    // (window cell = tile cell minus [296, 346]); WantedMm is the height the art list asks for above the lowest side.
    internal static class MapE1gLayout
    {
        internal sealed class Piece
        {
            internal readonly string Name;
            internal readonly int X, Z, W, D, WantedMm;
            internal Piece(string name, int x, int z, int w, int d, int wantedMm) { Name = name; X = x; Z = z; W = w; D = d; WantedMm = wantedMm; }
            internal long WantedRaw => WantedMm * 65536L / 1000;
        }

        internal const int Solids = 9;

        internal static readonly Piece[] Pieces =
        {
            new Piece("a-barn", 88, 46, 8, 12, 3000),
            new Piece("a-house", 106, 47, 6, 9, 6000),
            new Piece("e-farm", 110, 98, 9, 8, 6000),
            new Piece("e-farm-b", 115, 126, 9, 8, 6000),
            new Piece("e-house", 102, 115, 6, 9, 6000),
            new Piece("b-house", 34, 34, 9, 6, 6000),
            new Piece("c-farm", 26, 90, 9, 8, 6000),
            new Piece("c-barn", 13, 80, 8, 12, 3000),
            new Piece("d-house", 57, 142, 9, 6, 6000),
            new Piece("hedge-1", 121, 101, 1, 5, 2000),
            new Piece("hedge-2", 117, 91, 1, 5, 2000),
            new Piece("hedge-3", 117, 82, 1, 5, 2000),
            new Piece("hedge-4", 117, 72, 1, 5, 2000),
            new Piece("hedge-5", 114, 63, 1, 5, 2000),
            new Piece("hedge-6", 110, 58, 1, 5, 2000),
            new Piece("hedge-7", 69, 133, 5, 1, 2000),
            new Piece("hedge-8", 75, 130, 5, 1, 2000),
            new Piece("hedge-9", 87, 126, 5, 1, 2000),
            new Piece("hedge-10", 94, 124, 5, 1, 2000),
            new Piece("hedge-11", 81, 53, 5, 1, 2000),
            new Piece("hedge-12", 67, 51, 5, 1, 2000),
            new Piece("hedge-13", 57, 52, 5, 1, 2000),
            new Piece("hedge-14", 25, 56, 1, 5, 2000),
            new Piece("hedge-15", 20, 68, 1, 5, 2000),
            new Piece("hedge-16", 10, 81, 1, 5, 2000),
            new Piece("hedge-17", 35, 105, 5, 1, 2000),
            new Piece("hedge-18", 48, 106, 1, 5, 2000),
            new Piece("hedge-19", 57, 116, 1, 5, 2000),
            new Piece("hay-1", 110, 94, 2, 2, 2000),
            new Piece("hay-2", 100, 88, 2, 2, 2000),
            new Piece("hay-3", 108, 78, 2, 2, 2000),
            new Piece("hay-4", 98, 70, 2, 2, 2000),
            new Piece("wood-1", 91, 60, 3, 1, 2000),
            new Piece("hay-5", 99, 64, 2, 2, 2000),
            new Piece("hedge-20", 92, 65, 5, 1, 2000),
            new Piece("hay-6", 106, 60, 2, 2, 2000),
            new Piece("hay-7", 103, 40, 2, 2, 2000),
            new Piece("hedge-21", 52, 48, 1, 5, 2000),
            new Piece("hay-8", 48, 42, 2, 2, 2000),
            new Piece("hay-9", 48, 49, 2, 2, 2000),
            new Piece("hedge-22", 30, 55, 5, 1, 2000),
            new Piece("hay-10", 40, 58, 2, 2, 2000),
            new Piece("wood-2", 29, 44, 1, 3, 2000),
            new Piece("hay-11", 30, 58, 2, 2, 2000),
            new Piece("hedge-23", 27, 87, 5, 1, 2000),
            new Piece("hay-12", 8, 99, 2, 2, 2000),
            new Piece("hay-13", 30, 103, 2, 2, 2000),
            new Piece("hedge-24", 17, 105, 5, 1, 2000),
            new Piece("hay-14", 45, 129, 2, 2, 2000),
            new Piece("hay-15", 57, 123, 2, 2, 2000),
            new Piece("hay-16", 61, 120, 2, 2, 2000),
            new Piece("hay-17", 69, 129, 2, 2, 2000),
            new Piece("hedge-25", 104, 105, 1, 5, 2000),
            new Piece("hay-18", 114, 122, 2, 2, 2000),
            new Piece("hay-19", 100, 108, 2, 2, 2000),
            new Piece("wood-3", 125, 110, 1, 3, 2000),
            new Piece("hedge-26", 90, 15, 5, 1, 2000),
            new Piece("truck-1", 103, 11, 2, 5, 2000),
            new Piece("truck-2", 88, 3, 5, 2, 2000),
            new Piece("hedge-27", 110, 143, 1, 5, 2000),
            new Piece("truck-3", 120, 140, 2, 5, 2000),
            new Piece("truck-4", 110, 156, 5, 2, 2000),
        };

        internal sealed class Spot
        {
            internal readonly string Name;
            internal readonly long XMm, ZMm, SpawnXMm, SpawnZMm;
            internal readonly int InitialOwner;
            internal Spot(string name, long xMm, long zMm, int owner, long spawnXMm, long spawnZMm)
            {
                Name = name; XMm = xMm; ZMm = zMm; InitialOwner = owner; SpawnXMm = spawnXMm; SpawnZMm = spawnZMm;
            }
        }

        internal const int PointRadiusMm = 6000;

        // A to E, ids 1 to 5; cell centres.
        internal static readonly Spot[] Points =
        {
            new Spot("A", 101500, 52500, 1, 109500, 38500),
            new Spot("B", 40500, 48500, 0, 40500, 30500),
            new Spot("C", 20500, 98500, 0, 10500, 89500),
            new Spot("D", 57500, 133500, 0, 61500, 150500),
            new Spot("E", 114500, 113500, 2, 119500, 123500),
        };

        // Team 1 and 2 bases (ids 1 and 2).
        internal static readonly long[][] Bases = { new long[] { 98500, 8500 }, new long[] { 118500, 150500 } };

        internal const int OnField = 40, Reserve = 20;
        internal const string SoldierOne = "rwr.soldier-1", SoldierTwo = "rwr.soldier-2";

        internal static string Soldier(int team) => team == 1 ? SoldierOne : SoldierTwo;
    }
}
