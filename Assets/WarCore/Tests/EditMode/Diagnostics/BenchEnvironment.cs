using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WarCore.Tests.Diagnostics
{
    internal static class BenchEnvironment
    {
        internal static string OutputDirectory()
        {
            string root = Directory.GetCurrentDirectory();
            if (!Directory.Exists(Path.Combine(root, "Assets", "WarCore")))
                root = Path.GetDirectoryName(ArchitectureTests.AssetRoot());
#if UNITY_EDITOR
            string backend = "unity-editor-mono";
#else
            string backend = "standalone-mono";
#endif
            string directory = Path.Combine(root, "TestResults", "m0b3-stage1", backend);
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal static void Report(TextWriter output)
        {
#if UNITY_EDITOR
            output.WriteLine("Environment: Unity Editor Mono; Unity={0}; editor codeOptimization={1}; NOT IL2CPP Release.",
                UnityEngine.Application.unityVersion, UnityEditor.Compilation.CompilationPipeline.codeOptimization);
#else
            output.WriteLine("Environment: standalone Mono from Unity distribution; Tools/run-managed-checks.sh uses csc -checked+ without -optimize+; NOT Unity Editor; NOT IL2CPP Release.");
#endif
            output.WriteLine("Runtime={0}; OS={1}; processArchitecture={2}; Stopwatch.Frequency={3}; GC=runtime-probed-allocation-meter; G24=UNVERIFIED.",
                Environment.Version, Environment.OSVersion, RuntimeInformation.ProcessArchitecture, Stopwatch.Frequency);
        }
    }
}
