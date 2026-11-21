using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    // SHOW-1: play the showcase in a new, unsaved scene driven by ShowcaseHost. "Play" is played by hand; "Play And Capture"
    // runs every stage's script and needs WARCORE_SHOT_DIR (no default). No asset is written.
    public static class ShowcaseMenu
    {
        [MenuItem("WarCore/Showcase/Play")]
        public static void Play() => Run(false);

        [MenuItem("WarCore/Showcase/Play And Capture")]
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
                    Debug.LogError("SHOW-1 capture needs WARCORE_SHOT_DIR set to an existing directory (got '" + dir + "'); not entering Play mode.");
                    if (Batch)
                        EditorApplication.Exit(2);
                    return;
                }
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("SHOW-1 ShowcaseHost").AddComponent<ShowcaseHost>();
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (capture)
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "WarCore SHOW-1 shots");
            SessionState.SetString(ShowcaseHost.ShotDirKey, dir);
            SessionState.SetInt(ShowcaseHost.StageKey, 0);
            EditorApplication.isPlaying = true;
        }
    }
}
