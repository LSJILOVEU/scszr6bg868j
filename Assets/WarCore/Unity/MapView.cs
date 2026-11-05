using System.Text;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // The visible half of a map: a ground slab and one box per wall, all from the ViewMap of one .wcmap.
    public static class MapView
    {
        public static GameObject Build(ViewMap map, Material ground, Material wall)
        {
            var root = new GameObject("Map " + map.MapId);
            root.AddComponent<MapBake>().fileHash = map.FileHash;
            float width = map.Width, depth = map.Depth;
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
