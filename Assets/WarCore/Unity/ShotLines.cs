using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // One short line per shot, kept for a few ticks so a still picture shows who is shooting whom.
    public sealed class ShotLines
    {
        private const int LifeTicks = 6;
        private readonly Transform parent;
        private readonly Color[] palette;
        private readonly float height;
        private readonly ViewGround ground;
        private readonly BodySizes sizes;
        private readonly float floorY;
        private readonly List<(LineRenderer Line, long Tick)> live = new List<(LineRenderer, long)>();

        // sizes (SCALE-2a, BodySizes.Of the session): when given, a line runs from the shot's start to its end as the simulation
        // has them (ViewFire.Y0, Y1), its end never below the ground there (the height field, else floorY); height is then not
        // used. Null: every line at height over the ground, as before.
        public ShotLines(Transform parent, Color[] palette, float height, ViewGround ground = null, BodySizes sizes = null, float floorY = 0f)
        {
            this.sizes = sizes;
            this.floorY = floorY;
            this.ground = ground;
            this.parent = parent;
            this.palette = palette;
            this.height = height;
        }

        // Height above the ground of the map's height field; the old constant on a flat map (ground null).
        private float Base(float x, float z) => ground == null ? 0f : ground.HeightAt(x, z, 0f);

        public Vector3 Start(ViewFire fire) =>
            sizes == null ? new Vector3(fire.X0, Base(fire.X0, fire.Z0) + height, fire.Z0) : new Vector3(fire.X0, fire.Y0, fire.Z0);

        public Vector3 End(ViewFire fire) => sizes == null ? new Vector3(fire.X1, Base(fire.X1, fire.Z1) + height, fire.Z1)
            : new Vector3(fire.X1, DrawnParts.ShotEndY(fire.Y1, ground, floorY, fire.X1, fire.Z1), fire.Z1);

        public void Add(FrameView frame)
        {
            for (int i = live.Count - 1; i >= 0; i--)
                if (frame.Tick - live[i].Tick >= LifeTicks)
                {
                    Object.Destroy(live[i].Line.gameObject);
                    live.RemoveAt(i);
                }
            foreach (var fire in frame.Fires)
            {
                var line = new GameObject("Shot").AddComponent<LineRenderer>();
                line.transform.SetParent(parent, false);
                line.useWorldSpace = true;
                line.widthMultiplier = fire.Lethal ? 0.22f : 0.08f; // a lethal hit is drawn thick
                line.positionCount = 2;
                line.SetPosition(0, Start(fire));
                line.SetPosition(1, End(fire));
                line.material = new Material(Shader.Find("Unlit/Color")) { color = palette[fire.Team % palette.Length] };
                live.Add((line, frame.Tick));
            }
        }
    }
}
