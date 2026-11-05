using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using WarCore.Verification.Unity;

namespace WarCore.Verification.Editor
{
    // ExecuteMethod must return to the Editor loop; -quit would exit before counters are flushed.
    public static class G23ProfilerProbe
    {
        private static ProfilerAllocationMeter meter;
        private static ProfilerAllocationRun run;
        private static string path;
        private static string metadata;
        private static int update;
        private static double deadline;

        public static void Run()
        {
            path = Path.GetFullPath(HashVectorRunner.Argument("-g23Output", "Tools/g23/out/profiler/editor-profiler/allocations.txt"));
            try
            {
                if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-quit") >= 0)
                    throw new InvalidOperationException("Use batchmode executeMethod without -quit for the Editor frame probe.");
                if (run != null || EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Editor allocation probe requires an idle Edit Mode session.");
                metadata = ProfilerAllocationDriver.Metadata("Editor-Mono", "EditorApplication.update sequence; freshness checked by recorder sample counts")
                    + "editor_optimization=" + CompilationPipeline.codeOptimization + ";play_mode=false\n"
                    + "sources|sha256=" + G23Build.SourceStamp() + "\n";
                string bisectName = HashVectorRunner.Argument("-g23Bisect", "none");
                var spin = ProfilerSpinIdle.FromArguments(bisectName);
                var workload = bisectName == "none" ? null : new ProfilerBisectWorkload(bisectName, spin);
                int planned = workload == null ? ProfilerAllocationRun.TotalFrames : ProfilerAllocationRun.BisectTotalFrames;
                meter = new ProfilerAllocationMeter(planned + 256);
                run = new ProfilerAllocationRun(meter, HashVectorRunner.Argument("-g23InjectAlloc", "false") == "true", workload);
                update = 0;
                deadline = EditorApplication.timeSinceStartup + 900;
                if (spin != null) deadline += spin.BudgetMilliseconds / 1000.0;
                EditorApplication.update += Tick;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("Editor profiler probe exceeded 15 minutes.");
                run.AdvanceFrame(++update);
                if (!run.Complete)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                run.Write(path, metadata);
                int code = run.ExitCode;
                Cleanup();
                EditorApplication.Exit(code);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static void Fail(Exception error)
        {
            Cleanup();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".error.txt", error.ToString());
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }

        private static void Cleanup()
        {
            EditorApplication.update -= Tick;
            meter?.Dispose();
            meter = null;
            run = null;
        }
    }
}
