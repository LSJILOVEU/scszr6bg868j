using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WarCore.Verification.Unity
{
    public sealed class ProfilerAllocationDriver : MonoBehaviour
    {
        private ProfilerAllocationMeter meter;
        private ProfilerAllocationRun run = null;
        private string path;
        private string metadata = "";
        private System.Diagnostics.Stopwatch watchdog = null;
        private bool finished;
        private long watchdogLimitMs = 900000;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
#if !UNITY_EDITOR
            if (HashVectorRunner.Argument("-g23Profiler", "false") == "true" || HashVectorRunner.Argument("-g23Bisect", "none") != "none")
            {
                var owner = new GameObject("G23 Profiler Allocation Driver");
                DontDestroyOnLoad(owner);
                owner.AddComponent<ProfilerAllocationDriver>().Initialize();
            }
#endif
        }

        private void Initialize()
        {
            path = Path.GetFullPath(HashVectorRunner.Argument("-g23Output", "Tools/g23/out/profiler/allocations.txt"));
            try
            {
#if !ENABLE_IL2CPP || UNITY_EDITOR
                throw new InvalidOperationException("Use the IL2CPP Development Build, or the separate Editor update probe.");
#else
                if (!Debug.isDebugBuild)
                    throw new InvalidOperationException("Profiler allocation player requires a Development Build.");
                if (HashVectorRunner.Argument("-g23Run", "false") == "true")
                    throw new InvalidOperationException("Do not combine the synchronous G23 entry with the frame driver.");
                Application.runInBackground = true;
                QualitySettings.vSyncCount = 0;
                if (!int.TryParse(HashVectorRunner.Argument("-g23FrameRate", "-1"), out int frameRate)
                    || frameRate != -1 && (frameRate < 1 || frameRate > 1000))
                    throw new ArgumentException("-g23FrameRate requires -1 (uncapped) or 1..1000.");
                Application.targetFrameRate = frameRate;
                metadata = Metadata("IL2CPP-Development", "Player.Update / Time.frameCount")
                    + "frame_pacing|target_fps=" + frameRate + ";actual_intervals=in-csv;no-rate-based-acceptance=true\n"
                    + File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "g23-build-info.txt"));
                string bisectName = HashVectorRunner.Argument("-g23Bisect", "none");
                var spin = ProfilerSpinIdle.FromArguments(bisectName);
                var workload = bisectName == "none" ? null : new ProfilerBisectWorkload(bisectName, spin);
                int planned = workload == null ? ProfilerAllocationRun.TotalFrames : ProfilerAllocationRun.BisectTotalFrames;
                if (frameRate > 0) watchdogLimitMs = Math.Max(watchdogLimitMs, (planned + 1L) * 1000 / frameRate + 120000);
                if (spin != null) watchdogLimitMs += spin.BudgetMilliseconds;
                meter = new ProfilerAllocationMeter(planned + 256);
                run = new ProfilerAllocationRun(meter, HashVectorRunner.Argument("-g23InjectAlloc", "false") == "true", workload);
                watchdog = System.Diagnostics.Stopwatch.StartNew();
#endif
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private void Update()
        {
            if (finished || run == null)
                return;
            try
            {
                if (watchdog.ElapsedMilliseconds > watchdogLimitMs)
                    throw new TimeoutException("Profiler allocation player exceeded its pacing-adjusted deadline.");
                run.AdvanceFrame(Time.frameCount);
                if (!run.Complete)
                    return;
                run.Write(path, metadata);
                int code = run.ExitCode;
                Finish();
                Application.Quit(code);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        public static string Metadata(string environment, string frameClock)
        {
            return "environment=" + environment + ";unity=" + Application.unityVersion
                + ";development=" + Debug.isDebugBuild + ";architecture=" + RuntimeInformation.ProcessArchitecture
                + ";cpu=" + SystemInfo.processorType + ";os=" + SystemInfo.operatingSystem + "\n"
                + "frame_clock=" + frameClock + ";workload_calls_per_active_frame=1;baseline_workload_calls=0;timing_acceptance=false\n";
        }

        private void Fail(Exception error)
        {
            Finish();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".error.txt", error.ToString());
            Debug.LogException(error);
            Application.Quit(1);
        }

        private void Finish()
        {
            finished = true;
            meter?.Dispose();
            meter = null;
        }

        private void OnDestroy()
        {
            Finish();
        }
    }
}
