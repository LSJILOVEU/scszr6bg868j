using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    public sealed class EntityViews
    {
        private readonly Transform parent;
        private readonly Vector3 scale;
        private readonly float lift;
        private readonly Color[] palette;
        private readonly Dictionary<ulong, GameObject> cubes = new Dictionary<ulong, GameObject>();
        private readonly List<ulong> removed = new List<ulong>();

        // Defaults keep the M1 look; a map demo passes a unit-sized scale, a lift to the feet, and a palette by squad.
        public EntityViews(Transform parent, Vector3? scale = null, float lift = 0, Color[] palette = null)
        {
            this.parent = parent;
            this.scale = scale ?? Vector3.one;
            this.lift = lift;
            this.palette = palette;
        }

        // Combat units colour by team; older demos have no team and keep colouring by squad.
        private static int Colour(ViewEntity entity) => entity.Team != 0 ? entity.Team : entity.Squad;

        public void Apply(FrameView previous, FrameView current, float alpha)
        {
            removed.Clear();
            foreach (var pair in cubes)
                if (!current.TryGet(pair.Key, out _))
                    removed.Add(pair.Key);
            foreach (ulong key in removed)
            {
                Object.Destroy(cubes[key]);
                cubes.Remove(key);
            }
            foreach (ViewEntity entity in FrameView.Interpolate(previous, current, alpha).Entities)
            {
                if (!cubes.TryGetValue(entity.Key, out GameObject cube))
                {
                    cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Unit {entity.Key}";
                    cube.transform.SetParent(parent, false);
                    cube.transform.localScale = scale;
                    if (palette != null)
                        cube.GetComponent<Renderer>().material = new Material(Shader.Find("Unlit/Color"))
                            { color = palette[Colour(entity) % palette.Length] };
                    cubes.Add(entity.Key, cube);
                }
                if (!entity.Alive && palette != null)
                {
                    // A corpse stays where it fell: grey and flat, so the fight reads from above.
                    cube.GetComponent<Renderer>().material.color = new Color(0.35f, 0.35f, 0.35f);
                    cube.transform.localScale = new Vector3(scale.x, scale.y * 0.2f, scale.z);
                }
                cube.transform.position = new Vector3(entity.X, entity.Y + lift, entity.Z);
            }
        }
    }
}
