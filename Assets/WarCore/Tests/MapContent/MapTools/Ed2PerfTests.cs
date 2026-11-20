using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed2Fixtures;

namespace WarCore.Tests
{
    // ED-2 acceptance 9: numbers for the pull request, recorded and not asserted. Run by hand:
    // dotnet test -c Release --no-build --filter "FullyQualifiedName~Ed2PerfTests"
    public sealed class Ed2PerfTests
    {
        [Test, Explicit, Category("Manual")]
        public void Record()
        {
            MapDesc big = MapNew.Desc(new MapNewSpec
            {
                Id = "big", Content = "rwr", Pack = Pack, Width = 512, Depth = 512, CeilingRaw = 200 * S, HeightRaw = 0, Surface = "grass",
            });
            foreach (int r in new[] { 16, 32, 64 })
            {
                var clock = Stopwatch.StartNew();
                MapHeightWrite[] soft = MapBrush.Raise(big, 256, 256, r, (int)(2 * S));
                double softMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                MapHeightWrite[] steep = MapBrush.Raise(big, 256, 256, r, (int)(100 * S));
                double steepMs = clock.Elapsed.TotalMilliseconds;
                TestContext.Out.WriteLine("raise r=" + r + ": 2 m " + softMs.ToString("F1") + " ms (" + soft.Length + " points), 100 m "
                    + steepMs.ToString("F1") + " ms (" + steep.Length + " points, " + MapBrush.LastPasses + " passes)");
            }
            foreach (int top in new[] { 20, 50, 100 })
            {
                var c = Stopwatch.StartNew();
                string result;
                try
                {
                    MapHeightWrite[] ramp = MapBrush.Ramp(big, 10, 256, 0, 500, 256, (int)(top * S), 3, 3, 4);
                    result = ramp.Length + " points";
                }
                catch (ArgumentException e) { result = e.Message; }
                TestContext.Out.WriteLine("ramp across the map, 0 to " + top + " m: " + c.Elapsed.TotalMilliseconds.ToString("F1")
                    + " ms (" + result + ", " + MapBrush.LastPasses + " passes)");
            }
        }
    }
}
