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
}
