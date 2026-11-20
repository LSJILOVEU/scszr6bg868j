using WarCore.Sim;

namespace WarCore.Battle
{
    // ML-9b: the layer a unit stands on (0 = the ground, 1..N = a floor plate), read by the match systems on a map with
    // floor plates. The bridge implements it from the vertical state; without plates the systems get null and keep the old rules.
    public interface IUnitLayers
    {
        int LayerOf(EntityId unit);
    }

    // ML-9b: the top of a layer's walkable cell under (x, z), for the guard slots of a point whose centre is on a plate.
    public interface ISlotFloor
    {
        bool TryFloor(int layer, SimFix x, SimFix z, out SimFix y);
    }

    // ML-11a: the walking cost (LayeredPathfinder units) from a place on a layer to a control point or to a team's base, for
    // the AI on a map with floor plates. The bridge implements it from one cost table per target; without plates the AI gets null.
    public interface IPointReach
    {
        // False when the point cannot be walked to from there (or the place is not a walkable cell of that layer). A point is
        // walked to at its centre's cell, or, when nobody can walk on that cell, at the nearest walkable cell in its circle.
        bool TryCost(int point, SimVec3 from, int fromLayer, out long cost);
        // The same to the team's first spawn anchor; false when the team has none or nobody can walk on its cell.
        bool TryBaseCost(int team, SimVec3 from, int fromLayer, out long cost);
    }
}
