using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    // MAP-BL0: play the walled ward alone in a new, unsaved scene driven by ShowcaseHost (stage set ShowcaseHost.WardSet).
    // "Play Walled Ward" is played by hand; "Play And Capture Walled Ward" runs the stage's script and needs
    // WARCORE_SHOT_DIR (no default). No asset is written. ShowcaseMenu is left as it is.
    public static class WardMenu
    {
        [MenuItem("WarCore/Showcase/Play Walled Ward")]
        public static void Play() => Run(false);

        [MenuItem("WarCore/Showcase/Play And Capture Walled Ward")]
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
                    Debug.LogError("MAP-BL0 capture needs WARCORE_SHOT_DIR set to an existing directory (got '" + dir + "'); not entering Play mode.");
                    if (Batch)
                        EditorApplication.Exit(2);
                    return;
                }
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("MAP-BL0 ShowcaseHost").AddComponent<ShowcaseHost>();
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (capture)
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "WarCore MAP-BL0 shots");
            SessionState.SetString(ShowcaseHost.ShotDirKey, dir);
            SessionState.SetInt(ShowcaseHost.StageKey, 0);
            SessionState.SetString(ShowcaseHost.SetKey, ShowcaseHost.WardSet);
            EditorApplication.isPlaying = true;
        }
    }
}
