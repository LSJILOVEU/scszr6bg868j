using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace WarCore.Tests
{
    // Report-only baseline for the first engagement tick of the fixed 100 vs 100 scene. No timing gate: numbers are relative to one runner.
    public sealed class PeakBaselineTests
    {
        private const int Runs = 5, Window = 30;

        [Category("Manual"), Test, Explicit("Separate uninstrumented Release performance run; no timing gate.")]
        public void FirstFireTickPeak()
        {
            var warm = M7BehaviorLockTests.Real(endless: true); warm.Step(300);
            var probe = M7BehaviorLockTests.Real(); int first = 0;
            for (int tick = 1; tick <= 900 && first == 0; tick++) { probe.Step(); if (probe.Combat.Fires.Length > 0) first = tick; }
            Assert.That(first, Is.GreaterThan(1), "scene must reach a first shot");
            var firstMs = new double[Runs]; var windowMs = new double[Runs]; ulong hash = 0;
            for (int run = 0; run < Runs; run++)
            {
                var w = M7BehaviorLockTests.Real(); w.Step(first - 1);
                for (int i = 0; i < Window; i++)
                {
                    long start = Stopwatch.GetTimestamp(); w.Sim.Step(default);
                    double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                    if (i == 0) firstMs[run] = ms;
                    windowMs[run] = Math.Max(windowMs[run], ms);
                }
                if (run == 0) hash = w.Hash; else Assert.That(w.Hash, Is.EqualTo(hash));
            }
            TestContext.Out.WriteLine($"PEAK_BASELINE firstFireTick={first} runs={Runs} window={Window} hash={hash:X16}");
            TestContext.Out.WriteLine($"PEAK_BASELINE firstTick  max={firstMs.Max():F3} median={Median(firstMs):F3} all=[{string.Join(", ", firstMs.Select(v => v.ToString("F3")))}]");
            TestContext.Out.WriteLine($"PEAK_BASELINE window     max={windowMs.Max():F3} median={Median(windowMs):F3} all=[{string.Join(", ", windowMs.Select(v => v.ToString("F3")))}]");
        }

        private static double Median(double[] v) { var s = (double[])v.Clone(); Array.Sort(s); return s[s.Length / 2]; }
    }
}
