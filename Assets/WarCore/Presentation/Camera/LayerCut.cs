using System;
using System.Collections.Generic;
using WarCore.Content;

namespace WarCore.Presentation
{
    public enum CutState : byte { Shown = 0, Hidden = 1, Faded = 2 }

    // ML-10: what the picture hides or fades around the player's unit. Plate(k) is the plate of layer k + 1, Wall(k) the wall
    // ViewMap.Walls[k]. Only the picture and picking read it; Sim and its input never do.
    public sealed class LayerCutResult
    {
        private readonly CutState[] plates, walls;

        internal LayerCutResult(CutState[] plates, CutState[] walls, bool indoors)
        {
            this.plates = plates; this.walls = walls; Indoors = indoors;
        }

        // True when a plate stands over the player's unit (its rectangle, cutouts included, with its bottom above the feet).
        public bool Indoors { get; }
        public int PlateCount => plates.Length;
        public int WallCount => walls.Length;
        public CutState Plate(int index) => plates[index];
        public CutState Wall(int index) => walls[index];

        // A unit on a hidden plate is neither drawn nor snapped to; a unit on the ground never hides.
        public bool IsHidden(ViewEntity unit) => unit.Layer > 0 && unit.Layer <= plates.Length && plates[unit.Layer - 1] == CutState.Hidden;

        // A pick lands only on a plate that is drawn as it is (neither hidden nor faded).
        public bool Pickable(int layer) => layer < 1 || layer > plates.Length || plates[layer - 1] == CutState.Shown;
    }

    // ML-10 (multi-level maps 2.6): the layer cut follows the player's unit.
    //   Indoors: some plate's rectangle covers the unit's (x, z) with its bottom there above the unit's feet y. The building is
    //   the plates whose rectangles overlap or share an edge with such a plate, transitively. Hidden: its plates whose lowest
    //   bottom is above y + Headroom (the walk clearance), and the walls whose bottom is at or above y + Headroom with a
    //   footprint touching one of its rectangles. Outdoors nothing is hidden.
    //   Fading: a plate (other than the one the unit stands on) or wall that is not hidden and stands on the line from the
    //   camera to the unit's feet (y + FootLift) or head (y + BodySizes.TopOf: its stance's height, 1 m without sizes) is faded,
    //   indoors and outdoors.
    //   The see-through ring's threshold (SeeZone) is the standing eye height: SeeHeight.
    public static class LayerCut
    {
        // The walk clearance, defined once in WallSpan (ClearanceRaw): plates and walls this far over the feet are another storey.
        public static readonly float Headroom = WallSpan.ClearanceMetres;
        public const float FootLift = 0.1f;  // PLACEHOLDER: the low end of the unit seen through walls
        public const float FadeAlpha = 0.3f; // PLACEHOLDER: tune after play
        public const float SeeRadius = 2f;   // SEE-1 PLACEHOLDER: the radius the player scenes pass (Run's own default is 0 = off); 5 m faded walls 5 to 7 m early
        // The see-through threshold of a pack without a body (the old eye height). With a body the threshold is its eye.
        public const float LegacySeeHeight = 1.5f;

        // See-through threshold = standing eye height: a wall or plate must top the feet by more than this to fade by the ring.
        // It is the standing eye (level 0) whatever the unit's stance now; without sizes it is LegacySeeHeight.
        internal static float SeeHeight(BodySizes sizes) => sizes == null ? LegacySeeHeight : sizes.StandingEye;

        // Nothing hidden or faded (no player, or a map without plates or walls).
        public static LayerCutResult None(ViewPlates plates, ViewMap map) =>
            new LayerCutResult(new CutState[plates?.Count ?? 0], new CutState[map == null ? 0 : map.Walls.Length], false);

        // forward is the camera's view direction (IsoCamera.Forward; Y below 0).
        // seeRadius (SEE-1) above 0 also fades what stands over the ground the unit can see within that radius (SeeZone).
        // sizes (SCALE-2a): the head line and the ring's eye follow the body; null keeps the old 1 m.
        public static LayerCutResult Run(ViewPlates plates, ViewMap map, ViewEntity? player, (float X, float Y, float Z) forward,
            float seeRadius = 0f, BodySizes sizes = null)
        {
            LayerCutResult none = None(plates, map);
            if (!player.HasValue || !player.Value.Alive) return none;
            if (!(forward.Y < 0)) throw new ArgumentOutOfRangeException(nameof(forward), "The camera must look down.");
            ViewEntity me = player.Value;
            int plateCount = plates?.Count ?? 0, wallCount = map == null ? 0 : map.Walls.Length;
            var plateStates = new CutState[plateCount];
            var wallStates = new CutState[wallCount];

            var building = new bool[plateCount];
            bool indoors = false;
            for (int k = 0; k < plateCount; k++)
            {
                ViewPlate p = plates[k];
                if (p.TryPlaneTop(me.X, me.Z, out float top) && top - p.Thick > me.Y)
                {
                    building[k] = true;
                    indoors = true;
                }
            }
            if (indoors)
            {
                var queue = new Queue<int>();
                for (int k = 0; k < plateCount; k++) if (building[k]) queue.Enqueue(k);
                while (queue.Count > 0)
                {
                    int a = queue.Dequeue();
                    for (int b = 0; b < plateCount; b++)
                        if (!building[b] && Touch(plates[a], plates[b])) { building[b] = true; queue.Enqueue(b); }
                }
                float cut = me.Y + Headroom;
                for (int k = 0; k < plateCount; k++)
                    if (building[k] && plates[k].LowestBottom > cut) plateStates[k] = CutState.Hidden;
                for (int w = 0; w < wallCount; w++)
                {
                    ViewWall wall = map.Walls[w];
                    if (wall.CenterY - wall.SizeY / 2 < cut) continue;
                    for (int k = 0; k < plateCount; k++)
                        if (building[k] && Covers(plates[k], wall)) { wallStates[w] = CutState.Hidden; break; }
                }
            }

            // The line from each target point back against the view direction, until it is above everything.
            float head = BodySizes.TopOf(sizes, me), highest = me.Y + head;
            for (int k = 0; k < plateCount; k++)
                for (int j = 0; j <= plates[k].Depth; j++)
                    for (int i = 0; i <= plates[k].Width; i++) highest = Math.Max(highest, plates[k].Top(i, j));
            for (int w = 0; w < wallCount; w++) highest = Math.Max(highest, map.Walls[w].CenterY + map.Walls[w].SizeY / 2);
            foreach (float lift in new[] { FootLift, head })
            {
                double ox = me.X, oy = me.Y + lift, oz = me.Z;
                double length = (highest + 1 - oy) / -forward.Y;
                if (length <= 0) continue;
                double dx = -forward.X * length, dy = -forward.Y * length, dz = -forward.Z * length;
                for (int k = 0; k < plateCount; k++)
                    if (plateStates[k] == CutState.Shown && k + 1 != me.Layer && PlateRay.Crosses(plates[k], ox, oy, oz, dx, dy, dz))
                        plateStates[k] = CutState.Faded;
                for (int w = 0; w < wallCount; w++)
                {
                    ViewWall wall = map.Walls[w];
                    if (wallStates[w] == CutState.Shown && PlateRay.CrossesBox(wall.CenterX, wall.CenterY, wall.CenterZ, wall.SizeX,
                            wall.SizeY, wall.SizeZ, ox, oy, oz, dx, dy, dz))
                        wallStates[w] = CutState.Faded;
                }
            }
            if (seeRadius > 0) SeeZone.Fade(plates, map, me, forward, seeRadius, highest, plateStates, wallStates, sizes);
            return new LayerCutResult(plateStates, wallStates, indoors);
        }

        // Rectangles that overlap or share a stretch of edge (a corner alone does not count).
        private static bool Touch(ViewPlate a, ViewPlate b)
        {
            int x = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
            int z = Math.Min(a.Z + a.Depth, b.Z + b.Depth) - Math.Max(a.Z, b.Z);
            return x >= 0 && z >= 0 && (x > 0 || z > 0);
        }

        // The wall's footprint meets the plate's closed rectangle.
        private static bool Covers(ViewPlate p, ViewWall w) =>
            w.CenterX + w.SizeX / 2 >= p.MinX && w.CenterX - w.SizeX / 2 <= p.MinX + p.Width
            && w.CenterZ + w.SizeZ / 2 >= p.MinZ && w.CenterZ - w.SizeZ / 2 <= p.MinZ + p.Depth;
    }
}
