using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using WarCore.Sim;

namespace WarCore.Tests
{
    public static partial class StableIdentityBench
    {
        public static void CheckKey(ulong expected, ulong actual)
        {
            if (expected != actual)
                throw new InvalidOperationException("Identity benchmark output mismatch: " + expected + " != " + actual);
        }
        public static void Run(string mode, TextWriter output)
        {
            if (mode != "smoke" && mode != "full")
                throw new ArgumentException("Identity benchmark requires smoke or full.");
            int samples = mode == "full" ? 300 : 30;
            var meter = new AllocationMeter();
            // Stopwatch initializes runtime timer metadata on its first call; keep that outside product windows.
            Stopwatch.GetTimestamp();
            meter.Difference(meter.Read());
            output.WriteLine("identity-benchmark|entities=10000; samples="+samples+"; runtime="+RuntimeInformation.FrameworkDescription
                +"; architecture="+RuntimeInformation.ProcessArchitecture+"; os="+RuntimeInformation.OSDescription+"; processorCount="+Environment.ProcessorCount);
#if STABLE_BASELINE
            output.WriteLine("protocol=pre-migration-HEAD; comparison=query-only; execution=see-host-metadata");
#else
            output.WriteLine("protocol=stable-v3/snapshot-v5; execution=see-host-metadata; budget=IL2CPP-arm64-only; validation=does-not-imply-budget-acceptance");
#endif
            output.WriteLine("workload|seed=913; creation=(i*7919+913)%10000; chunks=logical%7; static=even logical; large=logical%997==0,radius16; otherRadius=1; height=2*radius");
            output.WriteLine("queries|origin=(-2,0,0); sphere/cylinder/sector radius=12; halfHeight=4; sectorCos=500; direction=(1,0,0); capsuleEnd=(22,0,0),radius=2; rayRange=24; layerMask=1; excluded=layer2(logical%13==0); no-self-exclusion; cell=4,maxCellsPerBody=4");
            output.WriteLine(meter.DescribeProbe());
            CheckKey(1,1);
            bool detected = false;
            try { CheckKey(1,2); }
            catch (InvalidOperationException) { detected = true; }
            if (!detected)
                throw new InvalidOperationException("Identity benchmark difference control did not fail.");
            output.WriteLine("positive-comparison-control=detected");
#if !STABLE_BASELINE
            IdentityGroups(samples,meter,output);
#endif
#if !STABLE_BASELINE
            PairedQueries(samples,meter,output,true);
#else
            for (int density = 0; density < 2; density++)
            {
                var scene = new StableIdentityBenchScene(density != 0);
                output.WriteLine("scene|density="+density+"; entities=10000; actualLarge="+scene.Sim.SpatialIndex.LargeBodyCount
                    +"; actualCells="+scene.Sim.SpatialIndex.OccupiedCells+"; memberships="+scene.Sim.SpatialIndex.MembershipCount);
                for (int shape = 0; shape < scene.Requests.Length; shape++)
                {
                    SpatialRequest request = scene.Requests[shape];
                    for (int resultMode = 0; resultMode < 4; resultMode++)
                    {
                        SpatialHit[] expected = scene.Expected(request,resultMode == 3);
                        scene.Check(request,resultMode,expected);
                        for (int warm = 0; warm < 32; warm++)
                            scene.Execute(request,resultMode);
                        int operations = resultMode == 0 ? 512 : 1;
                        var elapsed = new long[samples];
                        var allocations = new AllocationSample[samples];
                        for (int i = 0; i < samples; i++)
                        {
                            AllocationSample before = meter.Read();
                            long start = Stopwatch.GetTimestamp();
                            for (int repeat = 0; repeat < operations; repeat++)
                                scene.Execute(request,resultMode);
                            elapsed[i] = Stopwatch.GetTimestamp()-start;
                            allocations[i] = meter.Difference(before);
                            // Recheck the public operation outside the timed/allocation window on every sample.
                            scene.Check(request,resultMode,expected);
                        }
                        Report(output,"query."+(density == 0 ? "scatter" : "dense")+"."+request.Kind+"."+ModeName(resultMode),elapsed,allocations,true,operations);
                        output.WriteLine("query-hits|shape="+request.Kind+"; density="+density+"; mode="+ModeName(resultMode)+"; hits="+expected.Length);
                    }
                }
#if !STABLE_BASELINE
                if (density == 0)
                    StateGroups(scene,samples,meter,output);
#endif
            }
#endif
            output.WriteLine("identity-benchmark=complete");
        }
        private static string ModeName(int mode) => mode == 0 ? "Any" : mode == 1 ? "Nearest" : mode == 2 ? "AllCapped512" : "Full";
        private static void Report(TextWriter output, string group, long[] samples, AllocationSample[] allocations, bool strict, int operations = 1)
        {
            if (strict)
                foreach (AllocationSample sample in allocations)
                    if (sample.IsDirect && sample.Bytes != 0)
                        throw new InvalidOperationException(group+": direct allocation must be zero, got "+sample.Bytes);
            Array.Sort(samples);
            double scale = 1000.0 / Stopwatch.Frequency / operations;
            double sum = 0;
            foreach (long sample in samples)
                sum += sample;
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "metric|{0}; n={1}; ops_per_sample={8}; mean_ms={2:F9}; median_ms={3:F9}; p95_ms={4:F9}; p99_ms={5:F9}; max_ms={6:F9}; {7}",
                group,samples.Length,sum/samples.Length*scale,samples[(samples.Length-1)/2]*scale,
                samples[(int)Math.Ceiling(samples.Length*0.95)-1]*scale,samples[(int)Math.Ceiling(samples.Length*0.99)-1]*scale,
                samples[samples.Length-1]*scale,AllocationSample.Summary(allocations),operations));
        }
    }
}
