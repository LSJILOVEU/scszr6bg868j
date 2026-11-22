using System;
using System.Collections.Generic;

namespace WarCore.Presentation
{
    // The side of a shop its door opens to: East = +X, West = -X, North = +Z, South = -Z.
    public enum WardFacing : byte { East = 0, West = 1, North = 2, South = 3 }

    // A box of whole cells: first cell (X, Z) and size in cells (W along X, D along Z).
    public readonly struct WardBox : IEquatable<WardBox>
    {
        public readonly int X, Z, W, D;
        public WardBox(int x, int z, int w, int d) { X = x; Z = z; W = w; D = d; }
        public int X1 => X + W - 1;
        public int Z1 => Z + D - 1;
        public bool Equals(WardBox other) => X == other.X && Z == other.Z && W == other.W && D == other.D;
        public override bool Equals(object obj) => obj is WardBox other && Equals(other);
        public override int GetHashCode() => ((X * 397 + Z) * 397 + W) * 397 + D;
        public override string ToString() => "(" + X + ", " + Z + ") " + W + "x" + D;
    }

    // One shop: 6 cells along its street and 5 deep, walls one cell thick. Front is the street-side column (East, West) or
    // row (North, South); Start is its first cell along the street. The street face is a door part (frame, door, frame) at
    // Start .. Start + 2 and a window part (frame, window, frame) at Start + 3 .. Start + 5; the back wall is 4 cells behind
    // the front, and the two sides close the cells between.
    public readonly struct WardShop : IEquatable<WardShop>
    {
        public readonly WardFacing Facing;
        public readonly int Front, Start;
        public WardShop(WardFacing facing, int front, int start) { Facing = facing; Front = front; Start = start; }
        public bool AlongZ => Facing == WardFacing.East || Facing == WardFacing.West;
        // +1 when the door opens to growing X or Z, else -1.
        public int Out => Facing == WardFacing.East || Facing == WardFacing.North ? 1 : -1;
        public int Back => Front - 4 * Out;
        // The footprint of all ten walls.
        public WardBox Box
        {
            get
            {
                int low = Math.Min(Front, Back);
                return AlongZ ? new WardBox(low, Start, 5, WardLayout.ShopLength) : new WardBox(Start, low, WardLayout.ShopLength, 5);
            }
        }
        // The doorway cell, the cell in front of it on the street, and the cell behind it inside the shop.
        public (int X, int Z) Door => AlongZ ? (Front, Start + 1) : (Start + 1, Front);
        public (int X, int Z) Outside(int step) => AlongZ ? (Front + step * Out, Start + 1) : (Start + 1, Front + step * Out);
        public (int X, int Z) Inside => AlongZ ? (Front - Out, Start + 1) : (Start + 1, Front - Out);
        public (int X, int Z) Window => AlongZ ? (Front, Start + 4) : (Start + 4, Front);
        public bool Equals(WardShop other) => Facing == other.Facing && Front == other.Front && Start == other.Start;
        public override bool Equals(object obj) => obj is WardShop other && Equals(other);
        public override int GetHashCode() => ((int)Facing * 397 + Front) * 397 + Start;
        public override string ToString() => Facing + " " + Front + " " + Start;
    }

    // MAP-BL0: the layout of the walled ward (walled-ward-160) as numbers only. WardBuild turns it into a map description.
    // Cell = 1 m, x east, z north. The skeleton (ward wall, gates, streets, square, points, bases, soldiers) is constant; the
    // pieces in the lists are this map's choice. Default is a new copy each time, so a test may change one list and build it.
    public sealed class WardLayout
    {
        public const string MapId = "walled-ward-160", Content = "rwr", Pack = "rwr-showcase", CrowdPass = "teamIdle";
        public const int Side = 160;
        public const long FloorRaw = 0, CeilingRaw = 1048576;
        public const ulong Seed = 41;

        // Heights (raw, 65536 = 1 m). Settable so that a test can build a copy with one height changed.
        public long WardWallRaw { get; set; } = 196608;
        public long ShopTopRaw { get; set; } = 196608;
        public long HouseRaw { get; set; } = 196608;
        public long StallRaw { get; set; } = 65536;
        public long HandcartRaw { get; set; } = 65536;
        public long OxcartRaw { get; set; } = 131072;
        public long WellRaw { get; set; } = 65536;
        public long TreeRaw { get; set; } = 196608;

        // Kinds (looks only; the simulation and the AI do not read them).
        public const string WardWallKind = "ward-wall", ShopKind = "wall", HouseKind = "solid-house", StallKind = "stall",
            HandcartKind = "handcart", OxcartKind = "oxcart", WellKind = "well", TreeKind = "tree";

        public const int ShopLength = 6;

        // The ward wall, in id order: west south, west north, east south, east north, south west, south east, north west,
        // north east. Two cells thick; the four gates are the 4-cell gaps at 78..81.
        public static readonly IReadOnlyList<WardBox> WardWalls = Array.AsReadOnly(new[]
        {
            new WardBox(8, 8, 2, 70), new WardBox(8, 82, 2, 70), new WardBox(150, 8, 2, 70), new WardBox(150, 82, 2, 70),
            new WardBox(10, 8, 68, 2), new WardBox(82, 8, 68, 2), new WardBox(10, 150, 68, 2), new WardBox(82, 150, 68, 2),
        });

        public const int GateLow = 78, GateHigh = 81;
        // Ring road outside the wall: cells 0..RingIn and RingOut..159 on either axis. Lane inside the wall: LaneLow..LaneIn
        // and LaneOut..LaneHigh.
        public const int RingIn = 7, RingOut = 152, LaneLow = 10, LaneIn = 13, LaneOut = 146, LaneHigh = 149;
        // The main streets (north-south at x, east-west at z): StreetLow..StreetHigh, the carriageway CarriageLow..CarriageHigh.
        public const int StreetLow = 73, StreetHigh = 86, CarriageLow = 77, CarriageHigh = 82;
        // The square.
        public const int SquareX0 = 68, SquareX1 = 91, SquareZ0 = 111, SquareZ1 = 134;
        // The four blocks lie in BlockLow..BlockEdgeLow or BlockEdgeHigh..BlockHigh on each axis.
        public const int BlockLow = 14, BlockEdgeLow = 72, BlockEdgeHigh = 87, BlockHigh = 145;

        // Points: crossroads, square. Centre and spawn in millimetres; radius 6 m; both neutral at the start.
        public const int PointRadiusMm = 6000;
        public static readonly IReadOnlyList<(long XMm, long ZMm)> Points = Array.AsReadOnly(new[] { (80000L, 80000L), (80000L, 123000L) });
        // Bases: team 1 outside the west gate, team 2 outside the east gate.
        public static readonly IReadOnlyList<(long XMm, long ZMm)> Bases = Array.AsReadOnly(new[] { (4000L, 80000L), (156000L, 80000L) });
        // The deployed squads start inside the gates; the reserves wait at the bases.
        public static readonly IReadOnlyList<(long XMm, long ZMm)> Formations = Array.AsReadOnly(new[] { (20000L, 80000L), (140000L, 80000L) });
        public const int OnField = 32, Reserve = 16;
        public static string Soldier(int team) => team == 1 ? "rwr.soldier-1" : "rwr.soldier-2";

        public List<WardShop> Shops { get; } = new List<WardShop>();
        public List<WardBox> Houses { get; } = new List<WardBox>();
        public List<WardBox> Stalls { get; } = new List<WardBox>();
        public List<WardBox> Handcarts { get; } = new List<WardBox>();
        public List<WardBox> Oxcarts { get; } = new List<WardBox>();
        public List<WardBox> Wells { get; } = new List<WardBox>();
        public List<WardBox> Trees { get; } = new List<WardBox>();

        public static WardLayout Default
        {
            get
            {
                var layout = new WardLayout();
                layout.Shops.AddRange(new[]
                {
                    // South-west block: three on the north-south street, two on the east-west street.
                    S(WardFacing.East, 72, 44), S(WardFacing.East, 72, 50), S(WardFacing.East, 72, 56), S(WardFacing.North, 72, 44), S(WardFacing.North, 72, 50),
                    // South-east.
                    S(WardFacing.West, 87, 44), S(WardFacing.West, 87, 50), S(WardFacing.West, 87, 56), S(WardFacing.North, 72, 104), S(WardFacing.North, 72, 110),
                    // North-west (the square takes the street front from z 111).
                    S(WardFacing.East, 72, 93), S(WardFacing.East, 72, 99), S(WardFacing.East, 72, 105), S(WardFacing.South, 87, 44), S(WardFacing.South, 87, 50),
                    // North-east.
                    S(WardFacing.West, 87, 93), S(WardFacing.West, 87, 99), S(WardFacing.West, 87, 105), S(WardFacing.South, 87, 104), S(WardFacing.South, 87, 110),
                });
                layout.Houses.AddRange(new[]
                {
                    B(58, 44, 7, 18), B(44, 58, 11, 7), B(44, 44, 11, 11), B(30, 44, 11, 11), B(30, 58, 11, 11),
                    B(95, 44, 7, 18), B(105, 58, 11, 7), B(105, 44, 11, 11), B(119, 44, 11, 11), B(119, 58, 11, 11),
                    B(58, 93, 7, 18), B(44, 95, 11, 7), B(44, 105, 11, 11), B(30, 105, 11, 11), B(30, 91, 11, 11),
                    B(95, 93, 7, 18), B(105, 95, 11, 7), B(105, 105, 11, 11), B(119, 105, 11, 11), B(119, 91, 11, 11),
                });
                layout.Stalls.AddRange(new[]
                {
                    // North-south street, both sides, south half.
                    B(75, 16, 1, 2), B(75, 26, 1, 2), B(75, 38, 1, 2), B(75, 50, 1, 2), B(75, 58, 1, 2),
                    B(84, 16, 1, 2), B(84, 26, 1, 2), B(84, 38, 1, 2), B(84, 50, 1, 2), B(84, 58, 1, 2),
                    // North half.
                    B(75, 88, 1, 2), B(75, 100, 1, 2), B(75, 142, 1, 2), B(84, 88, 1, 2), B(84, 100, 1, 2), B(84, 142, 1, 2),
                    // East-west street, west half.
                    B(20, 75, 2, 1), B(30, 75, 2, 1), B(48, 75, 2, 1), B(58, 75, 2, 1), B(20, 84, 2, 1), B(30, 84, 2, 1), B(48, 84, 2, 1), B(58, 84, 2, 1),
                    // East half.
                    B(138, 75, 2, 1), B(128, 75, 2, 1), B(110, 75, 2, 1), B(100, 75, 2, 1), B(138, 84, 2, 1), B(128, 84, 2, 1), B(110, 84, 2, 1), B(100, 84, 2, 1),
                    // Square.
                    B(68, 117, 2, 1), B(90, 117, 2, 1),
                });
                layout.Handcarts.AddRange(new[] { B(75, 32, 1, 2), B(84, 32, 1, 2), B(36, 75, 2, 1), B(36, 84, 2, 1), B(122, 75, 2, 1), B(122, 84, 2, 1) });
                layout.Oxcarts.AddRange(new[] { B(68, 113, 3, 2), B(89, 113, 3, 2), B(68, 131, 3, 2), B(89, 131, 3, 2) });
                layout.Wells.AddRange(new[] { B(74, 113, 1, 1), B(22, 22, 1, 1), B(131, 131, 1, 1) });
                layout.Trees.AddRange(new[]
                {
                    B(75, 22, 1, 1), B(75, 44, 1, 1), B(75, 64, 1, 1), B(84, 22, 1, 1), B(84, 44, 1, 1), B(84, 64, 1, 1),
                    B(75, 94, 1, 1), B(75, 138, 1, 1), B(84, 94, 1, 1), B(84, 138, 1, 1),
                    B(16, 75, 1, 1), B(42, 75, 1, 1), B(64, 75, 1, 1), B(16, 84, 1, 1), B(42, 84, 1, 1), B(64, 84, 1, 1),
                    B(143, 75, 1, 1), B(117, 75, 1, 1), B(95, 75, 1, 1), B(143, 84, 1, 1), B(117, 84, 1, 1), B(95, 84, 1, 1),
                    B(76, 133, 1, 1), B(83, 133, 1, 1), B(20, 40, 1, 1), B(130, 40, 1, 1), B(20, 120, 1, 1),
                });
                return layout;
            }
        }

        private static WardShop S(WardFacing facing, int front, int start) => new WardShop(facing, front, start);
        private static WardBox B(int x, int z, int w, int d) => new WardBox(x, z, w, d);
    }
}
