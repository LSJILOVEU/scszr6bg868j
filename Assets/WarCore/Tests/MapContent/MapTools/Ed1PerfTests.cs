using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ED-1 acceptance 10: numbers for the pull request, recorded and not asserted. Run by hand:
    // dotnet test -c Release --no-build --filter "FullyQualifiedName~Ed1PerfTests"
    public sealed class Ed1PerfTests
    {
        private static MapDesc Big() => MapNew.Desc(new MapNewSpec
        {
            Id = "big", Content = "rwr", Pack = "rwr-terrain-demo", Width = 512, Depth = 512, CeilingRaw = 262144, HeightRaw = 0, Surface = "grass",
        });

        [Test, Explicit, Category("Manual")]
        public void Record()
        {
            MapDesc big = Big();
            var batch = new MapEdit(big);
            var clock = Stopwatch.StartNew();
            batch.Batch(e =>
            {
                for (int j = 200; j < 217; j++)
                    for (int i = 200; i < 217; i++) e.SetHeight(i, j, 1000);
            });
            double batchMs = clock.Elapsed.TotalMilliseconds;

            var one = new MapEdit(big);
            clock.Restart();
            for (int j = 200; j < 217; j++)
                for (int i = 200; i < 217; i++) one.SetHeight(i, j, 1000);
            double oneMs = clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            byte[] bytes = MapDocument.Open(MapDescJson.Write(big)).Save();
            double openSaveMs = clock.Elapsed.TotalMilliseconds;

            MapDocument doc = MapDocument.Open(bytes);
            long before = GC.GetTotalMemory(true);
            var session = new MapEditSession(doc);
            for (int k = 0; k < 50; k++)
            {
                int at = k;
                session.Do(d => d.WithHeight(at, 0, 1000));
            }
            long after = GC.GetTotalMemory(true);
            GC.KeepAlive(session);

            TestContext.WriteLine("ED-1 17x17 batch ms: " + batchMs.ToString("F1"));
            TestContext.WriteLine("ED-1 17x17 one by one ms: " + oneMs.ToString("F1"));
            TestContext.WriteLine("ED-1 512x512 open+save ms: " + openSaveMs.ToString("F1"));
            TestContext.WriteLine("ED-1 session 50 versions bytes: " + (after - before));
        }
    }
}
