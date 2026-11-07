using System;
using System.Globalization;

namespace WarCore.Content.MapTools
{
    // One finding of the map gate: an English code plus the optional payload the code carries. Cell is relative to the map
    // origin (TryCell key % Width, key / Width); millimetres are the description's "at"; Line is a description line.
    public sealed class MapProblem : IEquatable<MapProblem>
    {
        public string Code { get; }
        public bool HasCell { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public bool HasMm { get; }
        public long XMm { get; }
        public long ZMm { get; }
        // Null when the code carries no entry id.
        public string Id { get; }
        public bool HasLine { get; }
        public int Line { get; }

        public MapProblem(string code, string id = null, (int X, int Z)? cell = null, MapMm? mm = null, int? line = null)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Id = id;
            HasCell = cell.HasValue;
            if (cell.HasValue) { CellX = cell.Value.X; CellZ = cell.Value.Z; }
            HasMm = mm.HasValue;
            if (mm.HasValue) { XMm = mm.Value.X; ZMm = mm.Value.Z; }
            HasLine = line.HasValue;
            if (line.HasValue) Line = line.Value;
        }

        public bool Equals(MapProblem other) => other != null && Code == other.Code && Id == other.Id
            && HasCell == other.HasCell && CellX == other.CellX && CellZ == other.CellZ
            && HasMm == other.HasMm && XMm == other.XMm && ZMm == other.ZMm && HasLine == other.HasLine && Line == other.Line;

        public override bool Equals(object obj) => obj is MapProblem other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Code) ^ CellX * 397 ^ CellZ * 7919;

        public override string ToString()
        {
            var c = CultureInfo.InvariantCulture;
            return Code + (Id == null ? "" : " [" + Id + "]")
                + (HasCell ? " cell (" + CellX.ToString(c) + ", " + CellZ.ToString(c) + ")" : "")
                + (HasMm ? " mm (" + XMm.ToString(c) + ", " + ZMm.ToString(c) + ")" : "")
                + (HasLine ? " line " + Line.ToString(c) : "");
        }
    }
}
