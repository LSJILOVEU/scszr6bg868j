using WarCore.Content;

namespace WarCore.Presentation
{
    // SCALE-2b: the standard parts of this description's content layer, and a building made from them. The conversion from
    // millimetres to raw happens on the content side (ScaleRules); these members only pass raw values on.
    public sealed partial class MapDocument
    {
        // The layer's section "scale", or the defaults when the layer has none.
        public MapScaleInfo Scale()
        {
            ScaleRules s = MapEntrySteps.ScaleOf(PackId);
            return new MapScaleInfo(s.StoreyRaw, s.PlateRaw, s.LowWallRaw, s.ChestWallRaw, s.HighWallRaw, s.WindowSillRaw, s.WindowTopRaw,
                s.RailingRaw, s.LaneRaw, s.SidewalkRaw, s.AlleyRaw, s.StairRiseMm, s.StairRunMm);
        }

        // A building for WithBuilding with the layer's storey and plate, a one-cell doorway and the stair length the layer's
        // stair step needs for this floor count (ScaleRules.StairCells: ArgumentException when no stair fits). Position, size,
        // door and stair start come from the caller and are checked by WithBuilding (the stair direction needs the stair's
        // cells + 4 inside the outer walls). WithFloors keeps a building's stair length, so a standard building that goes from
        // one floor to two, or from two to three, is made again with StandardBuilding instead.
        public MapBuildingInfo StandardBuilding(int cellX, int cellZ, int sizeX, int sizeZ, int floors, string doorSide, int doorAt,
            bool stairAlongX, int stairX, int stairZ)
        {
            ScaleRules s = MapEntrySteps.ScaleOf(PackId);
            return new MapBuildingInfo(cellX, cellZ, sizeX, sizeZ, floors, s.StoreyRaw, s.PlateRaw, doorSide, doorAt, 1, stairAlongX,
                stairX, stairZ, s.StairCells(floors));
        }
    }
}
