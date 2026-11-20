using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    // SEE-1: play the street demo (StreetDemo) in a new, unsaved scene driven by PlateHost. "Street Demo" is played by hand (WASD, mouse,
    // left button); "Street Demo And Capture" runs the scripted walk and needs WARCORE_SHOT_DIR (no default). No asset is written.
    public static class See1Street
    {
        [MenuItem("WarCore/SEE-1/Street Demo")]
        public static void Play() => Run(false);

        [MenuItem("WarCore/SEE-1/Street Demo And Capture")]
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
                    Debug.LogError("SEE-1 capture needs WARCORE_SHOT_DIR set to an existing directory (got '" + dir + "'); not entering Play mode.");
                    if (Batch)
                        EditorApplication.Exit(2);
                    return;
                }
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("SEE-1 PlateHost").AddComponent<PlateHost>();
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            if (capture)
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "WarCore SEE-1 shots");
            SessionState.SetString(PlateHost.ShotDirKey, dir);
            SessionState.SetString(PlateHost.DemoKey, PlateHost.StreetDemoName);
            EditorApplication.isPlaying = true;
        }
    }
}
