using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // The visible half of a map: a ground slab (or the height-field mesh) and one box per wall, all from the ViewMap of one .wcmap.
    public static class MapView
    {
        public static GameObject Build(ViewMap map, Material ground, Material wall)
        {
            var root = new GameObject("Map " + map.MapId);
            root.AddComponent<MapBake>().fileHash = map.FileHash;
            float width = map.Width, depth = map.Depth;
            if (map.HasHeightField)
                TerrainGround(root.transform, TerrainMesh.Build(map), map.MapId, ground);
            else
                Box("Ground", root.transform, new Vector3(map.MinX + width / 2, map.FloorY - 0.05f, map.MinZ + depth / 2),
                    new Vector3(width, 0.1f, depth), ground);
            foreach (ViewWall w in map.Walls)
                Box($"Wall {w.Chunk}:{w.Placement}", root.transform, new Vector3(w.CenterX, w.CenterY, w.CenterZ),
                    new Vector3(w.SizeX, w.SizeY, w.SizeZ), wall);
            return root;
        }

        // Empty string = visible walls, Sim collision and navigation bitmap agree with the map; else the first problems.
        public static string Verify(ViewMap map, SimSession session, Transform root)
        {
            var problems = new StringBuilder();
            var mark = root.GetComponent<MapBake>();
            if (mark == null || mark.fileHash != map.FileHash)
                problems.AppendLine("MapBake hash differs from the map file hash.");
            foreach (ViewWall w in map.Walls)
            {
                Transform t = root.Find($"Wall {w.Chunk}:{w.Placement}");
                if (t == null) { problems.AppendLine($"missing wall {w.Chunk}:{w.Placement}"); continue; }
                if (Vector3.Distance(t.position, new Vector3(w.CenterX, w.CenterY, w.CenterZ)) > 1e-3f
                    || Vector3.Distance(t.lossyScale, new Vector3(w.SizeX, w.SizeY, w.SizeZ)) > 1e-3f
                    || Quaternion.Angle(t.rotation, Quaternion.identity) > 1e-3f)
                    problems.AppendLine($"wall {w.Chunk}:{w.Placement} centre, size or rotation differs from the file");
            }
            Transform g = root.Find("Ground");
            if (g == null || Mathf.Abs(g.GetComponent<Renderer>().bounds.max.y - map.FloorY) > 1e-3f
                || Mathf.Abs(g.GetComponent<Renderer>().bounds.min.x - map.MinX) > 1e-3f
                || Mathf.Abs(g.GetComponent<Renderer>().bounds.size.x - map.Width) > 1e-3f
                || Mathf.Abs(g.GetComponent<Renderer>().bounds.size.z - map.Depth) > 1e-3f)
                problems.AppendLine("ground slab does not match the map extent or floor height");
            int bad = 0;
            for (int z = 0; z < map.Depth; z++)
                for (int x = 0; x < map.Width; x++)
                {
                    var centre = new Vector3(map.MinX + x + 0.5f, map.FloorY + 0.5f, map.MinZ + z + 0.5f);
                    bool visible = false;
                    foreach (Transform child in root)
                        if (child.name.StartsWith("Wall ") && child.GetComponent<Renderer>().bounds.Contains(centre))
                            visible = true;
                    if (visible != session.BlockedCell(x, z) || visible == map.IsPassable(x, z))
                        bad++;
                }
            if (bad > 0)
                problems.AppendLine($"{bad} cells disagree between visible, collision and navigation");
            return problems.ToString();
        }

        // Empty string = the "Ground" child holds exactly the terrain mesh of the map's height field (vertices bit-exact,
        // indices equal, identity transform); else what differs. A map without a height field reports that, it does not throw.
        public static string VerifyGround(ViewMap map, Transform root)
        {
            if (!map.HasHeightField) return "no height field";
            Transform g = root.Find("Ground");
            if (g == null) return "missing Ground";
            MeshFilter filter = g.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return "Ground has no mesh";
            if (g.localPosition != Vector3.zero || g.localRotation != Quaternion.identity || g.localScale != Vector3.one)
                return "Ground transform is not identity";
            TerrainMesh expected = TerrainMesh.Build(map);
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            if (vertices.Length != expected.VertexCount)
                return $"vertex count {vertices.Length}, expected {expected.VertexCount}";
            if (triangles.Length != expected.Triangles.Count)
                return $"index count {triangles.Length}, expected {expected.Triangles.Count}";
            var problems = new StringBuilder();
            int bad = 0;
            IReadOnlyList<float> p = expected.Positions;
            for (int v = 0; v < vertices.Length; v++)
                if (Bits(vertices[v].x) != Bits(p[3 * v]) || Bits(vertices[v].y) != Bits(p[3 * v + 1])
                    || Bits(vertices[v].z) != Bits(p[3 * v + 2]))
                    if (bad++ < 3) problems.AppendLine($"vertex {v} differs");
            for (int i = 0; i < triangles.Length; i++)
                if (triangles[i] != expected.Triangles[i])
                    if (bad++ < 3) problems.AppendLine($"index {i} differs");
            if (bad > 0) problems.AppendLine($"{bad} ground values differ from the height field");
            return problems.ToString();
        }

        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        private static void TerrainGround(Transform parent, TerrainMesh terrain, string mapId, Material material)
        {
            var ground = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer));
            ground.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "Terrain " + mapId };
            if (terrain.VertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
            var vertices = new Vector3[terrain.VertexCount];
            IReadOnlyList<float> p = terrain.Positions;
            for (int v = 0; v < vertices.Length; v++) vertices[v] = new Vector3(p[3 * v], p[3 * v + 1], p[3 * v + 2]);
            var triangles = new int[terrain.Triangles.Count];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = terrain.Triangles[i];
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ground.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (material != null)
                ground.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void Box(string name, Transform parent, Vector3 centre, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.position = centre;
            box.transform.localScale = size;
            if (material != null)
                box.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
