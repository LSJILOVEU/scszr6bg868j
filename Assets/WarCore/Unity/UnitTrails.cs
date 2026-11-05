using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // One line per unit, built from the position of every simulated tick (not from interpolated frames).
    public sealed class UnitTrails
    {
        private readonly Transform parent;
        private readonly Color[] palette;
        private readonly float height;
        private readonly Dictionary<ulong, List<Vector3>> points = new Dictionary<ulong, List<Vector3>>();
        private readonly Dictionary<ulong, LineRenderer> lines = new Dictionary<ulong, LineRenderer>();

        public UnitTrails(Transform parent, Color[] palette, float height)
        {
            this.parent = parent;
            this.palette = palette;
            this.height = height;
        }

        public void Add(FrameView frame)
        {
            foreach (ViewEntity entity in frame.Entities)
            {
                if (!lines.TryGetValue(entity.Key, out LineRenderer line))
                {
                    line = new GameObject($"Trail {entity.Key}").AddComponent<LineRenderer>();
                    line.transform.SetParent(parent, false);
                    line.useWorldSpace = true;
                    line.widthMultiplier = 0.3f;
                    line.numCornerVertices = 2;
                    line.material = new Material(Shader.Find("Unlit/Color")) { color = palette[entity.Squad % palette.Length] };
                    lines.Add(entity.Key, line);
                    points.Add(entity.Key, new List<Vector3>());
                }
                List<Vector3> list = points[entity.Key];
                var point = new Vector3(entity.X, height, entity.Z);
                if (list.Count > 0 && list[list.Count - 1] == point)
                    continue;
                list.Add(point);
                line.positionCount = list.Count;
                line.SetPositions(list.ToArray());
            }
        }
    }
}
