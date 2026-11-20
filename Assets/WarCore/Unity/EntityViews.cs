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
        // SEE-1: one flat see-through copy per covered team mate, drawn over everything ("Silhouette <key>").
        public const string SilhouettePrefix = "Silhouette ";
        private readonly Dictionary<ulong, GameObject> silhouettes = new Dictionary<ulong, GameObject>();

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

        public void Apply(FrameView previous, FrameView current, float alpha) => Apply(previous, current, alpha, null, null);

        public void Apply(FrameView previous, FrameView current, float alpha, LayerCutResult cut) => Apply(previous, current, alpha, cut, null);

        // ML-10: a unit the layer cut hides (on a hidden plate) is kept but not shown. SEE-1: a unit in covered (Silhouettes.Run) also
        // gets its silhouette drawn, a flat see-through copy of the body that no wall hides; null means none.
        public void Apply(FrameView previous, FrameView current, float alpha, LayerCutResult cut, ICollection<ulong> covered)
        {
            removed.Clear();
            foreach (var pair in cubes)
                if (!current.TryGet(pair.Key, out _))
                    removed.Add(pair.Key);
            foreach (ulong key in removed)
            {
                Object.Destroy(cubes[key]);
                cubes.Remove(key);
                if (silhouettes.TryGetValue(key, out GameObject gone))
                {
                    Object.Destroy(gone);
                    silhouettes.Remove(key);
                }
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
                bool shown = cut == null || !cut.IsHidden(entity);
                if (cube.activeSelf != shown) cube.SetActive(shown);
                Silhouette(entity, shown && covered != null && covered.Contains(entity.Key));
            }
        }

        // The silhouette of a unit is made the first time the unit is covered and switched on and off after that.
        private void Silhouette(ViewEntity entity, bool on)
        {
            if (!silhouettes.TryGetValue(entity.Key, out GameObject body))
            {
                if (!on) return;
                body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = SilhouettePrefix + entity.Key;
                body.GetComponent<Collider>().enabled = false;
                body.transform.SetParent(parent, false);
                body.transform.localScale = scale;
                body.GetComponent<Renderer>().sharedMaterial = SilhouetteMaterial(entity);
                silhouettes.Add(entity.Key, body);
            }
            body.transform.position = new Vector3(entity.X, entity.Y + lift, entity.Z);
            if (body.activeSelf != on) body.SetActive(on);
        }

        // One flat colour that ignores depth: the team colour, darker, at 60 % (white without a palette).
        private Material SilhouetteMaterial(ViewEntity entity)
        {
            Color team = palette != null ? palette[Colour(entity) % palette.Length] : Color.white;
            Shader shader = Shader.Find(SilhouetteShader) ?? Shader.Find("Unlit/Color");
            return new Material(shader) { color = new Color(team.r * 0.7f, team.g * 0.7f, team.b * 0.7f, 0.6f), name = "Silhouette " + Colour(entity) };
        }

        public const string SilhouetteShader = "WarCore/Silhouette";
    }
}
