using System;
using System.Collections.Generic;

namespace WarCore.Presentation
{
    // SEE-1 rule 2: which units are drawn as a silhouette. A unit is when it is alive, has a team, is of the player's team,
    // is not the player, does not stand on a hidden plate, and the line from its head (y + BodySizes.TopOf) back to the camera
    // crosses a wall or plate that is Shown in the cut (the plate it stands on excepted). A Faded or Hidden one covers nobody.
    // Enemies never are: what a Shown wall covers of them is simply not seen. No player, or a dead one: nobody. The cut is
    // the one the picture uses this frame (LayerCut.Run for the same player and camera). Sizes null: every head 1 m up, as before.
    public static class Silhouettes
    {
        public static HashSet<ulong> Run(ViewPlates plates, ViewMap map, LayerCutResult cut, ViewEntity? player,
            ReadOnlySpan<ViewEntity> units, (float X, float Y, float Z) forward, BodySizes sizes = null)
        {
            if (cut == null) throw new ArgumentNullException(nameof(cut));
            var set = new HashSet<ulong>();
            if (!player.HasValue || !player.Value.Alive) return set;
            if (!(forward.Y < 0)) throw new ArgumentOutOfRangeException(nameof(forward), "The camera must look down.");
            ViewEntity me = player.Value;
            int plateCount = plates?.Count ?? 0, wallCount = map == null ? 0 : map.Walls.Length;
            double highest = me.Y + BodySizes.TopOf(sizes, me);
            foreach (ViewEntity unit in units) highest = Math.Max(highest, unit.Y + BodySizes.TopOf(sizes, unit));
            for (int k = 0; k < plateCount; k++) highest = Math.Max(highest, SeeZone.HighestTop(plates[k]));
            for (int w = 0; w < wallCount; w++) highest = Math.Max(highest, map.Walls[w].CenterY + map.Walls[w].SizeY / 2);
            foreach (ViewEntity unit in units)
            {
                if (!unit.Alive || unit.Team == 0 || unit.Team != me.Team || unit.Key == me.Key || cut.IsHidden(unit)) continue;
                double oy = unit.Y + BodySizes.TopOf(sizes, unit);
                double length = (highest + 1 - oy) / -forward.Y;
                double dx = -forward.X * length, dy = -forward.Y * length, dz = -forward.Z * length;
                if (Covered(plates, map, cut, unit, oy, dx, dy, dz, plateCount, wallCount)) set.Add(unit.Key);
            }
            return set;
        }

        private static bool Covered(ViewPlates plates, ViewMap map, LayerCutResult cut, ViewEntity unit, double oy, double dx, double dy,
            double dz, int plateCount, int wallCount)
        {
            double minX = Math.Min(unit.X, unit.X + dx), maxX = Math.Max(unit.X, unit.X + dx);
            double minZ = Math.Min(unit.Z, unit.Z + dz), maxZ = Math.Max(unit.Z, unit.Z + dz);
            for (int w = 0; w < wallCount; w++)
            {
                ViewWall wall = map.Walls[w];
                if (wall.CenterX + wall.SizeX / 2 < minX || wall.CenterX - wall.SizeX / 2 > maxX || wall.CenterZ + wall.SizeZ / 2 < minZ
                    || wall.CenterZ - wall.SizeZ / 2 > maxZ) continue;
                if (cut.Wall(w) == CutState.Shown && PlateRay.CrossesBox(wall.CenterX, wall.CenterY, wall.CenterZ, wall.SizeX, wall.SizeY,
                        wall.SizeZ, unit.X, oy, unit.Z, dx, dy, dz))
                    return true;
            }
            for (int k = 0; k < plateCount; k++)
                if (cut.Plate(k) == CutState.Shown && k + 1 != unit.Layer && PlateRay.Crosses(plates[k], unit.X, oy, unit.Z, dx, dy, dz))
                    return true;
            return false;
        }
    }
}
