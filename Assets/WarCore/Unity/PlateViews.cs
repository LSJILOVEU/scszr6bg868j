using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // ML-10: the floor plates and stairs of a map, one mesh per plate (PlateMesh) named "Plate <layer>" under the map root,
    // and the layer cut on them and on the map's walls: a hidden plate or wall is not rendered, a faded one is drawn with
    // a see-through copy of its material.
    public sealed class PlateViews
    {
        public const string Prefix = "Plate ";

        private readonly Renderer[] plates;
        private readonly Renderer[] walls;
        private readonly Material plate, plateFade, wall, wallFade;

        public PlateViews(Transform mapRoot, ViewPlates source, ViewMap map, Material plateMaterial, Material wallMaterial)
        {
            plate = plateMaterial;
            wall = wallMaterial;
            plateFade = Fade(plateMaterial);
            wallFade = Fade(wallMaterial);
            plates = new Renderer[source?.Count ?? 0];
            for (int k = 0; k < plates.Length; k++)
            {
                PlateMesh built = PlateMesh.Build(source[k]);
                var go = new GameObject(Prefix + built.Layer);
                go.transform.SetParent(mapRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = ToMesh(built);
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = plate;
                plates[k] = renderer;
            }
            walls = new Renderer[map == null ? 0 : map.Walls.Length];
            for (int w = 0; w < walls.Length; w++)
            {
                ViewWall v = map.Walls[w];
                Transform t = mapRoot.Find($"Wall {v.Chunk}:{v.Placement}");
                walls[w] = t == null ? null : t.GetComponent<Renderer>();
            }
        }

        public void Apply(LayerCutResult cut)
        {
            for (int k = 0; k < plates.Length; k++)
                Set(plates[k], cut == null || k >= cut.PlateCount ? CutState.Shown : cut.Plate(k), plate, plateFade);
            for (int w = 0; w < walls.Length; w++)
                Set(walls[w], cut == null || w >= cut.WallCount ? CutState.Shown : cut.Wall(w), wall, wallFade);
        }

        // Empty string = every plate has its "Plate <layer>" child under the root holding exactly PlateMesh's vertices; else one
        // line per problem. A map without plates has nothing to check.
        public static string Verify(ViewPlates source, Transform mapRoot)
        {
            var lines = new StringBuilder();
            for (int k = 0; k < (source?.Count ?? 0); k++)
            {
                PlateMesh expected = PlateMesh.Build(source[k]);
                Transform t = mapRoot.Find(Prefix + expected.Layer);
                MeshFilter filter = t == null ? null : t.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    lines.Append("plate ").Append(expected.Layer).Append(": missing\n");
                    continue;
                }
                if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one)
                    lines.Append("plate ").Append(expected.Layer).Append(": transform is not identity\n");
                Vector3[] vertices = filter.sharedMesh.vertices;
                var drawn = new List<float>(vertices.Length * 3);
                foreach (Vector3 v in vertices) { drawn.Add(v.x); drawn.Add(v.y); drawn.Add(v.z); }
                string problem = expected.Compare(drawn);
                if (problem != "") lines.Append(problem).Append('\n');
            }
            return lines.ToString();
        }

        private static void Set(Renderer renderer, CutState state, Material normal, Material faded)
        {
            if (renderer == null) return;
            renderer.enabled = state != CutState.Hidden;
            renderer.sharedMaterial = state == CutState.Faded ? faded : normal;
        }

        private static Mesh ToMesh(PlateMesh built)
        {
            var mesh = new Mesh { name = Prefix + built.Layer };
            if (built.VertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
            var vertices = new Vector3[built.VertexCount];
            for (int v = 0; v < vertices.Length; v++)
                vertices[v] = new Vector3(built.Positions[3 * v], built.Positions[3 * v + 1], built.Positions[3 * v + 2]);
            var triangles = new int[built.Triangles.Count];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = built.Triangles[i];
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A see-through copy of a Standard material (the Standard shader's own Fade mode, set by hand at run time); null for null.
        public static Material Fade(Material source)
        {
            if (source == null) return null;
            var m = new Material(source) { name = source.name + " (faded)" };
            m.SetFloat("_Mode", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            Color c = m.color;
            c.a = LayerCut.FadeAlpha;
            m.color = c;
            return m;
        }
    }
}
