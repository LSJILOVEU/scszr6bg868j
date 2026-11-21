using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;
using static WarCore.Tests.Ed3cFixtures;

namespace WarCore.Tests
{
    // ED-3c-1 acceptance A1 (no base, no change in the text) and A4: the optional base key of .gen.json.
    public sealed class Ed3cSpecTests
    {
        private static MapGenSpec Accepts(string gen)
        {
            MapGenSpec spec = null;
            Assert.DoesNotThrow(() => spec = MapGenSpec.Read(gen), gen);
            return spec;
        }

        private static void Refuses(string gen, string part = null)
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => MapGenSpec.Read(gen), gen);
            Assert.That(e.Message, Does.StartWith("gen"));
            if (part != null) Assert.That(e.Message, Does.Contain(part));
        }

        private static string With(string key) => WithKey(Gen(8, 9, ""), key);

        [Test]
        public void WithoutBaseTheTextHasNoBaseKey()
        {
            string text = Accepts(Gen(8, 9, Hill(1, 2, 3, 4))).Write();
            Assert.That(text, Does.Not.Contain("\"base\""));
            Assert.That(Accepts(text).Base, Is.Null);
            Assert.That(new MapGenSpec(8, 9, "p", 1, 0, "grass").Base, Is.Null);
            Assert.That(new MapGenSpec(8, 9, "p", 1, 0, "grass").Write(), Does.Not.Contain("\"base\""));
        }

        [Test]
        public void BaseRoundTripsToItsCanonicalLine()
        {
            string text = Accepts("{\"stamps\":[],\"ground\":\"grass\",\"base\":{\"datumMm\":-7,\"exaggerationMilli\":1100,\"scaleMilli\":7000,"
                + "\"z0Mm\":15,\"x0Mm\":3,\"sha256\":\"" + Fp + "\",\"tile\":\"t.dem.json\"},\"baseMm\":-5,\"seed\":2,\"pack\":\"p\","
                + "\"depth\":9,\"width\":8,\"version\":1}").Write();
            Assert.That(text, Is.EqualTo("{\n  \"version\": 1,\n  \"width\": 8,\n  \"depth\": 9,\n  \"pack\": \"p\",\n  \"seed\": 2,\n"
                + "  \"baseMm\": -5,\n"
                + "  \"base\": { \"tile\": \"t.dem.json\", \"sha256\": \"" + Fp + "\", \"x0Mm\": 3, \"z0Mm\": 15, \"scaleMilli\": 7000, "
                + "\"exaggerationMilli\": 1100, \"datumMm\": -7 },\n"
                + "  \"ground\": \"grass\",\n  \"stamps\": []\n}\n"));
            Assert.That(Accepts(text).Write(), Is.EqualTo(text));
            MapGenBase b = Accepts(text).Base;
            Assert.That(b.Tile, Is.EqualTo("t.dem.json"));
            Assert.That(b.Sha256, Is.EqualTo(Fp));
            Assert.That(b.X0Mm, Is.EqualTo(3));
            Assert.That(b.Z0Mm, Is.EqualTo(15));
            Assert.That(b.ScaleMilli, Is.EqualTo(7000));
            Assert.That(b.ExaggerationMilli, Is.EqualTo(1100));
            Assert.That(b.DatumMm, Is.EqualTo(-7));
            string noGround = Accepts(WithKey(Gen(8, 9, "", ground: null), BaseKey())).Write();
            Assert.That(noGround, Does.Contain("  \"baseMm\": 0,\n  \"base\": { \"tile\": \"t.dem.json\", \"sha256\": \"" + Fp
                + "\", \"x0Mm\": 0, \"z0Mm\": 0, \"scaleMilli\": 10000, \"exaggerationMilli\": 1000, \"datumMm\": 0 },\n  \"stamps\": []\n"));
            Assert.That(Accepts(WithKey(Gen(8, 9, ""), BaseKey(tile: "\"a\\\"q\""))).Write(), Does.Contain("\"tile\": \"a\\\"q\""));
        }

        [Test]
        public void BaseRangesAcceptBothEndsAndRefuseOneBeyond()
        {
            Accepts(With(BaseKey(x0: 0))); Accepts(With(BaseKey(x0: 51200000)));
            Refuses(With(BaseKey(x0: -1)), "x0Mm"); Refuses(With(BaseKey(x0: 51200001)), "x0Mm");
            Accepts(With(BaseKey(z0: 0))); Accepts(With(BaseKey(z0: 51200000)));
            Refuses(With(BaseKey(z0: -1)), "z0Mm"); Refuses(With(BaseKey(z0: 51200001)), "z0Mm");
            Accepts(With(BaseKey(scale: 100))); Accepts(With(BaseKey(scale: 100000)));
            Refuses(With(BaseKey(scale: 99)), "scaleMilli"); Refuses(With(BaseKey(scale: 100001)), "scaleMilli");
            Accepts(With(BaseKey(exag: 1))); Accepts(With(BaseKey(exag: 100000)));
            Refuses(With(BaseKey(exag: 0)), "exaggerationMilli"); Refuses(With(BaseKey(exag: 100001)), "exaggerationMilli");
            Accepts(With(BaseKey(datum: 9000000))); Accepts(With(BaseKey(datum: -9000000)));
            Refuses(With(BaseKey(datum: 9000001)), "datumMm"); Refuses(With(BaseKey(datum: -9000001)), "datumMm");
            Refuses(With(BaseKey().Replace("\"x0Mm\": 0", "\"x0Mm\": 0.5")), "x0Mm");
        }

        [Test]
        public void TileAndFingerprintTextsAreChecked()
        {
            Accepts(With(BaseKey(tile: "\"t..dem.json\""))); Accepts(With(BaseKey(tile: "\"" + new string('t', 255) + "\"")));
            Refuses(With(BaseKey(tile: "\"\"")), "tile"); Refuses(With(BaseKey(tile: "\"" + new string('t', 256) + "\"")), "tile");
            Refuses(With(BaseKey(tile: "\"a/b.dem.json\"")), "tile"); Refuses(With(BaseKey(tile: "\"a\\\\b.dem.json\"")), "tile");
            Refuses(With(BaseKey(tile: "\".\"")), "tile"); Refuses(With(BaseKey(tile: "\"..\"")), "tile");
            Refuses(With(BaseKey(tile: "7")), "tile");
            Accepts(With(BaseKey(sha: new string('0', 64)))); Accepts(With(BaseKey(sha: "0123456789abcdef" + new string('f', 48))));
            Refuses(With(BaseKey(sha: Fp.Substring(1))), "sha256"); Refuses(With(BaseKey(sha: Fp + "0")), "sha256");
            Refuses(With(BaseKey(sha: Fp.ToUpperInvariant())), "sha256");
            Refuses(With(BaseKey(sha: "A" + Fp.Substring(1))), "sha256");
            Refuses(With(BaseKey(sha: "g" + Fp.Substring(1))), "sha256");
            Refuses(With(BaseKey(sha: "/" + Fp.Substring(1))), "sha256");
            Refuses(With(BaseKey(sha: ":" + Fp.Substring(1))), "sha256");
            Refuses(With(BaseKey(sha: "`" + Fp.Substring(1))), "sha256");
            Refuses(With(BaseKey().Replace("\"sha256\": \"" + Fp + "\"", "\"sha256\": 7")), "sha256");
            Refuses(With(BaseKey().Replace("\"sha256\": \"" + Fp + "\"", "\"sha256\": " + new string('1', 64))), "sha256");
        }

        [Test]
        public void BaseKeysAreExactAndNeedBaseMm()
        {
            Refuses(With(BaseKey().Replace("\"tile\": \"t.dem.json\", ", "")), "tile");
            Refuses(With(BaseKey().Replace(", \"datumMm\": 0", "")), "datumMm");
            Refuses(With(BaseKey().Replace(", \"scaleMilli\": 10000", "")), "scaleMilli");
            Refuses(With(BaseKey().Replace("\"datumMm\": 0", "\"datumMm\": 0, \"rotation\": 0")), "rotation");
            Refuses(With(BaseKey().Replace("\"datumMm\": 0", "\"datumMm\": 0, \"datumMm\": 0")), "datumMm");
            Refuses(WithKey(Gen(8, 9, ""), BaseKey() + ", " + BaseKey()), "base");
            Refuses(With("\"base\": 7"), "base");
            Refuses(With("\"base\": []"), "base");
            Refuses(WithKey(Gen(8, 9, "", baseMm: null), BaseKey()), "baseMm");
            Refuses(WithKey(Gen(8, 9, "", baseMm: null, ground: null), BaseKey()), "baseMm");
        }
    }
}
