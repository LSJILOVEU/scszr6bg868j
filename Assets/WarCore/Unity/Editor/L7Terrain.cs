using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    // L7: show the demo height field as a lit mesh in the open scene, and save one labelled PNG of it.
    // Neither menu saves the scene; the only asset written is the new L7Terrain material.
    public static class L7Terrain
    {
        private const string MaterialDir = "Assets/WarCore/Unity/Materials";
        private const string MaterialPath = MaterialDir + "/L7Terrain.mat";
        // Built-in render pipeline (no SRP asset in GraphicsSettings), so the lit shader is Standard.
        private const string LitShader = "Standard";
        private const int ShotWidth = 1600, ShotHeight = 900, LabelScale = 4;
        // Override with WARCORE_L7_SHOT_DIR; default is outside the project, in the system temp folder.
        private static string ShotDir => Environment.GetEnvironmentVariable("WARCORE_L7_SHOT_DIR") is string dir && dir != ""
            ? dir : Path.Combine(Path.GetTempPath(), "WarCore-L7");

        [MenuItem("WarCore/L7/Show Terrain Demo")]
        public static void ShowTerrainDemo() => Show();

        [MenuItem("WarCore/L7/Capture Terrain PNG")]
        public static void CaptureTerrainPng()
        {
            byte[] bytes = Show();
            Camera camera = Camera.main;
            var target = new RenderTexture(ShotWidth, ShotHeight, 24);
            var image = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                TerrainBake.Frame(camera, GameObject.Find(RootName).transform, (float)ShotWidth / ShotHeight);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                Stamp(image, TerrainBake.Label(bytes));
                image.Apply();
                Directory.CreateDirectory(ShotDir);
                string path = Path.Combine(ShotDir, "L7-terrain-" + TerrainBake.Label(bytes).Substring(4) + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("L7 terrain PNG written to " + path);
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private const string RootName = "Map terrain-demo";

        private static byte[] Show()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before showing the terrain demo.");
            byte[] bytes = TerrainBake.DemoMapBytes();
            for (GameObject old = GameObject.Find(RootName); old != null; old = GameObject.Find(RootName))
            {
                // The terrain mesh is created by MapView, not an asset: release it with the old view.
                Transform ground = old.transform.Find("Ground");
                MeshFilter filter = ground == null ? null : ground.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && !AssetDatabase.Contains(filter.sharedMesh))
                    UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                UnityEngine.Object.DestroyImmediate(old);
            }
            Material material = EnsureMaterial();
            GameObject root = TerrainBake.Build(bytes, material, material);
            string problems = TerrainBake.VerifyGround(bytes, root.transform);
            if (problems != "")
                throw new InvalidOperationException("Terrain view differs from the map: " + problems);
            EnsureLight();
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
            }
            TerrainBake.Frame(camera, root.transform, camera.aspect);
            Debug.Log("L7 terrain demo shown: " + TerrainBake.Label(bytes));
            return bytes;
        }

        private static Material EnsureMaterial()
        {
            Directory.CreateDirectory(MaterialDir);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find(LitShader));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = Shader.Find(LitShader);
            material.color = new Color(0.62f, 0.66f, 0.48f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void EnsureLight()
        {
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) return;
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // 3 x 5 pixel glyphs for the label "map <hex>": the character, then 15 bits, rows top to bottom.
        private static readonly string[] Glyphs =
        {
            " 000000000000000", "a000011101101011", "m000111111101101", "p000110101110100",
            "0111101101101111", "1010110010010111", "2111001111100111", "3111001111001111", "4101101111001001",
            "5111100111001111", "6111100111101111", "7111001001001001", "8111101111101111", "9111101111001111",
            "A010101111101101", "B110101110101110", "C111100100100111", "D110101101101110", "E111100111100111",
            "F111100111100100",
        };

        // White text on a black box in the top-left corner (ReadPixels rows start at the bottom).
        private static void Stamp(Texture2D image, string text)
        {
            int cell = 4 * LabelScale, top = image.height - 1;
            for (int y = 0; y < 7 * LabelScale; y++)
                for (int x = 0; x < text.Length * cell + 2 * LabelScale; x++) image.SetPixel(x, top - y, Color.black);
            for (int c = 0; c < text.Length; c++)
            {
                string glyph = Array.Find(Glyphs, g => g[0] == text[c]) ?? Glyphs[0];
                for (int bit = 0; bit < 15; bit++)
                {
                    if (glyph[1 + bit] != '1') continue;
                    int x0 = LabelScale + c * cell + (bit % 3) * LabelScale, y0 = LabelScale + (bit / 3) * LabelScale;
                    for (int sy = 0; sy < LabelScale; sy++)
                        for (int sx = 0; sx < LabelScale; sx++) image.SetPixel(x0 + sx, top - y0 - sy, Color.white);
                }
            }
        }
    }
}
