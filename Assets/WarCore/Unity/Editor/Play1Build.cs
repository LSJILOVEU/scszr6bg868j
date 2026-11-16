using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarCore.Unity.Editor
{
    public static class Play1Build
    {
        public static void Build()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use the PLAY-1 batch build entry point.");
            string[] args = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(args, "-play1Output");
            if (outputIndex < 0 || outputIndex + 1 == args.Length)
                throw new ArgumentException("Pass -play1Output with the output .app path.");
            string output = Path.GetFullPath(args[outputIndex + 1]);
            var target = NamedBuildTarget.Standalone;
            var oldBackend = PlayerSettings.GetScriptingBackend(target);
            bool oldDevelopment = EditorUserBuildSettings.development;
            string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/WarCore/Unity/Scenes/Play1Generated.unity");
            try
            {
                if (oldBackend != ScriptingImplementation.Mono2x)
                    PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                EditorUserBuildSettings.development = false;
                var scene = EditorSceneManager.OpenScene("Assets/WarCore/Unity/Scenes/M7Map.unity", OpenSceneMode.Single);
                var host = UnityEngine.Object.FindAnyObjectByType<M2Host>();
                host.gameObject.AddComponent<MpHost>();
                UnityEngine.Object.DestroyImmediate(host);
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new IOException("Cannot save PLAY-1 bootstrap scene.");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath }, locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.None
                });
                if (result.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("PLAY-1 build failed: " + result.summary.result);
            }
            finally
            {
                if (oldBackend != ScriptingImplementation.Mono2x)
                    PlayerSettings.SetScriptingBackend(target, oldBackend);
                EditorUserBuildSettings.development = oldDevelopment;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
