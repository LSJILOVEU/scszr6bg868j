using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    // L8b: play the demo terrain match (or the A3a search demo) in a new, unsaved scene driven by TerrainHost.
    // The capture menus need WARCORE_SHOT_DIR (no default); every output is written flat in that directory.
    // WARCORE_TERRAIN_LAYER names the terrain layer (default rwr-terrain-demo); WARCORE_SHOT_PREFIX the file prefix
    // (default L8b, or L8b-A3a for the A3a demo). No scene or material asset is written.
    public static class L8bTerrain
    {
        [MenuItem("WarCore/L8b/Play")]
        public static void Play() => Run(TerrainHost.TerrainDemoName, false);

        [MenuItem("WarCore/L8b/Play And Capture")]
        public static void PlayAndCapture() => Run(TerrainHost.TerrainDemoName, true);

        [MenuItem("WarCore/L8b/Play A3a")]
        public static void PlayA3a() => Run(TerrainHost.A3aDemoName, false);

        [MenuItem("WarCore/L8b/Play A3a And Capture")]
        public static void PlayA3aAndCapture() => Run(TerrainHost.A3aDemoName, true);

        private static bool Batch => Application.isBatchMode || Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1";

        private static void Run(string demo, bool capture)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            string dir = "";
            if (capture)
            {
                dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    Debug.LogError("L8b capture needs WARCORE_SHOT_DIR set to an existing directory (got '" + dir + "'); not entering Play mode.");
                    if (Batch)
                        EditorApplication.Exit(2);
                    return;
                }
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("L8b TerrainHost").AddComponent<TerrainHost>();
            // Game view: in the Scene view WaitForEndOfFrame never comes and no picture is taken.
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (capture)
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "WarCore L8b shots");
            string layer = Environment.GetEnvironmentVariable("WARCORE_TERRAIN_LAYER");
            string prefix = Environment.GetEnvironmentVariable("WARCORE_SHOT_PREFIX");
            if (string.IsNullOrEmpty(prefix))
                prefix = demo == TerrainHost.A3aDemoName ? TerrainHost.DefaultPrefix + "-A3a" : TerrainHost.DefaultPrefix;
            SessionState.SetString(TerrainHost.DemoKey, demo);
            SessionState.SetString(TerrainHost.LayerKey, string.IsNullOrEmpty(layer) ? TerrainHost.DefaultLayer : layer);
            SessionState.SetString(TerrainHost.ShotDirKey, dir);
            SessionState.SetString(TerrainHost.PrefixKey, prefix);
            EditorApplication.isPlaying = true;
        }
    }
}
