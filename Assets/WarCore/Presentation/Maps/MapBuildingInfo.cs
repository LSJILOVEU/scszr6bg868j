using System;

namespace WarCore.Presentation
{
    // ML-12a: one building's parameters in plain values (MapDocument.WithBuilding and Buildings). Cells are whole cells,
    // heights raw (65536 = 1 m). Cell and size include the outer walls; DoorSide is "n", "e", "s" or "w" and DoorAt counts
    // from the start of that side's wall segment (south and north from the west corner, west and east from the cell after
    // the south wall); the stair is one cell wide and runs StairLength cells along x (StairAlongX) or z from (StairX, StairZ).
    // Instance is the stamp name ("building-<n>"); it is empty in a building passed to WithBuilding.
    public sealed class MapBuildingInfo
    {
        public string Instance { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public int SizeX { get; }
        public int SizeZ { get; }
        public int Floors { get; }
        public long StoreyRaw { get; }
        public long ThickRaw { get; }
        public string DoorSide { get; }
        public int DoorAt { get; }
        public int DoorWidth { get; }
        public bool StairAlongX { get; }
        public int StairX { get; }
        public int StairZ { get; }
        public int StairLength { get; }

        public MapBuildingInfo(int cellX, int cellZ, int sizeX, int sizeZ, int floors, long storeyRaw, long thickRaw, string doorSide,
            int doorAt, int doorWidth, bool stairAlongX, int stairX, int stairZ, int stairLength)
            : this("", cellX, cellZ, sizeX, sizeZ, floors, storeyRaw, thickRaw, doorSide, doorAt, doorWidth, stairAlongX, stairX, stairZ, stairLength)
        {
        }

        internal MapBuildingInfo(string instance, int cellX, int cellZ, int sizeX, int sizeZ, int floors, long storeyRaw, long thickRaw,
            string doorSide, int doorAt, int doorWidth, bool stairAlongX, int stairX, int stairZ, int stairLength)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            CellX = cellX; CellZ = cellZ; SizeX = sizeX; SizeZ = sizeZ; Floors = floors; StoreyRaw = storeyRaw; ThickRaw = thickRaw;
            DoorSide = doorSide ?? throw new ArgumentNullException(nameof(doorSide));
            DoorAt = doorAt; DoorWidth = doorWidth; StairAlongX = stairAlongX; StairX = stairX; StairZ = stairZ; StairLength = stairLength;
        }
    }

    // What a building edit gives back: the new document, the new .build.json text (canonical) and the building's stamp name.
    public sealed class MapBuildResult
    {
        public MapDocument Document { get; }
        public string BuildJson { get; }
        public string Instance { get; }

        internal MapBuildResult(MapDocument document, string buildJson, string instance)
        {
            Document = document; BuildJson = buildJson; Instance = instance;
        }
    }
}
