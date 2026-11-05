using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using WarCore.Tests.Diagnostics;

namespace WarCore.Tests
{
    public static class Round4ChurnBench
    {
        public static void Run(string mode, TextWriter output, bool placed = false)
        {
            if (mode != "smoke" && mode != "full")
                throw new ArgumentException("Churn benchmark requires smoke or full.");
            int samples = mode == "full" ? 300 : 30;
            output.WriteLine("MF4-churn|protocol="+(placed ? "retained-v5/snapshot-v7" : "retained-v4/snapshot-v6")+"; runtime="+RuntimeInformation.FrameworkDescription
                +"; architecture="+RuntimeInformation.ProcessArchitecture+"; samples="+samples+"; warmup=40; budget=IL2CPP-Release-arm64-p99<=5ms; managed=diagnostic-only");
            output.WriteLine("workload|seed=0xB30001; soldiers=2000; walls=40; covers=24; large-units=16; alive=2080; every-tick-deaths=104; every-tick-creates=104; all-entity-churn=5%; shared-stage-consumers=4; entity_capacity=2184; identity_capacity=8736; retention=32; timer_capacity=104");
            output.WriteLine("queries|AI-rays=660; spheres=165; sectors=165; rifle=40; shotgun=4x8; volley=100; melee=60; explosion=1,radius8; cover-rays=measured-E; sightMask=14; unitMask=9; soldierMask=1; no-faction/source/self-exclusions; Any/Nearest-sight; nearest-rifle/shotgun; volley-AllCapped4; areas-AllCapped512");
            output.WriteLine("geometry|scatter-spacing=3m,dense-spacing=0.75m; soldier-radius=0.25m,height=3m; scatter-rays-range12; dense-range48,origin=(-4,1.5,(17+index%7)*0.75),aim=((25)*0.75,1.5,(18+index%5)*0.75); scatter-start=selected-soldier or offsets(-4,0,-2),(-4,0,0),(0,8,0); spheres-radius=scatter6/dense2; sector-radius=scatter6/dense3; sector-direction=(1,0,1),cos600,halfHeight2; melee-radius3; area-origin=seed-selected-soldier; explosion-origin=(49*spacing/2,1.5,39*spacing/2)");
            for (int layout = 0; layout < 2; layout++)
            {
                var scene = new Round4ChurnScene(layout != 0,placed:placed);
                for (int i = 0; i < 40; i++)
                    scene.Sim.Step(default);
                var meter = new AllocationMeter();
                output.WriteLine(meter.DescribeProbe());
                var elapsed = new long[samples];
                var allocations = new AllocationSample[samples];
                Stopwatch.GetTimestamp();
                meter.Difference(meter.Read());
                long created = scene.Created;
                int identities = scene.Sim.Entities.RegisteredIdentityCount;
                for (int i = 0; i < samples; i++)
                {
                    AllocationSample before = meter.Read();
                    long start = Stopwatch.GetTimestamp();
                    scene.Sim.Step(default);
                    elapsed[i] = Stopwatch.GetTimestamp()-start;
                    allocations[i] = meter.Difference(before);
                    if (allocations[i].IsDirect)
                        Round4LongRun.Require(allocations[i].Bytes == 0,"Churn direct allocation must be zero.");
                    Round4LongRun.Require(scene.Sim.Entities.RegisteredIdentityCount == identities && scene.Sim.Events.EventsDroppedTotal == 0,
                        "Churn state or event count grew unexpectedly.");
                    // Independent predicates and full sorting run outside both measurement windows.
                    scene.VerifyOracle(i*37);
                }
                Round4LongRun.Require(scene.Created-created == samples*(long)Round4ChurnScene.ChurnPerTick,"Churn work was skipped.");
                Array.Sort(elapsed);
                double sum = 0;
                foreach (long value in elapsed)
                    sum += value;
                double scale = 1000.0/Stopwatch.Frequency;
                output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "churn-metric|layout={0}; n={1}; mean_ms={2:F6}; median_ms={3:F6}; p95_ms={4:F6}; p99_ms={5:F6}; max_ms={6:F6}; E={7}; rays={8}; queries={9}; identities={10}; created={11}; {12}",
                    layout == 0 ? "scatter" : "dense",samples,sum/samples*scale,elapsed[(samples-1)/2]*scale,
                    elapsed[(int)Math.Ceiling(samples*0.95)-1]*scale,elapsed[(int)Math.Ceiling(samples*0.99)-1]*scale,elapsed[samples-1]*scale,
                    scene.LastAffected,scene.LastRays,scene.LastQueries,identities,scene.Created-created,AllocationSample.Summary(allocations)));
            }
            ReferenceChurnBench.Run(samples,output,placed);
            output.WriteLine("MF4-churn=complete; full-required-for-G24; IL2CPP-results-must-come-from-external-run");
        }
    }
}
