using System;

namespace WarCore.Presentation
{
    // SEE-1 rule 1: the ground round the player's unit. 16 sample points: 8 directions (0 degrees is +x, 90 degrees +z), each at
    // half the radius and at the whole radius, at the unit's feet height. A sample counts when the unit can see it: the level
    // line from its eye (y + BodySizes.EyeOf: the body's eye, or 1 m without sizes) to the sample crosses no wall. From each counted sample the line back to the camera
    // (the same line as the cut's feet line, from y + FootLift) fades the walls and plates it crosses that are still Shown,
    // other than the plate under the unit, and only those whose top is more than MinSeeHeight over the feet, so sandbags and
    // low walls stay. Hidden stays hidden. Slopes are not followed: the samples take the unit's own height.
    internal static class SeeZone
    {
        private const int Directions = 8;

        internal static void Fade(ViewPlates plates, ViewMap map, ViewEntity me, (float X, float Y, float Z) forward, float radius,
            double highest, CutState[] plateStates, CutState[] wallStates, BodySizes sizes = null)
        {
            int plateCount = plateStates.Length, wallCount = wallStates.Length;
            var plateTops = new double[plateCount];
            for (int k = 0; k < plateCount; k++) plateTops[k] = HighestTop(plates[k]);
            double eye = me.Y + BodySizes.EyeOf(sizes, me), start = me.Y + LayerCut.FootLift;
            double length = (highest + 1 - start) / -forward.Y;
            if (length <= 0) return;
            double dx = -forward.X * length, dy = -forward.Y * length, dz = -forward.Z * length;
            for (int direction = 0; direction < Directions; direction++)
            {
                double angle = direction * Math.PI / (Directions / 2), ux = Math.Cos(angle), uz = Math.Sin(angle);
                for (int ring = 1; ring <= 2; ring++)
                {
                    double reach = radius * ring / 2, sx = me.X + ux * reach, sz = me.Z + uz * reach;
                    if (!Seen(map, me.X, eye, me.Z, sx - me.X, sz - me.Z)) continue;
                    for (int k = 0; k < plateCount; k++)
                        if (plateStates[k] == CutState.Shown && k + 1 != me.Layer && plateTops[k] - me.Y > LayerCut.MinSeeHeight
                            && PlateRay.Crosses(plates[k], sx, start, sz, dx, dy, dz))
                            plateStates[k] = CutState.Faded;
                    for (int w = 0; w < wallCount; w++)
                    {
                        ViewWall wall = map.Walls[w];
                        if (wallStates[w] == CutState.Shown && wall.CenterY + wall.SizeY / 2 - me.Y > LayerCut.MinSeeHeight
                            && PlateRay.CrossesBox(wall.CenterX, wall.CenterY, wall.CenterZ, wall.SizeX, wall.SizeY, wall.SizeZ,
                                sx, start, sz, dx, dy, dz))
                            wallStates[w] = CutState.Faded;
                    }
                }
            }
        }

        // The level segment from (x, y, z) by (dx, dz) crosses no wall (all of them, whatever their state).
        private static bool Seen(ViewMap map, double x, double y, double z, double dx, double dz)
        {
            for (int w = 0; w < map.Walls.Length; w++)
            {
                ViewWall wall = map.Walls[w];
                if (PlateRay.CrossesBox(wall.CenterX, wall.CenterY, wall.CenterZ, wall.SizeX, wall.SizeY, wall.SizeZ, x, y, z, dx, 0, dz))
                    return false;
            }
            return true;
        }

        internal static double HighestTop(ViewPlate p)
        {
            double top = double.NegativeInfinity;
            for (int j = 0; j <= p.Depth; j++)
                for (int i = 0; i <= p.Width; i++) top = Math.Max(top, p.Top(i, j));
            return top;
        }
    }
}
