using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    // L10: play the scripted terrain-player demo in a new, unsaved scene driven by TerrainPlayerHost. Shots needs
    // WARCORE_SHOT_DIR (no default); WARCORE_SHOT_PREFIX names the file prefix (default L10). No asset is written.
    public static class L10Terrain
    {
        [MenuItem("WarCore/L10/Terrain Player")]
        public static void Play() => Run(false);

        [MenuItem("WarCore/L10/Terrain Player And Capture")]
        public static void Shots() => Run(true);

        private static bool Batch => Application.isBatchMode || Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1";

        private static void Run(bool capture)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            string dir = "";
            if (capture)
            {
                dir = Environment.GetEnvironmentVariable("WARCORE_SHOT_DIR");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    Debug.LogError("L10 capture needs WARCORE_SHOT_DIR set to an existing directory (got '" + dir + "'); not entering Play mode.");
                    if (Batch)
                        EditorApplication.Exit(2);
                    return;
                }
            }
            string prefix = Environment.GetEnvironmentVariable("WARCORE_SHOT_PREFIX");
            if (string.IsNullOrEmpty(prefix))
                prefix = TerrainPlayerHost.DefaultPrefix;
            if (!TerrainPlayerHost.IsShotPrefix(prefix))
            {
                Debug.LogError("L10: WARCORE_SHOT_PREFIX must be a plain file name starting with " + TerrainPlayerHost.DefaultPrefix
                    + " (letters, digits, '-', '_'); got '" + prefix + "'. Not entering Play mode.");
                if (Batch)
                    EditorApplication.Exit(2);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("L10 TerrainPlayerHost").AddComponent<TerrainPlayerHost>();
            // Game view: in the Scene view WaitForEndOfFrame never comes and no picture is taken.
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (capture)
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "WarCore L10 shots");
            SessionState.SetString(TerrainPlayerHost.ShotDirKey, dir);
            SessionState.SetString(TerrainPlayerHost.PrefixKey, prefix);
            EditorApplication.isPlaying = true;
        }
    }
}
