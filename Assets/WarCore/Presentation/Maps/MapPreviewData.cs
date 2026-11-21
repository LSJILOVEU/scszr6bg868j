using System.Collections.Generic;

namespace WarCore.Presentation
{
    // Everything a flat picture of a map description needs, in plain values (the output of MapPreview.Build). Cells run row by row
    // (z first, then x), W x D of them. Positions of points, bases, units and pins are world millimetres; cells and sizes of
    // walls and plates are whole cells counted from the map origin. Colours are 0xRRGGBB.
    public sealed class MapPreviewData
    {
        public string Id { get; internal set; } = "";
        public string Pack { get; internal set; } = "";
        public int Width { get; internal set; }
        public int Depth { get; internal set; }
        public int OriginX { get; internal set; }
        public int OriginZ { get; internal set; }
        // 40 (dark) to 240 (light); 160 on flat ground and on a map without heights.
        public IReadOnlyList<int> Shade { get; internal set; } = new int[0];
        // The cell is steeper than the pack's walkable slope.
        public IReadOnlyList<bool> Steep { get; internal set; } = new bool[0];
        public IReadOnlyList<int> Ground { get; internal set; } = new int[0];
        public IReadOnlyList<MapPreviewWall> Walls { get; internal set; } = new MapPreviewWall[0];
        public IReadOnlyList<MapPreviewPlate> Plates { get; internal set; } = new MapPreviewPlate[0];
        public IReadOnlyList<MapPreviewPoint> Points { get; internal set; } = new MapPreviewPoint[0];
        public IReadOnlyList<MapPreviewBase> Bases { get; internal set; } = new MapPreviewBase[0];
        public IReadOnlyList<MapPreviewUnit> Units { get; internal set; } = new MapPreviewUnit[0];
        // One per problem of the gate that has a place, at the place the gate gave.
        public IReadOnlyList<MapPreviewPin> Pins { get; internal set; } = new MapPreviewPin[0];
        // Every problem of the gate, in the gate's order.
        public IReadOnlyList<MapPreviewProblem> Problems { get; internal set; } = new MapPreviewProblem[0];
        // Things worth saying that are not problems (a surface the palette has no colour for).
        public IReadOnlyList<MapPreviewNote> Notes { get; internal set; } = new MapPreviewNote[0];
    }

    public sealed class MapPreviewWall
    {
        public ulong Id { get; internal set; }
        public int CellX { get; internal set; }
        public int CellZ { get; internal set; }
        public int SizeX { get; internal set; }
        public int SizeZ { get; internal set; }
        public long HeightRaw { get; internal set; }
        // A wall that stands on BaseRaw instead of on the floor.
        public bool Raised { get; internal set; }
        public long BaseRaw { get; internal set; }
        public int SideColor { get; internal set; }
        public int TopColor { get; internal set; }
    }

    public sealed class MapPreviewPlate
    {
        public ulong Id { get; internal set; }
        public int CellX { get; internal set; }
        public int CellZ { get; internal set; }
        public int SizeX { get; internal set; }
        public int SizeZ { get; internal set; }
        // "x" or "z" for a slope that rises along that axis, "" for a flat top (FromRaw = ToRaw = the top).
        public string Axis { get; internal set; } = "";
        public long FromRaw { get; internal set; }
        public long ToRaw { get; internal set; }
        public long ThickRaw { get; internal set; }
    }

    public sealed class MapPreviewPoint
    {
        public int Id { get; internal set; }
        public long XMm { get; internal set; }
        public long ZMm { get; internal set; }
        public long RadiusMm { get; internal set; }
        public int Owner { get; internal set; }
        public int Color { get; internal set; }
    }

    public sealed class MapPreviewBase
    {
        public int Id { get; internal set; }
        public int Team { get; internal set; }
        public long XMm { get; internal set; }
        public long ZMm { get; internal set; }
        public int Color { get; internal set; }
    }

    public sealed class MapPreviewUnit
    {
        public ulong Key { get; internal set; }
        public string Type { get; internal set; } = "";
        public int Team { get; internal set; }
        public int Squad { get; internal set; }
        public long XMm { get; internal set; }
        public long ZMm { get; internal set; }
        public bool Reserve { get; internal set; }
        public int Color { get; internal set; }
    }

    public sealed class MapPreviewPin
    {
        // The index of this pin's problem in MapPreviewData.Problems.
        public int ProblemIndex { get; internal set; }
        public string Code { get; internal set; } = "";
        // The entry the problem names (for a writer problem, the writer's code); empty when it names none.
        public string Id { get; internal set; } = "";
        public string Text { get; internal set; } = "";
        public long XMm { get; internal set; }
        public long ZMm { get; internal set; }
    }

    public sealed class MapPreviewProblem
    {
        public string Code { get; internal set; } = "";
        public string Id { get; internal set; } = "";
        public string Text { get; internal set; } = "";
    }

    public sealed class MapPreviewNote
    {
        public string Name { get; internal set; } = "";
        public string Key { get; internal set; } = "";
        public string Text { get; internal set; } = "";
    }
}
