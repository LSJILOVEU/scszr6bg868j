using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-4 acceptance 2, 3 and 4: plates and cutouts in the description, their round trip, the top heights and the version.
    public sealed class Ml4DescTests
    {
        private const long M = Ml4Maps.M;

        internal static MapDesc Read(string text)
        {
            MapDesc d = null;
            Assert.DoesNotThrow(() => d = MapDescJson.Read(Encoding.UTF8.GetBytes(text)));
            return d;
        }

        internal static string Error(string text) => Assert.Throws<InvalidDataException>(() => MapDescJson.Read(Encoding.UTF8.GetBytes(text))).Message;

        internal static int Line(string text, string fragment, int skip = 0)
        {
            int at = -1;
            for (int n = 0; n <= skip; n++)
            {
                at = text.IndexOf(fragment, at + 1, System.StringComparison.Ordinal);
                Assert.That(at < 0, Is.False, fragment);
            }
            return text.Substring(0, at).Split('\n').Length;
        }

        // Every plate shape: flat, x and z slopes rising and falling, a surface, an owner and a src; and a cutout.
        internal static MapDesc AllShapes()
        {
            var surfaces = new MapSurfaces(new[] { "grass", "road" }, Enumerable.Repeat("AAAAAAAAAAAA", 8).ToArray());
            var plates = new[]
            {
                new MapPlate(1, new MapXZ(0, 0), new MapXZ(3, 2), 3 * M, null, M / 2),
                new MapPlate(2, new MapXZ(3, 0), new MapXZ(4, 2), null, new MapSlope("x", 0, 3 * M), M / 2, "road"),
                new MapPlate(3, new MapXZ(7, 0), new MapXZ(4, 2), null, new MapSlope("x", 3 * M, M), M),
                new MapPlate(4, new MapXZ(0, 3), new MapXZ(2, 4), null, new MapSlope("z", M, 2 * M), M / 2, null, 1, new MapSrc("s", "plate", 1)),
                new MapPlate(5, new MapXZ(3, 3), new MapXZ(2, 4), null, new MapSlope("z", 2 * M, 0), 2 * M, "grass"),
            };
            var cutouts = new[] { new MapCutout(1, 1, new MapXZ(1, 0), new MapXZ(1, 1)), new MapCutout(2, 4, new MapXZ(0, 3), new MapXZ(1, 1), 1, new MapSrc("s", "cutout", 2)) };
            return new MapDesc("ml4", "none", "rwr-terrain-demo", new MapXZ(12, 8), new MapXZ(0, 0), 0, 10 * M,
                new[] { new MapLayer("walls", 1) }, null, surfaces, plates, cutouts, new MapWall[0], new MapPoint[0], new MapBase[0],
                new MapUnit[0], new MapRun(41, "none"), new[] { MapEdit.Hand, "stamp:s" }, null, null,
                new[] { new MapStamp("s", "plate", 1, 4), new MapStamp("s", "cutout", 2, 2) }, new MapSrc[0], new MapNext(1, 1, 1, 1, 6, 3));
        }

        [Test]
        public void PlatesRoundTripAsVersionTwo()
        {
            byte[] first = Ml4Maps.Bytes(AllShapes());
            MapDesc read = MapDescJson.Read(first);
            byte[] second = MapDescJson.Write(read);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(MapDescJson.Write(MapDescJson.Read(second)), Is.EqualTo(first));
            Assert.That(read.Version, Is.EqualTo(2));
            Assert.That(read.Plates.Select(p => (p.Id, p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z, p.YRaw, p.Slope?.Axis, p.Slope?.FromRaw, p.Slope?.ToRaw, p.ThickRaw, p.Surface, p.Owner)),
                Is.EqualTo(AllShapes().Plates.Select(p => (p.Id, p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z, p.YRaw, p.Slope?.Axis, p.Slope?.FromRaw, p.Slope?.ToRaw, p.ThickRaw, p.Surface, p.Owner))));
            Assert.That(read.Plates[3].Src.Instance + read.Plates[3].Src.Kind + read.Plates[3].Src.Src, Is.EqualTo("splate1"));
            Assert.That(read.Cutouts.Select(c => (c.Id, c.Plate, c.Cell.X, c.Cell.Z, c.Size.X, c.Size.Z, c.Owner)),
                Is.EqualTo(new[] { (1ul, 1ul, 1, 0, 1, 1, (int?)null), (2ul, 4ul, 0, 3, 1, 1, (int?)1) }));
            Assert.That((read.Next.Plate, read.Next.Cutout), Is.EqualTo((6ul, 3ul)));
            string text = Encoding.UTF8.GetString(first);
            Assert.That(text.IndexOf("\"surfaces\"") < text.IndexOf("\"plates\"") && text.IndexOf("\"plates\"") < text.IndexOf("\"cutouts\"")
                && text.IndexOf("\"cutouts\"") < text.IndexOf("\"walls\": ["), Is.True, "plates and cutouts sit between surfaces and walls");
            Assert.That(text.IndexOf("\"unit\"") < text.IndexOf("\"plate\": 6") && text.IndexOf("\"plate\": 6") < text.IndexOf("\"cutout\": 3"), Is.True);
        }

        // The .wcmap is version 3 and reads back every plate field; plates are in id order with cutouts dug in.
        [Test]
        public void WcMapCarriesThePlates()
        {
            MapBakeResult bake = MapBake.Run(Ml4Maps.Bytes(AllShapes()));
            Assert.That(bake.WcMapBytes[4] | bake.WcMapBytes[5] << 8, Is.EqualTo(3), "version");
            WcMap map = WcMapReader.Read(bake.WcMapBytes);
            Assert.That(map.Plates.Select(p => (p.Id, p.X, p.Z, p.Width, p.Depth, p.Axis, p.FromRaw, p.ToRaw, p.ThickRaw, p.Surface)), Is.EqualTo(new[]
            {
                (1ul, 0, 0, 3, 2, PlateAxis.Flat, (int)(3 * M), (int)(3 * M), (int)(M / 2), WcMapPlate.NoSurface),
                (2ul, 3, 0, 4, 2, PlateAxis.X, 0, (int)(3 * M), (int)(M / 2), (byte)1),
                (3ul, 7, 0, 4, 2, PlateAxis.X, (int)(3 * M), (int)M, (int)M, WcMapPlate.NoSurface),
                (4ul, 0, 3, 2, 4, PlateAxis.Z, (int)M, (int)(2 * M), (int)(M / 2), WcMapPlate.NoSurface),
                (5ul, 3, 3, 2, 4, PlateAxis.Z, (int)(2 * M), 0, (int)(2 * M), (byte)0),
            }));
            Assert.That(map.Plates[0].IsSolid(1, 0), Is.False);
            Assert.That(map.Plates[0].IsSolid(0, 0) && map.Plates[0].IsSolid(1, 1), Is.True);
            Assert.That(map.Plates[3].IsSolid(0, 0), Is.False);
            Assert.That(map.Plates[0].Solid, Is.EqualTo(new byte[] { 0b111101 }));
            Assert.That(map.Plates[0].Walkable, Is.EqualTo(new byte[] { 0b111101 }));
            byte[] again = WcMapWriter.Write(map);
            Assert.That(again, Is.EqualTo(bake.WcMapBytes));
        }

        // Without plates the .wcmap stays version 2, 11 sections.
        [Test]
        public void WcMapWithoutPlatesIsVersionTwo()
        {
            MapBakeResult bake = MapBake.Run(Ml4Maps.Bytes(Ml4Maps.Make(0, 4, 2, null, new MapPlate[0])));
            Assert.That(bake.WcMapBytes[4] | bake.WcMapBytes[5] << 8, Is.EqualTo(2));
            Assert.That(WcMapReader.Read(bake.WcMapBytes).Plates, Is.Empty);
        }

        // ---- acceptance 3 ----

        [Test]
        public void TopPointsOnTheGridLines()
        {
            Assert.That(Enumerable.Range(0, 5).Select(k => PlateSpan.TopPoint(PlateAxis.X, 0, 196608, 4, 1, k, 0)),
                Is.EqualTo(new long[] { 0, 49152, 98304, 147456, 196608 }));
            Assert.That(Enumerable.Range(0, 4).Select(k => PlateSpan.TopPoint(PlateAxis.Z, 0, 100000, 1, 3, 0, k)),
                Is.EqualTo(new long[] { 0, 33333, 66666, 100000 }));
            Assert.That(Enumerable.Range(0, 4).Select(k => PlateSpan.TopPoint(PlateAxis.X, 100000, 0, 3, 1, k, 0)),
                Is.EqualTo(new long[] { 100000, 66666, 33333, 0 }), "floor toward negative infinity");
            Assert.That(PlateSpan.TopPoint(PlateAxis.X, 0, 196608, 4, 3, 2, 2), Is.EqualTo(98304), "the same across the other axis");
            Assert.That(PlateSpan.TopPoint(PlateAxis.Flat, 5, 5, 4, 3, 4, 3), Is.EqualTo(5));
            Assert.That(PlateSpan.FloorDiv(-7, 2), Is.EqualTo(-4));
            Assert.That(PlateSpan.FloorDiv(7, -2), Is.EqualTo(-4));
            Assert.That(PlateSpan.FloorDiv(-8, 2), Is.EqualTo(-4));
            Assert.That(PlateSpan.FloorDiv(7, 2), Is.EqualTo(3));
        }

        // The plate top as a height field placed on the map: the cell-centre heights are the hand values.
        [Test]
        public void TopFieldSamplesTheHandValues()
        {
            var plate = new WcMapPlate(1, 2, 1, 3, 1, PlateAxis.X, 0, 100000, 32768, WcMapPlate.NoSurface, new byte[] { 7 }, new byte[] { 7 });
            HeightField top = plate.TopField(SimFix.FromInt(10), SimFix.FromInt(20));
            long[] expected = { 16666, 49999, 83333 };
            for (int i = 0; i < 3; i++)
            {
                Assert.That(top.TryHeightAt(SimFix.FromRaw((12 + i) * M + M / 2), SimFix.FromRaw(21 * M + M / 2), out SimFix h), Is.True);
                Assert.That(h.Raw, Is.EqualTo(expected[i]).Within(1), "cell " + i);
            }
            Assert.That(top.TryHeightAt(SimFix.FromRaw(11 * M), SimFix.FromRaw(21 * M + M / 2), out _), Is.False, "west of the plate");
            Assert.That(plate.TopRaw(3, 0), Is.EqualTo(100000));
        }

        // ---- acceptance 4: version 2 only when one of the six things is used ----

        private static string V1 => Ml4Maps.Text(Ml4Maps.Make(0, 4, 2, null, new MapPlate[0]));

        private static readonly string[] OneThing =
        {
            "baseRaw", "plates", "cutouts", "owners", "stamps", "removed", "nextPlate", "nextCutout",
        };

        // A version 2 text that uses only the named thing (cutouts: the cutout points at no plate, so the version step
        // passes and pass 2 reports the plate reference).
        private static string Using(string thing)
        {
            string t = V1.Replace("\"version\": 1,", "\"version\": 2,");
            switch (thing)
            {
                case "baseRaw": return t.Replace("\"walls\": [],", "\"walls\": [{\"id\": 1, \"kind\": \"wall\", \"layer\": \"walls\", \"cell\": [1, 0], \"size\": [1, 1], \"heightRaw\": 65536, \"baseRaw\": 131072}],")
                    .Replace("\"wall\": 1,", "\"wall\": 2,");
                case "plates": return t.Replace("\"walls\": [],", "\"plates\": [{\"id\": 1, \"cell\": [0, 0], \"size\": [1, 1], \"yRaw\": 196608, \"thickRaw\": 32768}], \"walls\": [],")
                    .Replace("\"unit\": 1\n", "\"unit\": 1, \"plate\": 2\n");
                case "cutouts": return t.Replace("\"walls\": [],", "\"cutouts\": [{\"id\": 1, \"plate\": 1, \"cell\": [0, 0], \"size\": [1, 1]}], \"walls\": [],")
                    .Replace("\"unit\": 1\n", "\"unit\": 1, \"cutout\": 2\n");
                case "owners": return t.Replace("\"owners\": [\"hand\"]", "\"owners\": [\"hand\"" + string.Concat(Enumerable.Range(1, 64).Select(k => ", \"stamp:s" + k + "\"")) + "]");
                case "stamps": return t.Replace("\"stamps\": []", "\"stamps\": [{\"instance\": \"s\", \"kind\": \"plate\", \"src\": 1, \"id\": 1}]");
                case "removed": return t.Replace("\"removed\": []", "\"removed\": [{\"instance\": \"s\", \"kind\": \"cutout\", \"src\": 1}]");
                case "nextPlate": return t.Replace("\"unit\": 1\n", "\"unit\": 1, \"plate\": 2\n");
                default: return t.Replace("\"unit\": 1\n", "\"unit\": 1, \"cutout\": 2\n");
            }
        }

        [TestCaseSource(nameof(OneThing))]
        public void EachThingAloneMakesVersionTwo(string thing)
        {
            string text = Using(thing);
            if (thing == "cutouts")
            {
                Assert.That(Error(text), Is.EqualTo("MAPDESC_PLATE cutout 1 line " + Line(text, "\"plate\": 1,")), "past the version step");
                var model = new MapDesc("m", "none", "rwr-m6", new MapXZ(4, 2), new MapXZ(0, 0), 0, M, new MapLayer[0], null, null, new MapPlate[0],
                    new[] { Ml4Maps.Cut(1, 1, 0, 0, 1, 1) }, new MapWall[0], new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(1, "none"),
                    new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0], new MapNext(1, 1, 1, 1, 1, 2));
                Assert.That(model.Version, Is.EqualTo(2));
                return;
            }
            MapDesc d = Read(text);
            Assert.That(d.Version, Is.EqualTo(2), thing);
            byte[] written = MapDescJson.Write(d);
            Assert.That(MapDescJson.Write(MapDescJson.Read(written)), Is.EqualTo(written));
            Assert.That(Encoding.UTF8.GetString(written), Does.Contain("\"version\": 2,"));
        }

        // Plates or cutouts alone make version 2, before next is looked at: with next at 1 the text gets past the version
        // step to the id error, and a model built with next at 1 is still version 2.
        [Test]
        public void PlatesAndCutoutsDecideWithoutNext()
        {
            string plates = Using("plates").Replace("\"unit\": 1, \"plate\": 2\n", "\"unit\": 1\n");
            Assert.That(Error(plates), Is.EqualTo("MAPDESC_ID plate 1 line " + Line(plates, "\"id\": 1, \"cell\"")));
            string cutouts = Using("cutouts").Replace("\"unit\": 1, \"cutout\": 2\n", "\"unit\": 1\n");
            Assert.That(Error(cutouts), Is.EqualTo("MAPDESC_ID cutout 1 line " + Line(cutouts, "\"id\": 1, \"plate\"")));
            var next = new MapNext(1, 1, 1, 1, 1, 1);
            var withPlate = new MapDesc("m", "none", "rwr-m6", new MapXZ(4, 2), new MapXZ(0, 0), 0, M, new MapLayer[0], null, null,
                new[] { Ml4Maps.Flat(1, 0, 0, 1, 1, 3 * M) }, new MapCutout[0], new MapWall[0], new MapPoint[0], new MapBase[0], new MapUnit[0],
                new MapRun(1, "none"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0], next);
            Assert.That(withPlate.Version, Is.EqualTo(2), "plates");
            var withCutout = new MapDesc("m", "none", "rwr-m6", new MapXZ(4, 2), new MapXZ(0, 0), 0, M, new MapLayer[0], null, null,
                new MapPlate[0], new[] { Ml4Maps.Cut(1, 1, 0, 0, 1, 1) }, new MapWall[0], new MapPoint[0], new MapBase[0], new MapUnit[0],
                new MapRun(1, "none"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0], next);
            Assert.That(withCutout.Version, Is.EqualTo(2), "cutouts");
        }

        // The same text as version 1: the error version 1 always gave.
        [TestCaseSource(nameof(OneThing))]
        public void EachThingIsAnErrorInVersionOne(string thing)
        {
            string text = Using(thing).Replace("\"version\": 2,", "\"version\": 1,");
            string expected;
            switch (thing)
            {
                case "baseRaw": expected = "MAPDESC_KEY wall 1 line " + Line(text, "\"baseRaw\""); break;
                case "plates": expected = "MAPDESC_KEY line " + Line(text, "\"plates\""); break;
                case "cutouts": expected = "MAPDESC_KEY line " + Line(text, "\"cutouts\""); break;
                case "owners": expected = "MAPDESC_OWNER line " + Line(text, "\"owners\""); break;
                case "stamps": expected = "MAPDESC_OWNER stamp line " + Line(text, "\"kind\": \"plate\""); break;
                case "removed": expected = "MAPDESC_OWNER removed line " + Line(text, "\"kind\": \"cutout\""); break;
                case "nextPlate": expected = "MAPDESC_KEY line " + Line(text, "\"plate\": 2"); break;
                default: expected = "MAPDESC_KEY line " + Line(text, "\"cutout\": 2"); break;
            }
            Assert.That(Error(text), Is.EqualTo(expected), thing);
        }

        // Version 2 using none of them is a version error on line 3, empty arrays and next 1 included.
        [Test]
        public void VersionTwoUsingNothingIsAVersionError()
        {
            string t = V1.Replace("\"version\": 1,", "\"version\": 2,");
            Assert.That(Error(t), Is.EqualTo("MAPDESC_FORMAT line 3"));
            Assert.That(Error(t.Replace("\"walls\": [],", "\"plates\": [], \"cutouts\": [], \"walls\": [],")), Is.EqualTo("MAPDESC_FORMAT line 3"));
            Assert.That(Error(t.Replace("\"unit\": 1\n", "\"unit\": 1, \"plate\": 1, \"cutout\": 1\n")), Is.EqualTo("MAPDESC_FORMAT line 3"));
            Assert.That(Error(t.Replace("\"owners\": [\"hand\"]", "\"owners\": [\"hand\"" + string.Concat(Enumerable.Range(1, 63).Select(k => ", \"stamp:s" + k + "\"")) + "]")),
                Is.EqualTo("MAPDESC_FORMAT line 3"), "64 owners fit version 1");
            Assert.That(Error(t.Replace("\"stamps\": []", "\"stamps\": [{\"instance\": \"s\", \"kind\": \"wall\", \"src\": 1, \"id\": 1}]")), Is.EqualTo("MAPDESC_FORMAT line 3"));
        }

        // The model makes the same decision on each of the six things.
        [Test]
        public void ModelDecidesTheSame()
        {
            foreach (string thing in OneThing.Where(t => t != "cutouts"))
                Assert.That(Read(Using(thing)).NeedsVersion2, Is.True, thing);
            Assert.That(Read(V1).NeedsVersion2, Is.False);
        }
    }
}
