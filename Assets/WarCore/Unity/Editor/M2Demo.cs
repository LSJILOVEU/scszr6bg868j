using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    public static class M2Demo
    {
        private const string ScenePath = "Assets/WarCore/Unity/Scenes/M2Map.unity";
        private const string MatchScenePath = "Assets/WarCore/Unity/Scenes/M6Map.unity";
        private const string M7ScenePath = "Assets/WarCore/Unity/Scenes/M7Map.unity";
        private const string MaterialDir = "Assets/WarCore/Unity/Materials";
        private static string MapFile => Path.Combine(Application.streamingAssetsPath, M2Host.MapPath);
        private static string MatchMapFile(bool m7) => Path.Combine(Application.streamingAssetsPath, m7 ? M2Host.M7MapPath : M2Host.MatchMapPath);
        private static string DemoScenePath(bool match, bool m7) => m7 ? M7ScenePath : match ? MatchScenePath : ScenePath;

        [MenuItem("WarCore/M2/Write Demo Map")]
        public static void WriteDemoMap()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MapFile));
            File.WriteAllBytes(MapFile, M2Bake.DemoMapBytes());
            AssetDatabase.Refresh();
        }

        // Bake = turn the .wcmap into the scene's visible objects, save, reload, and verify against Sim and the bitmap.
        [MenuItem("WarCore/M2/Bake Scene")]
        public static void BakeScene() => Bake(false);

        [MenuItem("WarCore/M6/Write Demo Map")]
        public static void WriteMatchMap() => WriteMatchMap(false);

        [MenuItem("WarCore/M7/Write Demo Map")]
        public static void WriteM7Map() => WriteMatchMap(true);

        private static void WriteMatchMap(bool m7)
        {
            string path = MatchMapFile(m7);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, M2Bake.MatchMapBytes(m7));
            AssetDatabase.Refresh();
        }

        [MenuItem("WarCore/M6/Bake Scene")]
        public static void BakeMatchScene() => Bake(true);

        [MenuItem("WarCore/M7/Bake Scene")]
        public static void BakeM7Scene() => Bake(true, true);

        private static void Bake(bool match, bool m7 = false)
        {
            string scenePath = DemoScenePath(match, m7);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before baking.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            byte[] bytes = File.ReadAllBytes(match ? MatchMapFile(m7) : MapFile);
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            Directory.CreateDirectory(MaterialDir);
            Material ground = EnsureMaterial("M2Ground", new Color(0.30f, 0.42f, 0.28f));
            Material wall = EnsureMaterial("M2Wall", new Color(0.62f, 0.2f, 0.18f));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
            var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(60, -20, 0);
            new GameObject("M2Host").AddComponent<M2Host>();
            M2Bake.Build(bytes, ground, wall);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("Could not save " + scenePath);
            VerifySavedScene(bytes, scenePath, match, m7);
        }

        // Reload the saved scene from disk and compare what it holds with the file.
        public static void VerifySavedScene(byte[] bytes, bool match = false)
            => VerifySavedScene(bytes, DemoScenePath(match, false), match, false);

        public static void VerifySavedScene(byte[] bytes, string scenePath, bool match, bool m7)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Transform root = UnityEngine.Object.FindAnyObjectByType<MapBake>().transform;
            string problems = match ? M2Bake.VerifyMatch(bytes, root, m7) : M2Bake.Verify(bytes, root);
            if (problems != "")
                throw new InvalidOperationException("Baked scene differs from the map file:\n" + problems);
            Debug.Log(scenePath + " bake ok: " + M2Bake.Describe(bytes));
        }

        [MenuItem("WarCore/M2/Bake Scene (batch)")]
        public static void BakeSceneBatch() => BakeBatch(false);

        [MenuItem("WarCore/M6/Bake Scene (batch)")]
        public static void BakeMatchSceneBatch() => BakeBatch(true);

        [MenuItem("WarCore/M7/Bake Scene (batch)")]
        public static void BakeM7SceneBatch() => BakeBatch(true, true);

        private static void BakeBatch(bool match, bool m7 = false)
        {
            try
            {
                Bake(match, m7);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        [MenuItem("WarCore/M2/Play")]
        public static void Play() => Run("");

        // Steps to ticks 0/150/300/600 and writes a PNG at each; needs the Game view visible. WARCORE_SHOT_DIR overrides the folder.
        [MenuItem("WarCore/M2/Play And Capture")]
        public static void PlayAndCapture()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
            Run(string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/screenshots/M2b")) : dir);
        }

        [MenuItem("WarCore/M3/Play")]
        public static void PlayPath() => Run("", true);

        // Same as the M2 capture, but units route around walls (M3); also writes frames/ for a video.
        [MenuItem("WarCore/M3/Play And Capture")]
        public static void PlayPathAndCapture()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
            Run(string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/screenshots/M3")) : dir, true);
        }

        [MenuItem("WarCore/M4/Play")]
        public static void PlayCrowd() => Run("", true, true);

        // Two squads of ten walk to their slots without overlapping (M4); same capture layout as M3.
        [MenuItem("WarCore/M4/Play And Capture")]
        public static void PlayCrowdAndCapture()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
            Run(string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/screenshots/M4")) : dir, true, true);
        }

        [MenuItem("WarCore/M5/Play")]
        public static void PlayCombat() => Run("", true, true, true);

        // Two squads of ten fight with hitscan weapons (M5); same capture layout as M4 plus an end-of-fight picture.
        [MenuItem("WarCore/M5/Play And Capture")]
        public static void PlayCombatAndCapture()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
            Run(string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/screenshots/M5")) : dir, true, true, true);
        }

        [MenuItem("WarCore/M6/Play")]
        public static void PlayMatch() => Run("", true, true, true, true);

        // Three control points and simple AI on both sides (M6): runs to the end of the match; shots at 0/150/300/600/1500/3000 and the end.
        [MenuItem("WarCore/M6/Play And Capture")]
        public static void PlayMatchAndCapture()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
            Run(string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/screenshots/M6")) : dir, true, true, true, true);
        }

        [MenuItem("WarCore/M7/Play")]
        public static void PlayM7() => Run("", true, true, true, true, true);

        [MenuItem("WarCore/M7/Play And Capture")]
        public static void PlayM7AndCapture()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
            Run(string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/screenshots/M7")) : dir,
                true, true, true, true, true);
        }

        [MenuItem("WarCore/P0/Play")]
        public static void PlayP0() => RunP0("");

        [MenuItem("WarCore/MP/Play")]
        public static void PlayMp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (!File.Exists(M7ScenePath))
                Bake(true, true);
            if (!File.Exists(M7ScenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(M7ScenePath);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            SessionState.EraseBool(P0Host.ModeKey);
            SessionState.EraseString(P0Host.ShotDirKey);
            SessionState.SetBool(MpHost.ModeKey, true);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("WarCore/P0/Play And Capture")]
        public static void PlayP0AndCapture()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
            RunP0(string.IsNullOrEmpty(dir) ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/screenshots/P0")) : dir);
        }

        // The M7 scene and map, driven by P0Host instead of M2Host (M2Host hands over when ModeKey is set).
        private static void RunP0(string shotDir)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (!File.Exists(M7ScenePath))
                Bake(true, true);
            if (!File.Exists(M7ScenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(M7ScenePath);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (shotDir != "")
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "WarCore P0 shots");
            SessionState.EraseBool(MpHost.ModeKey);
            SessionState.SetBool(P0Host.ModeKey, true);
            SessionState.SetString(P0Host.ShotDirKey, shotDir);
            EditorApplication.isPlaying = true;
        }

        private static void Run(string shotDir, bool pathMode = false, bool crowdMode = false, bool combatMode = false, bool matchMode = false, bool m7 = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            string scenePath = DemoScenePath(matchMode, m7);
            if (!File.Exists(scenePath))
                Bake(matchMode, m7);
            if (!File.Exists(scenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (shotDir != "")
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, m7 ? "WarCore M7 shots" : matchMode ? "WarCore M6 shots" : combatMode ? "WarCore M5 shots" : crowdMode ? "WarCore M4 shots" : pathMode ? "WarCore M3 shots" : "WarCore M2b shots");
            SessionState.EraseBool(MpHost.ModeKey);
            SessionState.SetString(M2Host.ShotDirKey, shotDir);
            SessionState.SetBool(M2Host.PathModeKey, pathMode);
            SessionState.SetBool(M2Host.CrowdModeKey, crowdMode);
            SessionState.SetBool(M2Host.CombatModeKey, combatMode);
            SessionState.SetBool(M2Host.MatchModeKey, matchMode);
            SessionState.SetBool(M2Host.M7ModeKey, m7);
            EditorApplication.isPlaying = true;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            // Flat colour: from above the demo should read as a plan, not as lit 3D.
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Unlit/Color"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Unlit/Color");
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
