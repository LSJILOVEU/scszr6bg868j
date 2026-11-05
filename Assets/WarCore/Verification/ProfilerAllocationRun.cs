using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using WarCore.Sim;
using WarCore.Verification.Fixtures;
using WarCore.Verification.Fixtures.Diagnostics;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Verification
{
    public interface IProfilerFrameSource : IDisposable
    {
        string Description { get; }
        void BeginFrame(int frame);
        ProfilerFrameReading ReadCompletedFrame(int followingFrame);
    }

    // One callback consumes the previous completed frame, then schedules exactly one next tick.
    public sealed class ProfilerAllocationRun
    {
        public const int SettleFrames = 16;
        public const int WarmupTicks = 32;
        public const int LightTicks = 1200;
        public const int DenseTicks = 300;
        private const int Opening = SettleFrames;
        public const int BaselineFrames = 1200;
        private const int IdleStart = Opening + 1;
        private const int LightStart = IdleStart + WarmupTicks + BaselineFrames;
        private const int DenseStart = LightStart + WarmupTicks + LightTicks;
        private const int ProbeStart = DenseStart + WarmupTicks + DenseTicks;
        private const int Closing = ProbeStart + WarmupTicks + BaselineFrames;
        public const int TotalFrames = Closing + 1;
        private readonly IProfilerFrameSource meter;
        public const int BisectTotalFrames = SettleFrames + 2 + 3 * (WarmupTicks + BaselineFrames);
        private readonly ProfilerFrameReading[] readings;
        private readonly long[] timestamps;
        private readonly ProfilerBisectWorkload bisect;
        private readonly string[] profiles;
        private readonly int[] starts;
        private readonly int[] ticks;
        private readonly int probeStart;
        private readonly int closing;
        public int PlannedFrames => readings.Length;
        private readonly ProfilerFrameEvidence evidence = new ProfilerFrameEvidence();
        private readonly Simulation light;
        private readonly LoadSystem load;
        private readonly SemanticBenchScene dense;
        private volatile byte[] openingAllocation;
        private volatile byte[] closingAllocation;
        private readonly bool injectAlloc;
        private readonly byte[][] injected;
        private int injectedCount;
        private long probeCalls;
        private ProfilerBaselineDelta[] deltas;
        private string verdict;
        private int scheduled;
        private int completed;
        public bool Complete => completed == PlannedFrames;
        public int ExitCode { get; private set; }

        public ProfilerAllocationRun(IProfilerFrameSource source, bool injectAlloc = false, ProfilerBisectWorkload workload = null)
        {
            meter = source;
            bisect = workload;
            this.injectAlloc = injectAlloc;
            readings = new ProfilerFrameReading[bisect == null ? TotalFrames : BisectTotalFrames];
            timestamps = new long[readings.Length + 1];
            probeStart = bisect == null ? ProbeStart : DenseStart;
            closing = probeStart + WarmupTicks + BaselineFrames;
            injected = new byte[bisect == null ? WarmupTicks * 2 + LightTicks + DenseTicks : WarmupTicks + BaselineFrames][];
            if (bisect == null)
            {
                profiles = new[] { "Idle", "Light", "DenseRegular", "ProbeOnly" };
                starts = new[] { IdleStart, LightStart, DenseStart, probeStart };
                ticks = new[] { BaselineFrames, LightTicks, DenseTicks, BaselineFrames };
                light = new Simulation(123, config: new SimConfig(8, 64, 64, 2000));
                load = new LoadSystem(light, 2000);
                light.Register("load", load);
                dense = new SemanticBenchScene(true, false);
            }
            else
            {
                profiles = new[] { "Idle", "Bisect-" + bisect.Name, "ProbeOnly" };
                starts = new[] { IdleStart, LightStart, probeStart };
                ticks = new[] { BaselineFrames, BaselineFrames, BaselineFrames };
            }
        }

        public void AdvanceFrame(int frame)
        {
            if (Complete)
                throw new InvalidOperationException("Profiler allocation run is already complete.");
            timestamps[scheduled] = Stopwatch.GetTimestamp();
            if (scheduled > completed)
            {
                ProfilerFrameReading reading = meter.ReadCompletedFrame(frame);
                readings[completed] = reading;
                if (completed == Opening || completed == closing)
                    evidence.Control(reading, completed == closing);
                else if (completed > Opening)
                    evidence.Observe(reading);
                completed++;
            }
            if (Complete)
                return;
            meter.BeginFrame(frame);
            int index = scheduled++;
            if (index == Opening)
                openingAllocation = AllocateControl();
            else if (index == closing)
                closingAllocation = AllocateControl();
            else if (index >= IdleStart && index < closing)
            {
                // Identical non-Sim bookkeeping in Idle, ProbeOnly and both Sim profiles.
                NonSimProbe();
                bool simFrame = index >= LightStart && index < probeStart;
                if (simFrame && injectAlloc)
                    injected[injectedCount++] = AllocateInjection();
                if (simFrame && bisect != null)
                    bisect.Step();
                else if (index >= LightStart && index < DenseStart)
                    light.Step(default);
                else if (index >= DenseStart && index < probeStart)
                    dense.Sim.Step(default);
                // Idle and ProbeOnly deliberately do not access any Sim object.
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void NonSimProbe() { probeCalls++; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static byte[] AllocateInjection()
        {
            // 64-byte payload; actual managed allocation includes runtime object overhead.
            var result = new byte[64];
            result[0] = 17;
            result[63] = 29;
            return result;
        }

        private static byte[] AllocateControl()
        {
            var result = new byte[ProfilerFrameEvidence.ControlBytes];
            result[0] = 17;
            result[result.Length - 1] = 29;
            return result;
        }

        public void Write(string path, string metadata)
        {
            if (!Complete)
                throw new InvalidOperationException("Cannot report an incomplete profiler run.");
            VerifyWork();
            VerifyControl(openingAllocation);
            VerifyControl(closingAllocation);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path + ".meta.txt", metadata + meter.Description);
            using (var output = new StreamWriter(path))
            {
                output.WriteLine("ProfilerRecorder allocation report v2");
                output.Write(metadata);
                output.Write(meter.Description);
                output.WriteLine("scope=whole completed frame/all threads; one workload invocation per active frame; Idle and ProbeOnly have no Sim calls; baseline distribution comparison; absolute bytes retained");
                output.WriteLine("timing_verdict=not-applicable; Development Build / Editor allocation diagnostics only; timing cannot determine acceptance");
                output.WriteLine("frame_alignment=next callback; exactly one fresh sample from each counter; settle_frames={0}; planned_frames={1}", SettleFrames, PlannedFrames);
                output.WriteLine("frame_time|source=Stopwatch.GetTimestamp;frequency={0};columns=elapsed_ticks,frame_duration_ticks;whole-callback-interval=true;rate_is_diagnostic_only=true", Stopwatch.Frequency);
                output.WriteLine("injection|enabled={0};payload_bytes=64;actual_bytes=payload-plus-runtime-header;scope=Sim-warmup-and-steady-only;retained_arrays={1}", injectAlloc, injectedCount);
                output.WriteLine("baseline_rule=all-quantiles-v1; sort bytes and counts independently; equal-rank weighting (legacy Dense samples repeat 4 times); every Sim rank <= corresponding Idle AND ProbeOnly rank; no tolerance; no negative cancellation; warmup excluded");
                output.WriteLine("baseline_scope=Idle before Sim; ProbeOnly after Sim; same callback/read/begin/bookkeeping; same empty workload slot; no extra recorder reads; no Sim access in either baseline");
                ControlReport(output, "opening", readings[Opening]);
                ControlReport(output, "closing", readings[closing]);
                output.WriteLine("allocation={0}; reason={1}", evidence.Label, evidence.Reason);
                if (bisect != null) bisect.Describe(output);
                else
                {
                    output.WriteLine("Light|entities=2000;seed=123;spacing_m=8;radius_m=0.5;height_m=2;moving=true;queries_per_tick=5;scheduler_add_per_tick=4;events_per_tick=16;hash_outside_measurement=true");
                    output.WriteLine("DenseRegular|generator={0};seed=0x{1:X};soldiers=2000;static=64;large_units=16;live_bodies=2080;rays=660;spheres=165;sectors=165;peak=false;explosion_E=not-applicable", SemanticBenchScene.Generator, RealBenchWorkload.Seed);
                    output.WriteLine("DenseRegular|spacing_m=0.75;soldier_radius_m=0.25;height_m=3;ray_origin=(-4,1.5,(17+index%7)*0.75);aim=((25)*0.75,1.5,(18+index%5)*0.75);range_m=48");
                    output.WriteLine("DenseRegular|sphere_radius_m=2;sector_radius_m=3;sector_half_height_m=2;cosine_half_angle_permille=600;area_origin=seed-selected soldier centre;sector_direction=(1,0,1);self_exclusion=none");
                    output.WriteLine("DenseRegular|rays=sight-mask alternating Any/Nearest;areas=AllCapped(512);sphere_mask=units;sector_mask=soldiers;no deaths;diagnostics=false;full output signatures checked each tick");
                    output.WriteLine("DenseRegular|input=0x{0:X16};selected=0x{1:X16};oracle=0x{2:X16};sight_mask=0x{3:X8};unit_mask=0x{4:X8}",
                        dense.InputSignature, dense.OutputSignature, dense.OracleSignature, dense.Content.SightMask, dense.Content.UnitMask);
                }
                for (int i = 0; i < profiles.Length; i++) Distribution(output, profiles[i], starts[i], ticks[i]);
                if (deltas != null)
                    for (int i = 1; i < profiles.Length - 1; i++)
                    {
                        output.WriteLine("{0}.delta.Idle|{1}", profiles[i], deltas[(i - 1) * 2]);
                        output.WriteLine("{0}.delta.ProbeOnly|{1}", profiles[i], deltas[(i - 1) * 2 + 1]);
                    }
                output.WriteLine("conclusion|verdict={0};basis={1};injected={2};claim=baseline-differential-evidence-only", verdict, evidence.Label, injectAlloc);
                if (bisect != null) bisect.WriteFinal(output);
                else
                {
                    output.WriteLine("Light.final|ticks={0};applied={1};pending={2};dropped={3};hash=0x{4:X16}",
                        light.Clock.Tick, load.AppliedTasks, light.Scheduler.PendingCount, light.Events.EventsDroppedTotal, light.StateHash());
                    output.WriteLine("DenseRegular.final|ticks={0};calls={1};dropped={2};hash=0x{3:X16};checksum=0x{4:X16}",
                        dense.Sim.Clock.Tick, dense.Calls, dense.Sim.Events.EventsDroppedTotal, dense.Sim.StateHash(), dense.Checksum);
                }
                output.WriteLine("complete|frames={0};allocation={1};strict_zero={2};exit_code={3}", completed, evidence.Label,
                    !evidence.IsDirect ? "unproven" : ExitCode == 0 ? "pass" : "fail", ExitCode);
            }
            using (var csv = new StreamWriter(path + ".csv"))
            {
                csv.WriteLine("frame,profile,phase,tick,allocation,gc_bytes,allocation_count,elapsed_ticks,frame_duration_ticks");
                for (int i = 0; i < profiles.Length; i++) WriteCsv(csv, profiles[i], starts[i], ticks[i]);
            }
        }

        private void VerifyWork()
        {
            if (bisect != null) bisect.Verify(WarmupTicks + BaselineFrames);
            else
            {
                HashScenarios.Require(light.Clock.Tick == WarmupTicks + LightTicks && dense.Sim.Clock.Tick == WarmupTicks + DenseTicks, "Profiler tick coverage mismatch.");
                HashScenarios.Require(load.AppliedTasks == (WarmupTicks + LightTicks - 2L) * 4 && light.Scheduler.PendingCount == 8, "Light scheduler work incomplete.");
                HashScenarios.Require(light.Events.EventsDroppedTotal == 0 && dense.Sim.Events.EventsDroppedTotal == 0, "Profiler workload dropped events.");
                HashScenarios.Require(dense.Calls == (WarmupTicks + DenseTicks) * 990L, "Dense query work incomplete.");
                int live = 0;
                foreach (EntityId entity in light.Entities)
                    live++;
                HashScenarios.Require(live == 2000, "Light workload lost entities.");
            }
            HashScenarios.Require(probeCalls == closing - IdleStart, "Non-Sim probe coverage mismatch.");
            HashScenarios.Require(injectedCount == (injectAlloc ? injected.Length : 0), "Injection coverage mismatch.");
            for (int i = 0; i < injectedCount; i++)
                HashScenarios.Require(injected[i].Length == 64 && injected[i][0] == 17 && injected[i][63] == 29, "Injection array changed.");
            GC.KeepAlive(injected);
            ExitCode = evidence.IsDirect ? 0 : 2;
            verdict = "unavailable";
            if (!evidence.IsDirect) return;
            deltas = new ProfilerBaselineDelta[(profiles.Length - 2) * 2];
            for (int i = 1; i < profiles.Length - 1; i++)
            {
                deltas[(i - 1) * 2] = Compare(starts[i], ticks[i], IdleStart);
                deltas[(i - 1) * 2 + 1] = Compare(starts[i], ticks[i], probeStart);
            }
            foreach (var delta in deltas)
                if (!delta.Pass) ExitCode = 3;
            bool baselineNonzero = deltas[0].BaselineBytes != 0 || deltas[1].BaselineBytes != 0;
            verdict = ExitCode == 3 ? "sim-delta-nonzero" : baselineNonzero
                ? "sim-delta-zero (baseline nonzero)" : "absolute-zero";
            if (injectAlloc && ExitCode == 0)
            {
                // A deliberately allocated control must never produce a zero verdict.
                ExitCode = 1;
                verdict = "injection-not-detected";
            }
        }

        private ProfilerBaselineDelta Compare(int simStart, int ticks, int baselineStart)
            => new ProfilerBaselineDelta(readings, simStart + WarmupTicks, ticks,
                baselineStart + WarmupTicks, BaselineFrames);

        private static void VerifyControl(byte[] value)
        {
            HashScenarios.Require(value != null && value.Length == ProfilerFrameEvidence.ControlBytes
                && value[0] == 17 && value[value.Length - 1] == 29, "Escaping control array changed.");
            GC.KeepAlive(value);
        }

        private static void ControlReport(TextWriter output, string name, ProfilerFrameReading reading)
        {
            output.WriteLine("control.{0}|payload=32768;sample_available={1};raw_byte_counter={2};raw_count_counter={3};reason={4};frame={5}",
                name, reading.Available, reading.Available ? reading.Bytes.ToString(CultureInfo.InvariantCulture) : "unavailable",
                reading.Available ? reading.Allocations.ToString(CultureInfo.InvariantCulture) : "unavailable", reading.Reason, reading.Frame);
        }

        private void Distribution(TextWriter output, string profile, int start, int ticks)
        {
            output.WriteLine("{0}.warmup|{1}", profile, AbsoluteDistribution(start, WarmupTicks));
            output.WriteLine("{0}.steady|{1}", profile, AbsoluteDistribution(start + WarmupTicks, ticks));
        }

        private string AbsoluteDistribution(int start, int count)
            => evidence.Distribution(readings, start, count).Replace("strict_zero=", "absolute_zero=")
                .Replace("nonzero_ticks=", "nonzero_frames=");

        private void WriteCsv(TextWriter output, string profile, int start, int ticks)
        {
            for (int i = 0; i < WarmupTicks + ticks; i++)
            {
                ProfilerFrameReading reading = readings[start + i];
                output.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}", reading.Frame, profile, i < WarmupTicks ? "warmup" : "steady",
                    i + 1, evidence.Label, evidence.ByteColumn(reading), evidence.CountColumn(reading),
                    timestamps[start + i] - timestamps[0], timestamps[start + i + 1] - timestamps[start + i]);
            }
        }
    }
}
