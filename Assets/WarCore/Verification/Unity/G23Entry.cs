using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WarCore.Verification.Unity
{
    public static class G23Entry
    {
        public static string EditorSourceStamp { get; set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartPlayer()
        {
#if !UNITY_EDITOR
            if (HashVectorRunner.Argument("-g23Run", "false") == "true")
                Application.Quit(Execute("not-editor"));
#endif
        }

        public static int Execute(string editorOptimization)
        {
            string path = Path.GetFullPath(HashVectorRunner.Argument("-g23Output", "Tools/g23/out/verification.txt"));
            string mode = HashVectorRunner.Argument("-g23Bench", "full");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                string probe = HashVectorRunner.Argument("-g23SingleProbe", "");
                if (probe.Length != 0)
                {
                    File.WriteAllText(path + ".started", probe + ";architecture=" + RuntimeInformation.ProcessArchitecture);
                    string result = RawRuntimeProbes.Evaluate(probe);
                    File.WriteAllText(path, result + "\n");
                    Console.WriteLine(probe + "|" + result);
                    return 0;
                }
                if (mode != "smoke" && mode != "full" && mode != "none") throw new ArgumentException("-g23Bench requires smoke, full or none");
#if UNITY_EDITOR
                const string backend = "UnityEditorMono";
#elif ENABLE_IL2CPP
                const string backend = "IL2CPP";
#else
                const string backend = "PlayerMono";
#endif
                string metadata = "backend=" + backend + ";architecture=" + RuntimeInformation.ProcessArchitecture
                    + ";unity=" + Application.unityVersion + ";development=" + Debug.isDebugBuild
                    + ";editorOptimization=" + editorOptimization + ";benchmark=" + mode + "\n";
                metadata += "platform|os=" + SystemInfo.operatingSystem + ";cpu=" + SystemInfo.processorType
                    + ";osArchitecture=" + RuntimeInformation.OSArchitecture + ";pointerBytes=" + IntPtr.Size + "\n";
#if ENABLE_IL2CPP && !UNITY_EDITOR
                metadata += File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "g23-build-info.txt"));
                if (Debug.isDebugBuild) throw new InvalidOperationException("G23 requires a non-development player.");
                RawRuntimeProbes.IsolatedExecutor = name => IsolateProbe(name, path);
#else
                metadata += "il2cpp_checks|not-applicable-to-Mono\n";
                metadata += "sources|sha256=" + EditorSourceStamp + "\n";
#endif
                File.WriteAllText(path + ".meta.txt", metadata);
                Console.Write(metadata);
                string churnMode = HashVectorRunner.Argument("-g23ChurnBench", "none");
                if (churnMode != "none")
                {
                    Round4Runner.Run(churnMode,path + ".churn.txt",HashVectorRunner.Argument("-g23LongRun", "false") == "true",
                        HashVectorRunner.Argument("-g23PlacedBench", "false") == "true");
                    return 0;
                }
                string identityMode = HashVectorRunner.Argument("-g23IdentityBench", "none");
                if (identityMode != "none")
                {
                    StableIdentityRunner.Run(identityMode,path + ".identity.txt");
                    return 0;
                }
                HashVectorRunner.Run();
                if (mode != "none") BenchmarkRunner.Run(mode, path + ".bench.txt");
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(path + ".error.txt", error.ToString());
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static string IsolateProbe(string name, string outputPath)
        {
            string resultPath = outputPath + "." + name;
            foreach (string suffix in new[] { "", ".started", ".error.txt", ".exit.txt" })
                if (File.Exists(resultPath + suffix)) File.Delete(resultPath + suffix);
            string executable = MacProbeProcess.Executable(Application.dataPath);
            Architecture architecture = RuntimeInformation.ProcessArchitecture;
            if (architecture != Architecture.Arm64 && architecture != Architecture.X64)
                throw new PlatformNotSupportedException("Raw probes require macOS arm64 or x86_64.");
            int status = MacProbeProcess.Run(new[]
            {
                // arch pins the same Universal slice as the parent, including translated x86_64.
                "/usr/bin/arch", architecture == Architecture.Arm64 ? "-arm64" : "-x86_64", executable,
                "-batchmode", "-nographics", "-g23Run", "true", "-g23SingleProbe", name,
                "-g23Output", resultPath, "-logFile", resultPath + ".log"
            }, 120000);
            int exitCode = MacProbeProcess.ExitCode(status);
            File.WriteAllText(resultPath + ".exit.txt", "wait-status=" + status + ";exit-code=" + exitCode
                + ";signal=" + MacProbeProcess.Signal(status) + "\n");
            if (File.Exists(resultPath + ".error.txt")) throw new InvalidOperationException(File.ReadAllText(resultPath + ".error.txt"));
            if (!File.Exists(resultPath + ".started") || File.ReadAllText(resultPath + ".started")
                != name + ";architecture=" + architecture)
                throw new InvalidOperationException("Raw probe process did not reach the requested instruction on the requested slice.");
            if (File.Exists(resultPath))
            {
                string result = File.ReadAllText(resultPath).TrimEnd('\n');
                if (exitCode != 0) throw new InvalidOperationException("Raw probe child exited after writing a result: " + exitCode);
                return result;
            }
            if (exitCode == 0) throw new InvalidOperationException("Raw probe child produced no result: " + name);
            // Native traps do not have a managed exception type. Preserve the OS exit status explicitly.
            return "process-exit:" + exitCode + ";no-managed-result";
        }
    }
}
