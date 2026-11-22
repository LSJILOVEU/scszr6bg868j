using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv2Maps;

namespace WarCore.Tests
{
    // TRV-2 T2-26: MapGate.Links, the read-only link report. MapGate.Check is not touched.
    public sealed class Trv2GateTests
    {
        private static string[] Lines(MapDesc d, ContentPack pack = null) => MapGate.Links(Ml4Maps.Bytes(d), pack ?? Test).Select(l => l.ToString()).ToArray();

        // F-window by hand: the corner box at (1, 1) gives four climbs, four drops and four vaults over it (c = 1 m); the
        // window gives one vault each way (c = sill 58982, gap 131072 - 58982). Order: from node, then to node.
        [Test]
        public void T2_26_TheWindowReportMatchesTheHandTable()
        {
            Assert.That(Lines(Window()), Is.EqualTo(new[]
            {
                "vault L0 (1, 0) -> L0 (1, 2) rise 65536 fall 65536 gap none ticks 24",
                "climb L0 (1, 0) -> L1 (1, 1) rise 65536 fall 0 gap none ticks 30",
                "vault L0 (0, 1) -> L0 (2, 1) rise 65536 fall 65536 gap none ticks 24",
                "climb L0 (0, 1) -> L1 (1, 1) rise 65536 fall 0 gap none ticks 30",
                "vault L0 (2, 1) -> L0 (0, 1) rise 65536 fall 65536 gap none ticks 24",
                "climb L0 (2, 1) -> L1 (1, 1) rise 65536 fall 0 gap none ticks 30",
                "vault L0 (1, 2) -> L0 (1, 0) rise 65536 fall 65536 gap none ticks 24",
                "climb L0 (1, 2) -> L1 (1, 1) rise 65536 fall 0 gap none ticks 30",
                "vault L0 (8, 7) -> L0 (8, 9) rise 58982 fall 58982 gap 72090 ticks 24",
                "vault L0 (8, 9) -> L0 (8, 7) rise 58982 fall 58982 gap 72090 ticks 24",
                "drop L1 (1, 1) -> L0 (1, 0) rise 0 fall 65536 gap none ticks 12",
                "drop L1 (1, 1) -> L0 (0, 1) rise 0 fall 65536 gap none ticks 12",
                "drop L1 (1, 1) -> L0 (2, 1) rise 0 fall 65536 gap none ticks 12",
                "drop L1 (1, 1) -> L0 (1, 2) rise 0 fall 65536 gap none ticks 12",
            }));
        }

        [Test]
        public void T2_26_TheReportFollowsDeriveLineForLine()
        {
            foreach (MapDesc d in new[] { Box1(), LowWall(), BoxWall(), SlopeHouse() })
            {
                WcMap map = Bake(d);
                LayeredNav nav = Nav(map);
                TraversalLink[] links = LinkDerive.Derive(map, nav, Rules, null);
                var lines = MapGate.Links(Ml4Maps.Bytes(d), Test);
                Assert.That(lines.Count, Is.EqualTo(links.Length));
                for (int i = 0; i < links.Length; i++)
                {
                    TraversalLinkLine l = lines[i];
                    Assert.That(l.KindId, Is.EqualTo(KindOf(links[i])));
                    Assert.That(new[] { l.FromLayer, l.FromX, l.FromZ, l.ToLayer, l.ToX, l.ToZ }, Is.EqualTo(new[]
                    {
                        nav.LayerOf(links[i].From), nav.XOf(links[i].From), nav.ZOf(links[i].From), nav.LayerOf(links[i].To), nav.XOf(links[i].To), nav.ZOf(links[i].To),
                    }));
                    Assert.That(new[] { l.Rise, l.Fall, l.Gap, (long)l.Ticks }, Is.EqualTo(new[] { links[i].Rise, links[i].Fall, links[i].Gap, (long)links[i].Ticks }));
                }
            }
        }

        [Test]
        public void TheReportIsEmptyWhenTraversalIsOff()
        {
            MapDesc plainPack = Make(16, 16, null, new[] { Box(1, 8, 8, M) }, pack: "rwr-m6");
            Assert.That(MapGate.Links(Ml4Maps.Bytes(plainPack), Load("rwr-m6")), Is.Empty, "pack without the section");
            Assert.That(MapGate.Links(new byte[] { (byte)'{' }, Test), Is.Empty, "unreadable description");
            Assert.That(Lines(Make(16, 16, null, new MapPlate[0], new[] { Wall(1, 4, 8, 8, 1, M) })), Is.Empty, "no plates");
            // Ground 1 m, a plate at 0.5 m: buried, so it does not bake.
            MapDesc buried = Make(6, 4, Ml4Maps.Ground(6, 4, M), new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, M / 2) });
            Assert.That(MapGate.Check(Ml4Maps.Bytes(buried), Test), Is.Not.Empty);
            Assert.That(Lines(buried), Is.Empty, "does not bake");
        }

        [Test]
        public void TheReportChecksItsArguments()
        {
            Assert.Throws<ArgumentNullException>(() => MapGate.Links(null, Test));
            Assert.Throws<ArgumentNullException>(() => MapGate.Links(Ml4Maps.Bytes(Box1()), null));
            Assert.Throws<ArgumentException>(() => MapGate.Links(Ml4Maps.Bytes(Make(16, 16, null, new[] { Box(1, 8, 8, M) }, pack: "rwr-m6")), Test));
        }

        // Check reads the same with and without the section (D3).
        [Test]
        public void CheckIsTheSameWithTheSection()
        {
            foreach (Func<string, MapDesc> make in new Func<string, MapDesc>[]
            {
                p => Make(16, 16, null, new[] { Box(1, 8, 8, M) }, pack: p),
                p => Make(16, 16, null, new[] { Box(1, 1, 1, M) }, new[] { Wall(1, 4, 8, 8, 1, M) }, pack: p),
                p => Make(6, 4, Ml4Maps.Ground(6, 4, M), new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, M / 2) }, pack: p),
            })
            {
                var with = MapGate.Check(Ml4Maps.Bytes(make(Pack)), Test).Select(p => p.ToString()).ToArray();
                var without = MapGate.Check(Ml4Maps.Bytes(make("rwr-m6")), Load("rwr-m6")).Select(p => p.ToString()).ToArray();
                Assert.That(with, Is.EqualTo(without));
            }
        }

        // Deliverable E: the link lists of the fixtures, written where TRV2_OUT points. Run by hand only.
        [Test, Explicit, Category("Manual")]
        public void WriteFixtureLinkLists()
        {
            string dir = Environment.GetEnvironmentVariable("TRV2_OUT");
            Assume.That(string.IsNullOrEmpty(dir), Is.False, "set TRV2_OUT");
            foreach ((string name, MapDesc d) in new[] { ("F-box", Box1()), ("F-window", Window()), ("F-low-wall", LowWall()), ("F-box-wall", BoxWall()), ("F-slope-house", SlopeHouse()) })
                File.WriteAllLines(Path.Combine(dir, name + ".txt"), Lines(d));
        }
    }
}
