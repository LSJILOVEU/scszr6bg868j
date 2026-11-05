using System;
using System.Globalization;

namespace WarCore.Tests
{
    // Frame counters are not cumulative method counters; keep them out of IAllocationMeter.Difference.
    public readonly struct ProfilerFrameReading
    {
        public readonly int Frame;
        public readonly long Bytes;
        public readonly long Allocations;
        public readonly bool Available;
        public readonly string Reason;

        private ProfilerFrameReading(int frame, long bytes, long allocations, bool available, string reason)
        {
            Frame = frame;
            Bytes = bytes;
            Allocations = allocations;
            Available = available;
            Reason = reason;
        }

        public static ProfilerFrameReading Unavailable(int frame, string reason)
            => new ProfilerFrameReading(frame, 0, 0, false, reason);

        public static ProfilerFrameReading Completed(int frame, int byteSamples, int countSamples, long bytes, long allocations)
        {
            // A stale sample, a missed frame or a full buffer cannot establish a per-tick value.
            if (byteSamples != 1 || countSamples != 1)
                return Unavailable(frame, "Expected one completed sample per counter between frame callbacks.");
            if (bytes < 0 || allocations < 0 || (bytes == 0) != (allocations == 0))
                return Unavailable(frame, "Allocation byte/count counters disagree or contain negative values.");
            return new ProfilerFrameReading(frame, bytes, allocations, true, null);
        }
    }

    // The same policy is generated into Verification; only Unity supplies the real frame readings.
    public sealed class ProfilerFrameEvidence
    {
        public const int ControlBytes = 32768;
        private bool openingControl;
        private bool closingControl;
        private int lastFrame;
        private string failure;
        public bool IsDirect => openingControl && closingControl && failure == null;
        public string Label => new AllocationSample(IsDirect ? AllocationBasis.DirectProfiler : AllocationBasis.Unavailable).Label;
        public string Reason => failure ?? (IsDirect ? "Both 32 KiB frame controls passed." : "Both frame controls must pass before a direct conclusion.");

        public void Observe(ProfilerFrameReading reading)
        {
            if (!reading.Available)
                Reject(reading.Reason ?? "No completed profiler frame sample.");
            if (reading.Frame <= 0 || lastFrame != 0 && reading.Frame != lastFrame + 1)
                Reject("Repeated, missing or reordered frame sample.");
            lastFrame = reading.Frame;
        }

        public void Control(ProfilerFrameReading reading, bool closing)
        {
            Observe(reading);
            if (!reading.Available || reading.Bytes < ControlBytes || reading.Allocations < 1)
                Reject("32 KiB positive control did not report at least 32768 bytes and one allocation.");
            if (closing)
            {
                if (!openingControl)
                    Reject("Closing control arrived before the opening control.");
                closingControl = true;
            }
            else
                openingControl = true;
        }

        private void Reject(string reason)
        {
            if (failure == null)
                failure = reason;
        }

        internal AllocationSample Sample(ProfilerFrameReading reading)
        {
            return IsDirect && reading.Available
                ? new AllocationSample(AllocationBasis.DirectProfiler, reading.Bytes)
                : new AllocationSample(AllocationBasis.Unavailable, reason: Reason);
        }

        public string Describe(ProfilerFrameReading reading) => Sample(reading).Describe();
        public string ByteColumn(ProfilerFrameReading reading)
            => IsDirect && reading.Available ? reading.Bytes.ToString(CultureInfo.InvariantCulture) : "";
        public string CountColumn(ProfilerFrameReading reading)
            => IsDirect && reading.Available ? reading.Allocations.ToString(CultureInfo.InvariantCulture) : "";

        public string Distribution(ProfilerFrameReading[] readings, int start, int count)
        {
            if (count <= 0)
                return "allocation=unavailable; reason=No frame samples; strict_zero=unproven";
            if (!IsDirect)
                return "allocation=unavailable; reason=" + Reason + "; strict_zero=unproven";
            long sum = 0;
            long maximum = 0;
            long allocations = 0;
            long maxAllocations = 0;
            int nonzero = 0;
            for (int i = start; i < start + count; i++)
            {
                ProfilerFrameReading reading = readings[i];
                if (!reading.Available)
                    return "allocation=unavailable; reason=Incomplete frame samples; strict_zero=unproven";
                sum += reading.Bytes;
                allocations += reading.Allocations;
                maximum = Math.Max(maximum, reading.Bytes);
                maxAllocations = Math.Max(maxAllocations, reading.Allocations);
                if (reading.Bytes != 0)
                    nonzero++;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "allocation={0}; ticks={1}; gc_bytes_sum={2}; gc_bytes_max={3}; nonzero_ticks={4}; allocation_count_sum={5}; allocation_count_max={6}; strict_zero={7}",
                Label, count, sum, maximum, nonzero, allocations, maxAllocations, nonzero == 0 ? "pass" : "fail");
        }
    }
}
