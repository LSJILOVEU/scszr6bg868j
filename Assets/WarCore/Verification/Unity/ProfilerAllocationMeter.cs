using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification.Unity
{
    public sealed class ProfilerAllocationMeter : IProfilerFrameSource
    {
        public const string ByteCounter = "GC Allocated In Frame";
        public const string CountCounter = "GC Allocation In Frame Count";
        private ProfilerRecorder bytes;
        private ProfilerRecorder counts;
        private int byteStart;
        private int countStart;
        private int frame;
        private bool pending;
        private string failure;
        public string Description { get; private set; }

        public ProfilerAllocationMeter(int capacity)
        {
            try
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var inventory = new StringBuilder();
                foreach (ProfilerRecorderHandle handle in handles)
                {
                    ProfilerRecorderDescription info = ProfilerRecorderHandle.GetDescription(handle);
                    if (info.Name.Contains("GC"))
                        inventory.Append("counter|category=").Append(info.Category.Name).Append(";name=")
                            .Append(info.Name).Append(";unit=").Append(info.UnitType).Append('\n');
                }
                // No ring wrap: a full buffer must fail the freshness check instead of repeating zero.
                const ProfilerRecorderOptions options = ProfilerRecorderOptions.SumAllSamplesInFrame;
                bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, ByteCounter, capacity, options);
                counts = ProfilerRecorder.StartNew(ProfilerCategory.Memory, CountCounter, capacity, options);
                if (!Usable)
                    failure = "Memory frame counters invalid or not running; use a Development Build or an Editor with profiler support.";
                else if (bytes.UnitType != ProfilerMarkerDataUnit.Bytes || counts.UnitType != ProfilerMarkerDataUnit.Count)
                    failure = "Unexpected counter units; byte evidence requires Bytes and allocation count requires Count.";
                Description = "profiler|category=Memory;bytes=" + ByteCounter + ";count=" + CountCounter
                    + ";capacity=" + capacity + ";wrap=false;scope=whole-frame-all-threads;read=next-frame\n" + inventory;
            }
            catch (Exception error)
            {
                failure = "Profiler initialization failed: " + error.GetType().Name + ": " + error.Message;
                Description = "profiler|initialization=unavailable;reason=" + failure + "\n";
            }
        }

        private bool Usable => bytes.Valid && counts.Valid && bytes.IsRunning && counts.IsRunning;

        public void BeginFrame(int frameId)
        {
            if (pending)
                throw new InvalidOperationException("A profiler frame is still pending.");
            frame = frameId;
            pending = true;
            if (failure != null || !Usable)
                return;
            byteStart = bytes.Count;
            countStart = counts.Count;
        }

        public ProfilerFrameReading ReadCompletedFrame(int followingFrame)
        {
            if (!pending)
                throw new InvalidOperationException("No profiler frame was scheduled.");
            pending = false;
            if (failure != null || !Usable)
                return ProfilerFrameReading.Unavailable(frame, failure ?? "Profiler stopped during the run.");
            if (followingFrame != frame + 1)
                return ProfilerFrameReading.Unavailable(frame, "Player/update callback skipped or repeated a frame.");
            int newBytes = bytes.Count - byteStart;
            int newCounts = counts.Count - countStart;
            // LastValue is finalized at the next frame. Sample.Count is NOT the allocation-count counter value.
            long byteValue = newBytes == 1 ? bytes.GetSample(bytes.Count - 1).Value : 0;
            long countValue = newCounts == 1 ? counts.GetSample(counts.Count - 1).Value : 0;
            return ProfilerFrameReading.Completed(frame, newBytes, newCounts, byteValue, countValue);
        }

        public void Dispose()
        {
            bytes.Dispose();
            counts.Dispose();
        }
    }
}
