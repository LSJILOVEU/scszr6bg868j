using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // ML-4 acceptance 5: every plate and cutout error of the description, with its code, entry, id and line, and the edges.
    public sealed class Ml4DescErrorTests
    {
        private const long M = Ml4Maps.M;

        private static string Error(string text) => Ml4DescTests.Error(text);
        private static int Line(string text, string fragment, int skip = 0) => Ml4DescTests.Line(text, fragment, skip);
        private static MapDesc Read(string text) => Ml4DescTests.Read(text);

        // 6 x 4 flat, ceiling 10 m: flat plate 1 on [1, 4) x [1, 3) at 3 m, a cutout on its cell (1, 1).
        private static string FlatText() => Ml4Maps.Text(Ml4Maps.Make(0, 6, 4, null, new[] { Ml4Maps.Flat(1, 1, 1, 3, 2, 3 * M) },
            new[] { Ml4Maps.Cut(1, 1, 1, 1, 1, 1) }));

        // The same with plate 1 sloped along x from 1 m to 2 m.
        private static string SlopeText() => Ml4Maps.Text(Ml4Maps.Make(0, 6, 4, null, new[] { Ml4Maps.Slope(1, 1, 1, 3, 2, "x", M, 2 * M) }));

        private static int PlateLine(string text) => Line(text, "\"plates\": [") + 1;
        private static int CutoutLine(string text) => Line(text, "\"cutouts\": [") + 1;

        private static string P(string text, string fragment) => "MAPDESC_PLATE plate 1 line " + Line(text, fragment);

        [Test]
        public void KeyErrorsArePass1KeyErrors()
        {
            string t = FlatText();
            string extra = t.Replace("\"thickRaw\": 32768\n", "\"thickRaw\": 32768,\n      \"other\": 1\n");
            Assert.That(Error(extra), Is.EqualTo("MAPDESC_KEY plate 1 line " + Line(extra, "\"other\"")));
            string s = SlopeText();
            string slopeExtra = s.Replace("\"toRaw\": 131072\n", "\"toRaw\": 131072,\n        \"other\": 1\n");
            Assert.That(Error(slopeExtra), Is.EqualTo("MAPDESC_KEY plate 1 line " + Line(slopeExtra, "\"other\"")));
            foreach (string key in new[] { "\"cell\": [1, 1],\n", "\"size\": [3, 2],\n" })
            {
                string missing = t.Replace("      " + key, "");
                Assert.That(Error(missing), Is.EqualTo("MAPDESC_KEY plate 1 line " + PlateLine(missing)), key);
            }
            string noThick = t.Replace(",\n      \"thickRaw\": 32768\n", "\n");
            Assert.That(Error(noThick), Is.EqualTo("MAPDESC_KEY plate 1 line " + PlateLine(noThick)));
            string noId = t.Replace("\"plates\": [\n    {\n      \"id\": 1,\n", "\"plates\": [\n    {\n");
            Assert.That(Error(noId), Is.EqualTo("MAPDESC_KEY plate line " + PlateLine(noId)));
            foreach (string key in new[] { "\"axis\": \"x\",\n", "\"fromRaw\": 65536,\n" })
            {
                string missing = s.Replace("        " + key, "");
                Assert.That(Error(missing), Is.EqualTo("MAPDESC_KEY plate 1 line " + Line(missing, "\"slope\"")), key);
            }
            string noTo = s.Replace(",\n        \"toRaw\": 131072\n", "\n");
            Assert.That(Error(noTo), Is.EqualTo("MAPDESC_KEY plate 1 line " + Line(noTo, "\"slope\"")));
            string type = t.Replace("\"yRaw\": 196608", "\"yRaw\": \"196608\"");
            Assert.That(Error(type), Is.EqualTo("MAPDESC_KEY plate 1 line " + Line(type, "\"yRaw\"")));
            string pair = t.Replace("\"size\": [3, 2]", "\"size\": [3]");
            Assert.That(Error(pair), Is.EqualTo("MAPDESC_KEY plate 1 line " + Line(pair, "\"size\": [3]")));
            string notObject = t.Replace("\"plates\": [\n    {", "\"plates\": [\n    7, {");
            Assert.That(Error(notObject), Is.EqualTo("MAPDESC_KEY plate line " + Line(notObject, "7, {")));
        }

        [Test]
        public void CutoutKeyErrors()
        {
            string t = FlatText();
            int at = t.IndexOf("\"cutouts\"");
            string noCell = t.Substring(0, at) + t.Substring(at).Replace("      \"cell\": [1, 1],\n", "");
            Assert.That(Error(noCell), Is.EqualTo("MAPDESC_KEY cutout 1 line " + CutoutLine(noCell)));
            string noSize = t.Substring(0, at) + t.Substring(at).Replace("\"cell\": [1, 1],\n      \"size\": [1, 1]\n", "\"cell\": [1, 1]\n");
            Assert.That(Error(noSize), Is.EqualTo("MAPDESC_KEY cutout 1 line " + CutoutLine(noSize)));
            string noPlate = t.Replace("\"plate\": 1,\n", "");
            Assert.That(Error(noPlate), Is.EqualTo("MAPDESC_KEY cutout 1 line " + CutoutLine(noPlate)));
            string extra = t.Replace("\"plate\": 1,\n", "\"plate\": 1,\n      \"other\": 2,\n");
            Assert.That(Error(extra), Is.EqualTo("MAPDESC_KEY cutout 1 line " + Line(extra, "\"other\"")));
            string zero = t.Replace("\"plate\": 1,\n", "\"plate\": 0,\n");
            Assert.That(Error(zero), Is.EqualTo("MAPDESC_PLATE cutout 1 line " + Line(zero, "\"plate\": 0")));
        }

        [Test]
        public void TopIsYRawOrSlopeExactlyOnce()
        {
            string t = FlatText();
            string neither = t.Replace("      \"yRaw\": 196608,\n", "");
            Assert.That(Error(neither), Is.EqualTo(P(neither, "{\n      \"id\": 1,\n      \"cell\": [1, 1]")));
            string both = SlopeText().Replace("\"slope\": {", "\"yRaw\": 65536,\n      \"slope\": {");
            Assert.That(Error(both), Is.EqualTo(P(both, "{\n      \"id\": 1,\n      \"cell\": [1, 1]")));
            string axis = SlopeText().Replace("\"axis\": \"x\"", "\"axis\": \"y\"");
            Assert.That(Error(axis), Is.EqualTo(P(axis, "\"axis\"")));
            Assert.That(Read(SlopeText().Replace("\"axis\": \"x\"", "\"axis\": \"z\"")).Plates[0].Slope.Axis, Is.EqualTo("z"));
        }

        [Test]
        public void SingleValueRangesArePass1()
        {
            string t = FlatText();
            string cell = t.Replace("\"cell\": [1, 1],\n      \"size\": [3, 2]", "\"cell\": [-1, 1],\n      \"size\": [3, 2]");
            Assert.That(Error(cell), Is.EqualTo(P(cell, "\"cell\": [-1")));
            string size = t.Replace("\"size\": [3, 2]", "\"size\": [3, 0]");
            Assert.That(Error(size), Is.EqualTo(P(size, "\"size\": [3, 0]")));
            long limit = MapDescReader.HeightLimit;
            Assert.That(Error(t.Replace("\"yRaw\": 196608", "\"yRaw\": " + (limit + 1))), Is.EqualTo(P(t, "\"yRaw\"")));
            Assert.That(Error(t.Replace("\"yRaw\": 196608", "\"yRaw\": " + (-limit - 1))), Is.EqualTo(P(t, "\"yRaw\"")));
            string s = SlopeText();
            Assert.That(Error(s.Replace("\"fromRaw\": 65536", "\"fromRaw\": " + (limit + 1))), Is.EqualTo(P(s, "\"fromRaw\"")));
            Assert.That(Error(s.Replace("\"toRaw\": 131072", "\"toRaw\": " + (-limit - 1))), Is.EqualTo(P(s, "\"toRaw\"")));
            Assert.That(Error(t.Replace("\"thickRaw\": 32768", "\"thickRaw\": 32767")), Is.EqualTo(P(t, "\"thickRaw\"")));
            Assert.That(Read(t).Plates[0].ThickRaw, Is.EqualTo(32768));
            Assert.That(Error(t.Replace("\"thickRaw\": 32768", "\"thickRaw\": " + (limit + 1))), Is.EqualTo(P(t, "\"thickRaw\"")));
            Assert.That(Read(t.Replace("\"thickRaw\": 32768", "\"thickRaw\": " + limit)).Plates[0].ThickRaw, Is.EqualTo(limit));
        }

        [Test]
        public void IdsAreIdErrors()
        {
            string t = FlatText();
            string zero = t.Replace("\"plates\": [\n    {\n      \"id\": 1,", "\"plates\": [\n    {\n      \"id\": 0,");
            Assert.That(Error(zero), Is.EqualTo("MAPDESC_ID plate line " + Line(zero, "\"id\": 0")));
            string huge = t.Replace("\"plates\": [\n    {\n      \"id\": 1,", "\"plates\": [\n    {\n      \"id\": 18446744073709551616,");
            Assert.That(Error(huge), Is.EqualTo("MAPDESC_ID plate line " + Line(huge, "\"id\": 18446744073709551616")));
            string cutZero = t.Replace("\"cutouts\": [\n    {\n      \"id\": 1,", "\"cutouts\": [\n    {\n      \"id\": 0,");
            Assert.That(Error(cutZero), Is.EqualTo("MAPDESC_ID cutout line " + Line(cutZero, "\"id\": 0")));
            string nextZero = t.Replace("\"plate\": 2,", "\"plate\": 0,");
            Assert.That(Error(nextZero), Is.EqualTo("MAPDESC_ID line " + Line(nextZero, "\"plate\": 0,")));
            string nextCutZero = t.Replace("\"cutout\": 2\n", "\"cutout\": 0\n");
            Assert.That(Error(nextCutZero), Is.EqualTo("MAPDESC_ID line " + Line(nextCutZero, "\"cutout\": 0")));
        }

        [Test]
        public void IdsAreUniqueAndBelowNext()
        {
            string two = Ml4Maps.Text(Ml4Maps.Make(0, 6, 4, null, new[] { Ml4Maps.Flat(1, 0, 0, 1, 1, M), Ml4Maps.Flat(2, 1, 0, 1, 1, M) },
                new[] { Ml4Maps.Cut(1, 1, 0, 0, 1, 1), Ml4Maps.Cut(2, 2, 1, 0, 1, 1) }));
            string dup = two.Replace("\"id\": 2,\n      \"cell\": [1, 0]", "\"id\": 1,\n      \"cell\": [1, 0]");
            Assert.That(Error(dup), Is.EqualTo("MAPDESC_ID plate 1 line " + Line(dup, "\"id\": 1,", 1)));
            string atNext = two.Replace("\"plate\": 3,", "\"plate\": 2,");
            Assert.That(Error(atNext), Is.EqualTo("MAPDESC_ID plate 2 line " + Line(atNext, "\"id\": 2,")));
            int cutouts = two.IndexOf("\"cutouts\"");
            string cutDup = two.Substring(0, cutouts) + two.Substring(cutouts).Replace("\"id\": 2,", "\"id\": 1,");
            Assert.That(Error(cutDup), Is.EqualTo("MAPDESC_ID cutout 1 line " + Line(cutDup, "\"id\": 1,", 2)));
            string cutNext = two.Replace("\"cutout\": 3\n", "\"cutout\": 2\n");
            Assert.That(Error(cutNext), Is.EqualTo("MAPDESC_ID cutout 2 line " + Line(cutNext, "\"id\": 2,", 1)));
        }

        // cell + size = the map edge is inside, one more is out (both axes, plates and cutouts).
        [Test]
        public void RectanglesStayOnTheMap()
        {
            string t = FlatText();
            Assert.That(Read(t.Replace("\"cell\": [1, 1],\n      \"size\": [3, 2]", "\"cell\": [3, 2],\n      \"size\": [3, 2]")).Plates[0].Cell.X, Is.EqualTo(3));
            string x = t.Replace("\"cell\": [1, 1],\n      \"size\": [3, 2]", "\"cell\": [4, 2],\n      \"size\": [3, 2]");
            Assert.That(Error(x), Is.EqualTo(P(x, "\"size\": [3, 2]")));
            string z = t.Replace("\"cell\": [1, 1],\n      \"size\": [3, 2]", "\"cell\": [3, 3],\n      \"size\": [3, 2]");
            Assert.That(Error(z), Is.EqualTo(P(z, "\"size\": [3, 2]")));
            Assert.That(Read(t.Replace("\"size\": [1, 1]\n", "\"size\": [5, 3]\n")).Cutouts[0].Size.X, Is.EqualTo(5));
            string cut = t.Replace("\"size\": [1, 1]\n", "\"size\": [6, 3]\n");
            Assert.That(Error(cut), Is.EqualTo("MAPDESC_PLATE cutout 1 line " + Line(cut, "\"size\": [6, 3]")));
        }

        // Tops in [floorRaw, ceilingRaw], both ends included; fromRaw is judged before toRaw; a slope must rise or fall.
        [Test]
        public void TopsStayBetweenFloorAndCeiling()
        {
            string t = FlatText();
            Assert.That(Read(t.Replace("\"yRaw\": 196608", "\"yRaw\": 0")).Plates[0].YRaw, Is.EqualTo(0));
            Assert.That(Error(t.Replace("\"yRaw\": 196608", "\"yRaw\": -1")), Is.EqualTo(P(t, "\"yRaw\"")));
            Assert.That(Read(t.Replace("\"yRaw\": 196608", "\"yRaw\": 655360")).Plates[0].YRaw, Is.EqualTo(10 * M));
            Assert.That(Error(t.Replace("\"yRaw\": 196608", "\"yRaw\": 655361")), Is.EqualTo(P(t, "\"yRaw\"")));
            string s = SlopeText();
            string both = s.Replace("\"fromRaw\": 65536", "\"fromRaw\": -1").Replace("\"toRaw\": 131072", "\"toRaw\": 655361");
            Assert.That(Error(both), Is.EqualTo(P(s, "\"fromRaw\"")));
            Assert.That(Error(s.Replace("\"toRaw\": 131072", "\"toRaw\": 655361")), Is.EqualTo(P(s, "\"toRaw\"")));
            Assert.That(Error(s.Replace("\"toRaw\": 131072", "\"toRaw\": 65536")), Is.EqualTo(P(s, "\"slope\"")));
            string low = s.Replace("\"floorRaw\": 0", "\"floorRaw\": -65536").Replace("\"ceilingRaw\": 655360", "\"ceilingRaw\": 589824");
            Assert.That(Read(low.Replace("\"fromRaw\": 65536", "\"fromRaw\": -65536")).Plates[0].Slope.FromRaw, Is.EqualTo(-M));
            Assert.That(Error(low.Replace("\"fromRaw\": 65536", "\"fromRaw\": -65537")), Is.EqualTo(P(low, "\"fromRaw\"")));
        }

        [Test]
        public void SurfaceMustBeInTheTable()
        {
            string t = FlatText().Replace("\"thickRaw\": 32768\n", "\"thickRaw\": 32768,\n      \"surface\": \"road\"\n");
            Assert.That(Error(t), Is.EqualTo(P(t, "\"surface\"")), "no surface table");
            MapDesc shapes = Ml4DescTests.AllShapes();
            string text = Ml4Maps.Text(shapes);
            string unknown = text.Replace("\"surface\": \"road\"", "\"surface\": \"mud\"");
            Assert.That(Error(unknown), Is.EqualTo("MAPDESC_PLATE plate 2 line " + Line(unknown, "\"surface\": \"mud\"")));
        }

        [Test]
        public void CutoutPointsAtAPlate()
        {
            string t = FlatText().Replace("\"plate\": 1,\n", "\"plate\": 7,\n");
            Assert.That(Error(t), Is.EqualTo("MAPDESC_PLATE cutout 1 line " + Line(t, "\"plate\": 7")));
        }

        [Test]
        public void OwnerAndSrcFollowTail()
        {
            string text = Ml4Maps.Text(Ml4DescTests.AllShapes());
            string owner = text.Replace("\"owner\": 1,\n      \"src\": {\n        \"instance\": \"s\",\n        \"kind\": \"plate\"",
                "\"owner\": 2,\n      \"src\": {\n        \"instance\": \"s\",\n        \"kind\": \"plate\"");
            Assert.That(Error(owner), Is.EqualTo("MAPDESC_OWNER plate 4 line " + Line(owner, "\"owner\": 2")));
            string kind = text.Replace("\"kind\": \"plate\",\n        \"src\": 1\n      }", "\"kind\": \"wall\",\n        \"src\": 1\n      }");
            Assert.That(Error(kind), Is.EqualTo("MAPDESC_OWNER plate 4 line " + Line(kind, "\"kind\": \"wall\"")));
            string cutKind = text.Replace("\"kind\": \"cutout\",\n        \"src\": 2\n      }", "\"kind\": \"plate\",\n        \"src\": 2\n      }");
            Assert.That(Error(cutKind), Is.EqualTo("MAPDESC_OWNER cutout 2 line " + Line(cutKind, "\"kind\": \"plate\",\n        \"src\": 2")));
        }

        // 4096 entries are read, 4097 are an error on the array's line (plates and cutouts).
        [TestCase(false)]
        [TestCase(true)]
        public void AtMost4096Entries(bool cutouts)
        {
            MapDesc Many(int n)
            {
                var plates = cutouts ? new[] { Ml4Maps.Flat(1, 0, 0, 64, 65, M) }
                    : Enumerable.Range(0, n).Select(k => Ml4Maps.Flat((ulong)k + 1, k % 64, k / 64, 1, 1, M)).ToArray();
                var cuts = cutouts ? Enumerable.Range(0, n).Select(k => Ml4Maps.Cut((ulong)k + 1, 1, k % 64, k / 64, 1, 1)).ToArray() : new MapCutout[0];
                return Ml4Maps.Make(0, 64, 65, null, plates, cuts);
            }
            Assert.That(cutouts ? Read(Ml4Maps.Text(Many(4096))).Cutouts.Count : Read(Ml4Maps.Text(Many(4096))).Plates.Count, Is.EqualTo(4096));
            string over = Ml4Maps.Text(Many(4097));
            Assert.That(Error(over), Is.EqualTo("MAPDESC_PLATE line " + Line(over, cutouts ? "\"cutouts\"" : "\"plates\"")));
        }
    }
}
