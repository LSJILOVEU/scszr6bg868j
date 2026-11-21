using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3a acceptance 2: the .gen.json format (read, canonical write, ranges, unknown keys, match with the map, mm to raw).
    public sealed class Ed3SpecTests
    {
        private const string Line = "[[0, 0], [10, 5], [20, 5]]";

        private static readonly string[] Each =
        {
            Hill(3, 4, 5, 6000),
            Hills(4, 0, 1, 30, 31, 2, 9, -500, 9000),
            Ridge(Line, 3, -2500),
            Mesa(10, 11, 4, 3000),
            Patch(5, 6, 7, "mud", 100),
            River(Line, 2, 1500, "mud"),
            Road(Line, 2, "road"),
        };

        [Test]
        public void EachKindRoundTripsToCanonicalBytes()
        {
            foreach (string stamp in Each)
            {
                string canonical = MapGenSpec.Read(Gen(40, 32, stamp, seed: 18446744073709551615UL)).Write();
                Assert.That(MapGenSpec.Read(canonical).Write(), Is.EqualTo(canonical), stamp);
                Assert.That(canonical, Does.EndWith("}\n"));
                Assert.That(canonical, Does.Contain("\"seed\": 18446744073709551615,"));
            }
            string all = MapGenSpec.Read(Gen(40, 32, Join(Each))).Write();
            Assert.That(MapGenSpec.Read(all).Write(), Is.EqualTo(all));
            Assert.That(MapGenSpec.Read(all).Stamps.Count, Is.EqualTo(7));
        }

        [Test]
        public void CanonicalTextIsFixed()
        {
            string text = MapGenSpec.Read("{\"stamps\":[" + Road("[[1,2],[3,4]]", 2) + "," + Hill(1, 2, 3, -4) + "],\"seed\":0,"
                + "\"ground\":\"grass\",\"baseMm\":-7,\"pack\":\"p\\\"q\",\"depth\":9,\"width\":8,\"version\":1}").Write();
            Assert.That(text, Is.EqualTo("{\n  \"version\": 1,\n  \"width\": 8,\n  \"depth\": 9,\n  \"pack\": \"p\\\"q\",\n  \"seed\": 0,\n"
                + "  \"baseMm\": -7,\n  \"ground\": \"grass\",\n  \"stamps\": [\n"
                + "    { \"kind\": \"road\", \"points\": [[1, 2], [3, 4]], \"halfWidth\": 2, \"surface\": \"road\" },\n"
                + "    { \"kind\": \"hill\", \"x\": 1, \"z\": 2, \"radius\": 3, \"heightMm\": -4 }\n  ]\n}\n"));
            string bare = MapGenSpec.Read(Gen(8, 9, "", baseMm: null, ground: null)).Write();
            Assert.That(bare, Is.EqualTo("{\n  \"version\": 1,\n  \"width\": 8,\n  \"depth\": 9,\n  \"pack\": \"rwr-terrain-demo\",\n"
                + "  \"seed\": 1,\n  \"stamps\": []\n}\n"));
            MapGenSpec spec = MapGenSpec.Read(bare);
            Assert.That(spec.BaseMm, Is.Null);
            Assert.That(spec.Ground, Is.Null);
        }

        private static void Accepts(string gen) => Assert.DoesNotThrow(() => MapGenSpec.Read(gen), gen);

        private static void Refuses(string gen, string part = null)
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => MapGenSpec.Read(gen), gen);
            Assert.That(e.Message, Does.StartWith("gen"));
            if (part != null) Assert.That(e.Message, Does.Contain(part));
        }

        private static string On(string stamp) => Gen(40, 30, stamp);

        [Test]
        public void RangesAcceptBothEndsAndRefuseOneBeyond()
        {
            Accepts(On(Hill(0, 0, 1, 0))); Accepts(On(Hill(0, 0, 256, 0)));
            Refuses(On(Hill(0, 0, 0, 0)), "radius"); Refuses(On(Hill(0, 0, 257, 0)), "radius");
            Accepts(On(Hill(-1024, -1024, 1, 0))); Accepts(On(Hill(1064, 1054, 1, 0)));
            Refuses(On(Hill(-1025, 0, 1, 0)), "x"); Refuses(On(Hill(1065, 0, 1, 0)), "x");
            Refuses(On(Hill(0, -1025, 1, 0)), "z"); Refuses(On(Hill(0, 1055, 1, 0)), "z");
            Accepts(On(Hill(0, 0, 1, 1000000))); Accepts(On(Hill(0, 0, 1, -1000000)));
            Refuses(On(Hill(0, 0, 1, 1000001)), "heightMm"); Refuses(On(Hill(0, 0, 1, -1000001)), "heightMm");
            Accepts(On(Hills(1, 0, 0, 0, 0, 1, 1, 0, 0))); Accepts(On(Hills(256, -1024, -1024, 1064, 1054, 1, 256, -1000000, 1000000)));
            Refuses(On(Hills(0, 0, 0, 0, 0, 1, 1, 0, 0)), "count"); Refuses(On(Hills(257, 0, 0, 0, 0, 1, 1, 0, 0)), "count");
            Refuses(On(Hills(1, 0, 0, 0, 0, 0, 1, 0, 0)), "rMin"); Refuses(On(Hills(1, 0, 0, 0, 0, 1, 257, 0, 0)), "rMax");
            Refuses(On(Hills(1, 0, 0, 1065, 0, 1, 1, 0, 0)), "x1"); Refuses(On(Hills(1, 0, -1025, 0, 0, 1, 1, 0, 0)), "z0");
            Refuses(On(Hills(1, 0, 0, 0, 0, 1, 1, -1000001, 0)), "hMinMm"); Refuses(On(Hills(1, 0, 0, 0, 0, 1, 1, 0, 1000001)), "hMaxMm");
            Accepts(On(Patch(0, 0, 1, "mud", 0))); Accepts(On(Patch(0, 0, 1, "mud", 512)));
            Refuses(On(Patch(0, 0, 1, "mud", -1)), "rough"); Refuses(On(Patch(0, 0, 1, "mud", 513)), "rough");
            Accepts(On(River("[[0, 0], [1, 1]]", 1, 1, "mud"))); Accepts(On(River("[[0, 0], [1, 1]]", 256, 1000000, "mud")));
            Refuses(On(River("[[0, 0], [1, 1]]", 1, 0, "mud")), "depthMm"); Refuses(On(River("[[0, 0], [1, 1]]", 1, 1000001, "mud")), "depthMm");
            Refuses(On(Road("[[0, 0], [1, 1]]", 0)), "halfWidth"); Refuses(On(Road("[[0, 0], [1, 1]]", 257)), "halfWidth");
            Refuses(On(Road("[[-1025, 0], [1, 1]]", 1)), "points"); Refuses(On(Road("[[0, 0], [1, 1055]]", 1)), "points");
            Accepts(On(Road("[[-1024, -1024], [1064, 1054]]", 1)));
        }

        private static string Polyline(int count)
        {
            var parts = new string[count];
            for (int n = 0; n < count; n++) parts[n] = "[" + n + ", 0]";
            return "[" + string.Join(", ", parts) + "]";
        }

        [Test]
        public void PolylinesTakeTwoToSixtyFourPoints()
        {
            Accepts(On(Ridge(Polyline(2), 1, 0))); Accepts(On(Ridge(Polyline(64), 1, 0)));
            Refuses(On(Ridge(Polyline(1), 1, 0)), "points"); Refuses(On(Ridge(Polyline(65), 1, 0)), "points");
            Refuses(On(Ridge("[[0, 0], [1]]", 1, 0)), "points"); Refuses(On(Ridge("[[0, 0], [1, 2, 3]]", 1, 0)), "points");
        }

        [Test]
        public void TopLevelFieldsAreChecked()
        {
            Refuses(Gen(40, 30, "").Replace("\"version\": 1", "\"version\": 2"), "version");
            Refuses(Gen(40, 30, "").Replace("\"version\": 1", "\"version\": 0"), "version");
            Refuses(Gen(0, 30, ""), "width"); Refuses(Gen(40, 513, ""), "depth");
            Accepts(Gen(1, 512, "")); Accepts(Gen(40, 30, "", seed: 0));
            Refuses(Gen(40, 30, "").Replace("\"seed\": 1", "\"seed\": -1"), "seed");
            Refuses(Gen(40, 30, "").Replace("\"seed\": 1", "\"seed\": 18446744073709551616"), "seed");
            Refuses(Gen(40, 30, "", baseMm: "1000001"), "baseMm"); Accepts(Gen(40, 30, "", baseMm: "-1000000"));
            Refuses(Gen(40, 30, "", baseMm: "1.5"), "baseMm");
            Refuses(Gen(40, 30, "", ground: ""), "ground");
            Refuses(Gen(40, 30, "", pack: ""), "pack");
            Refuses(Gen(40, 30, "").Replace("\"seed\": 1, ", ""), "seed");
            Refuses("[]");
            Refuses("{ \"version\": 1");
            var many = new string[257];
            for (int k = 0; k < many.Length; k++) many[k] = Hill(1, 1, 1, 1);
            Refuses(Gen(40, 30, Join(many)), "stamps");
            many = new string[256];
            for (int k = 0; k < many.Length; k++) many[k] = Hill(1, 1, 1, 1);
            Accepts(Gen(40, 30, Join(many)));
        }

        [Test]
        public void UnknownKeysAndKindsAndBackwardBoxesAreRefused()
        {
            Refuses(Gen(40, 30, "").Replace("\"stamps\"", "\"extra\": 1, \"stamps\""), "extra");
            Refuses(On(Hill(1, 1, 1, 1).Replace("\"x\"", "\"y\": 0, \"x\"")), "'y'");
            Refuses(On(Hill(1, 1, 1, 1).Replace("\"hill\"", "\"hil\"")), "hil");
            Refuses(On(Hill(1, 1, 1, 1).Replace("\"radius\": 1, ", "")), "radius");
            Refuses(On(Patch(1, 1, 1, "mud", 0).Replace("\"x\"", "\"halfWidth\": 2, \"x\"")), "halfWidth");
            Refuses(On(Hills(1, 5, 0, 4, 0, 1, 1, 0, 0)), "x0");
            Refuses(On(Hills(1, 0, 5, 0, 4, 1, 1, 0, 0)), "z0");
            Refuses(On(Hills(1, 0, 0, 0, 0, 3, 2, 0, 0)), "rMin");
            Refuses(On(Hills(1, 0, 0, 0, 0, 1, 1, 7, 6)), "hMinMm");
            Accepts(On(Hills(1, 5, 5, 5, 5, 3, 3, 7, 7)));
            Refuses(On(Patch(1, 1, 1, "", 0)), "surface");
            Refuses(On(Hill(1, 1, 1, 1).Replace("\"x\": 1", "\"x\": \"1\"")), "x");
        }

        [Test]
        public void SpecMustMatchTheMap()
        {
            MapDesc d = Map(40, 30);
            byte[] before = MapDescJson.Write(d);
            ArgumentException w = Assert.Throws<ArgumentException>(() => Compose(d, Gen(41, 30, "")));
            Assert.That(w.Message, Does.Contain("width"));
            ArgumentException z = Assert.Throws<ArgumentException>(() => Compose(d, Gen(40, 31, "")));
            Assert.That(z.Message, Does.Contain("depth"));
            ArgumentException p = Assert.Throws<ArgumentException>(() => Compose(d, Gen(40, 30, "", pack: "rwr-m6")));
            Assert.That(p.Message, Does.Contain("pack"));
            Assert.That(MapDescJson.Write(d), Is.EqualTo(before));
        }

        [Test]
        public void MillimetresBecomeRawTowardNegativeInfinity()
        {
            Assert.That(MapGenSpec.MmToRaw(1000), Is.EqualTo(65536));
            Assert.That(MapGenSpec.MmToRaw(1), Is.EqualTo(65));
            Assert.That(MapGenSpec.MmToRaw(-1), Is.EqualTo(-66));
            Assert.That(MapGenSpec.MmToRaw(-1000), Is.EqualTo(-65536));
            Assert.That(MapGenSpec.MmToRaw(1000000), Is.EqualTo(65536000));
            Assert.That(MapGenSpec.MmToRaw(-1000000), Is.EqualTo(-65536000));
            Assert.That(MapGenSpec.MmToRaw(0), Is.EqualTo(0));
        }
    }
}
