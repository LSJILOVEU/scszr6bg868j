// Generated from Tools/g23/G23Build.cs; do not edit.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEditor.OSXStandalone;
using UnityEngine;
using WarCore.Verification;
using WarCore.Verification.Unity;

namespace WarCore.Verification.Editor
{
    // Copied under Assets by generate.py so Unity can import this Tools-owned entry point.
    public static class G23Build
    {
        private const string Checks = "--emit-null-checks --enable-array-bounds-check --enable-divide-by-zero-check";
        private const string ScenePath = "Assets/WarCore/Verification/Unity/G23Generated.unity";
        private const string InfoPath = "Assets/StreamingAssets/g23-build-info.txt";

        public static void RunMono()
        {
            // Set by run.sh's -releaseCodeOptimization before scripts are compiled.
            if (CompilationPipeline.codeOptimization != CodeOptimization.Release)
                throw new InvalidOperationException("Start the Editor with -releaseCodeOptimization for comparable Mono measurements.");
            G23Entry.EditorSourceStamp = SourceStamp();
            EditorApplication.Exit(G23Entry.Execute(CompilationPipeline.codeOptimization.ToString()));
        }

        public static void Build()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use the G23 batch build script.");
            var target = NamedBuildTarget.Standalone;
            var oldBackend = PlayerSettings.GetScriptingBackend(target);
            var oldCompiler = PlayerSettings.GetIl2CppCompilerConfiguration(target);
            var oldCodegen = PlayerSettings.GetIl2CppCodeGeneration(target);
            var oldArchitecture = UserBuildSettings.architecture;
            string oldArgs = PlayerSettings.GetAdditionalIl2CppArgs();
            bool oldDevelopment = EditorUserBuildSettings.development;
            bool oldDebugging = EditorUserBuildSettings.allowDebugging;
            bool oldProfiler = EditorUserBuildSettings.connectProfiler;
            bool oldDeep = EditorUserBuildSettings.buildWithDeepProfilingSupport;
            bool development = HashVectorRunner.Argument("-g23Development", "false") == "true";
            OSArchitecture requestedArchitecture = development ? OSArchitecture.ARM64 : OSArchitecture.x64ARM64;
            if (File.Exists(ScenePath) || File.Exists(InfoPath)) throw new InvalidOperationException("Stale G23 build staging assets exist; inspect and remove them first.");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IL2CPP_ADDITIONAL_ARGS")))
                throw new InvalidOperationException("Unset IL2CPP_ADDITIONAL_ARGS so check settings have one source.");
            string output = Path.GetFullPath(HashVectorRunner.Argument("-g23Build", "Builds/G23/WarCoreG23.app"));
            try
            {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetIl2CppCompilerConfiguration(target, Il2CppCompilerConfiguration.Release);
                PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSpeed);
                UserBuildSettings.architecture = requestedArchitecture;
                var architecture = UserBuildSettings.architecture;
                PlayerSettings.SetAdditionalIl2CppArgs(Checks);
                EditorUserBuildSettings.development = development;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
                if (PlayerSettings.GetScriptingBackend(target) != ScriptingImplementation.IL2CPP
                    || PlayerSettings.GetIl2CppCompilerConfiguration(target) != Il2CppCompilerConfiguration.Release
                    || PlayerSettings.GetIl2CppCodeGeneration(target) != Il2CppCodeGeneration.OptimizeSpeed
                    || architecture != requestedArchitecture || PlayerSettings.GetAdditionalIl2CppArgs() != Checks
                    || EditorUserBuildSettings.development != development || EditorUserBuildSettings.allowDebugging
                    || EditorUserBuildSettings.connectProfiler || EditorUserBuildSettings.buildWithDeepProfilingSupport)
                    throw new InvalidOperationException("G23 build setting readback failed.");
                // Sim is untouched. A new source override must be reviewed before claiming these global options apply.
                foreach (string source in Directory.GetFiles("Assets/WarCore/Sim", "*.cs", SearchOption.AllDirectories))
                    if (File.ReadAllText(source).Contains("Il2CppSetOption"))
                        throw new InvalidOperationException("Sim has an IL2CPP check override: " + source);
                string info = "build|backend=" + PlayerSettings.GetScriptingBackend(target)
                    + ";cpp=" + PlayerSettings.GetIl2CppCompilerConfiguration(target)
                    + ";codegen=" + PlayerSettings.GetIl2CppCodeGeneration(target)
                    + ";architecture=" + architecture + ";development=" + development.ToString().ToLowerInvariant()
                    + ";scriptDebugging=false;connectProfiler=false;deepProfiling=false;unity=" + Application.unityVersion + "\n"
                    + "purpose|" + (development ? "Development Build; allocation measurement only; timing cannot determine acceptance" : "Release non-development; timing eligible for separate G24 review") + "\n"
                    + "il2cpp_checks|DivideByZeroChecks=true;NullChecks=true;ArrayBoundsChecks=true;Sim.Il2CppSetOption=none;source=explicit-global-args-readback\n"
                    + "il2cpp_args|" + PlayerSettings.GetAdditionalIl2CppArgs() + "\n"
                    + "sources|sha256=" + SourceStamp() + "\n";
                Directory.CreateDirectory(Path.GetDirectoryName(InfoPath));
                File.WriteAllText(InfoPath, info);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Cannot save G23 bootstrap scene.");
                AssetDatabase.Refresh();
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.StrictMode | (development ? BuildOptions.Development : BuildOptions.None)
                });
                if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("G23 build failed: " + result.summary.result);
                if (((result.summary.options & BuildOptions.Development) != 0) != development
                    || (result.summary.options & (BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler | BuildOptions.EnableDeepProfilingSupport)) != 0)
                    throw new InvalidOperationException("G23 BuildReport options differ from the requested settings.");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-settings.txt"), info
                    + "build_report|platform=" + result.summary.platform + ";options=" + result.summary.options + ";result=" + result.summary.result + "\n");
                Console.Write(info);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, oldBackend);
                PlayerSettings.SetIl2CppCompilerConfiguration(target, oldCompiler);
                PlayerSettings.SetIl2CppCodeGeneration(target, oldCodegen);
                UserBuildSettings.architecture = oldArchitecture;
                PlayerSettings.SetAdditionalIl2CppArgs(oldArgs);
                EditorUserBuildSettings.development = oldDevelopment;
                EditorUserBuildSettings.allowDebugging = oldDebugging;
                EditorUserBuildSettings.connectProfiler = oldProfiler;
                EditorUserBuildSettings.buildWithDeepProfilingSupport = oldDeep;
                AssetDatabase.DeleteAsset(ScenePath);
                AssetDatabase.DeleteAsset(InfoPath);
                AssetDatabase.SaveAssets();
            }
        }

        internal static string SourceStamp()
        {
            var paths = new[] { "Assets/WarCore/Sim", "Assets/WarCore/Verification" }
                .SelectMany(root => Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".asmdef", StringComparison.Ordinal) || path.EndsWith("link.xml", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal);
            using (var hash = SHA256.Create())
            using (var data = new MemoryStream())
            {
                foreach (string path in paths)
                {
                    byte[] name = Encoding.UTF8.GetBytes(path.Replace('\\', '/') + "\n");
                    data.Write(name, 0, name.Length);
                    byte[] bytes = File.ReadAllBytes(path);
                    data.Write(bytes, 0, bytes.Length);
                    data.WriteByte(0);
                }
                return BitConverter.ToString(hash.ComputeHash(data.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
