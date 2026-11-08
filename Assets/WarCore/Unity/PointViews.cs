using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // One flat disc per control point, coloured by owner, with a ring that fills as the capturing side makes progress.
    public sealed class PointViews
    {
        private const int RingSegments = 64;
        private static readonly Color Neutral = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color[] Teams = { Neutral, new Color(1f, 0.85f, 0.2f), new Color(0.2f, 0.5f, 1f) };
        private readonly Transform parent;
        private readonly ViewGround ground;
        private GameObject[] discs = System.Array.Empty<GameObject>();
        private LineRenderer[] rings = System.Array.Empty<LineRenderer>();

        // ground: the map's height field, or null on a flat map (then every height is the old constant).
        public PointViews(Transform parent, ViewGround ground = null) { this.parent = parent; this.ground = ground; }

        private float Base(float x, float z) => ground == null ? 0f : ground.HeightAt(x, z, 0f);

        public void Apply(FrameView frame)
        {
            var points = frame.Points;
            if (discs.Length != points.Length)
            {
                discs = new GameObject[points.Length];
                rings = new LineRenderer[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    discs[i] = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    discs[i].name = "Point " + points[i].Id;
                    discs[i].transform.SetParent(parent, false);
                    discs[i].GetComponent<Renderer>().material = new Material(Shader.Find("Unlit/Color"));
                    rings[i] = new GameObject("Ring " + points[i].Id).AddComponent<LineRenderer>();
                    rings[i].transform.SetParent(parent, false);
                    rings[i].useWorldSpace = true;
                    rings[i].widthMultiplier = 0.35f;
                    rings[i].material = new Material(Shader.Find("Unlit/Color"));
                }
            }
            for (int i = 0; i < points.Length; i++)
            {
                ViewPoint p = points[i];
                discs[i].transform.position = new Vector3(p.X, Base(p.X, p.Z) + 0.02f, p.Z);
                discs[i].transform.localScale = new Vector3(p.Radius * 2, 0.01f, p.Radius * 2);
                Color owner = Teams[p.Owner % Teams.Length];
                discs[i].GetComponent<Renderer>().material.color = Color.Lerp(owner, new Color(0.2f, 0.3f, 0.2f), 0.55f);
                int count = Mathf.Max(0, Mathf.RoundToInt(p.Progress * RingSegments));
                rings[i].enabled = count > 0;
                rings[i].positionCount = count + 1;
                rings[i].material.color = Teams[p.Capturing % Teams.Length];
                for (int k = 0; k <= count; k++)
                {
                    float angle = k * 2 * Mathf.PI / RingSegments;
                    float rx = p.X + Mathf.Cos(angle) * p.Radius, rz = p.Z + Mathf.Sin(angle) * p.Radius;
                    rings[i].SetPosition(k, new Vector3(rx, Base(rx, rz) + 0.05f, rz));
                }
            }
        }
    }
}
