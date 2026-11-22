using WarCore.Sim;

namespace WarCore.Presentation
{
    // TRV-7a: each point's entries and watch cells on a map with floor plates, built once from the walkable graph, the points'
    // cost tables and combat's static sight (walls, ground and plates; units never block it), with the pack's eye height. Only
    // data: no match system reads it, and it is neither saved nor hashed. Null without plates or without a match.
    internal static class EntryWiring
    {
        // Placeholders (K19): 15 m on the default surface (cost 1, 1000 a metre) and 3 watch cells an entry.
        internal const long ReachCost = 15000;
        internal const int Keep = 3;

        // Call once the walls are in the spatial index (after Sim.CompleteSpatialBoundary, or after a restore).
        internal static PointEntryTable Of(WorldAssembly a)
        {
            if (a.Reach == null || a.Plates == null || a.Combat == null) return null;
            LayeredNav nav = a.Plates.Nav;
            return PointEntryTable.Build(nav, EntryTable.Build(nav), a.Reach.Tables, ReachCost, Keep, a.Combat.Sees);
        }
    }
}
