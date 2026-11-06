using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // T1a-2a acceptance 13: the exact clearance query must not make crowd movement dramatically slower than the flat world
    // of the same size. 100 units, 600 ticks, the demo map's size (48 x 32). Hard ceiling: 3x the flat average tick.
    public sealed class Wp23PerformanceTests
    {
        private const long S = Wp23.S;

        private static (double msPerTick, long checksum) Run(bool terrain)
        {
            var boxes = new[] { (12, 4, 1, 14), (24, 14, 1, 14), (36, 2, 1, 16) };
            WcMap map = terrain
                ? Wp23.Make(48, 32, 0, 6 * S, boxes, 4 * S, (i, j) => (int)((i % 6) * (S / 8)), Wp23.Names, (x, z) => (byte)((x / 4 + z / 4) % 3))
                : Wp23.Make(48, 32, 0, 6 * S, boxes, 4 * S);
            var units = new UnitSpawn[100];
            var script = new BridgeCommand[100];
            for (int n = 0; n < 100; n++)
            {
                units[n] = Wp23.Unit((ulong)(n + 1), n + 1, 2500 + 1000 * (n % 10), 2500 + 1000 * (n / 10), 4000);
                script[n] = BridgeCommand.MoveTo(1 + n, n + 1, 40500 + 600 * (n % 10), 3500 + 2400 * (n / 10));
            }
            SimSession s = Wp23.Session(map, MoverKind.InterimCrowd, units, script, terrain ? Wp23.Pack() : null);
            var watch = Stopwatch.StartNew();
            for (int tick = 0; tick < 600; tick++) s.StepOnce();
            watch.Stop();
            long sum = 0;
            for (ulong k = 1; k <= 100; k++) sum += Wp23.At(s, k).X.Raw;
            return (watch.Elapsed.TotalMilliseconds / 600, sum);
        }

        [Test]
        public void CrowdOfAHundred_OnTerrain_StaysWithinThreeTimesTheFlatWorld()
        {
            Run(false); Run(true); // warm up the JIT
            double flat = double.MaxValue, terrain = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                flat = Math.Min(flat, Run(false).msPerTick);
                terrain = Math.Min(terrain, Run(true).msPerTick);
            }
            long initial = 0; for (int n = 0; n < 100; n++) initial += Wp23.Mm(2500 + 1000 * (n % 10)).Raw;
            Assert.That(Run(true).checksum, Is.GreaterThan(initial + 100 * 10 * S), "the crowd really walked east");
            Assert.That(Run(false).checksum, Is.GreaterThan(initial + 100 * 10 * S));
            Console.WriteLine($"WP23 PERF crowd 100 x 600 ticks, 48x32: flat {flat:F4} ms/tick, terrain {terrain:F4} ms/tick, ratio {terrain / flat:F2}");
            Assert.That(terrain, Is.LessThanOrEqualTo(3 * flat), "terrain world is more than 3x slower than the flat world");
        }
    }
}
