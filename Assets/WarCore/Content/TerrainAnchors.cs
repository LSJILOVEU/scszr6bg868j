using System;
using System.IO;
using WarCore.Sim;

namespace WarCore.Content
{
    // Reachability of map anchors under the final (rule-derived) walkable cells. Uses the runtime pathfinder, so the
    // adjacency rule (8 neighbours, diagonals need both orthogonal cells) is the one units actually follow.
    // Anchors are not part of a .wcmap; callers pass the spawn and control-point positions.
    public static class TerrainAnchors
    {
        public static void Validate(TerrainTables tables, SimVec3[] spawns, SimVec3[] controlPoints)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            foreach (SimVec3 p in spawns) RequireWalkable(tables.Nav, p, "spawn");
            foreach (SimVec3 p in controlPoints) RequireWalkable(tables.Nav, p, "control point");
            var finder = new GridPathfinder(tables.Nav);
            foreach (SimVec3 spawn in spawns)
                foreach (SimVec3 point in controlPoints)
                    if (finder.Find(spawn, point) != GridPathStatus.Found)
                        throw new InvalidDataException("TERRAIN_UNREACHABLE spawn " + At(spawn) + " cannot reach control point " + At(point));
        }

        private static string At(SimVec3 p) => "(x " + p.X.Raw + ", z " + p.Z.Raw + " raw)";

        private static void RequireWalkable(NavGrid nav, SimVec3 p, string what)
        {
            if (!nav.TryCell(p, out int key) || !nav.IsPassable(key % nav.Width, key / nav.Width))
                throw new InvalidDataException("TERRAIN_ANCHOR_BLOCKED " + what + " " + At(p) + " is not on a walkable cell");
        }
    }
}
