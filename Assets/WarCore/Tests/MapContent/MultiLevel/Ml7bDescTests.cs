using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ml7bMaps;

namespace WarCore.Tests
{
    // ML-7b acceptance 1, 2 and invariant 3: plate ids on points' spawns, bases and units in the description.
    public sealed class Ml7bDescTests
    {
        // Point 1's spawn on the first floor (id 9), base 1 on the first floor, unit 1 on the stair (id 5); the rest on the ground.
        internal static MapDesc OnPlates() =>
            House(new[] { Point(1, 5000, 13000, new MapMm(11500, 7500), 9), Point(2, 19000, 13000, new MapMm(19000, 13000)) },
                new[] { Base(1, 1, 11500, 8500, 9), Base(2, 2, 22000, 8000) }, new[] { Unit(1, 12500, 9500, 5), Unit(2, 1500, 1500) });

        [Test]
        public void TheNewKeysRoundTrip()
        {
            string text = Text(OnPlates());
            MapDesc d = Read(text);
            Assert.That(d.Version, Is.EqualTo(2));
            Assert.That((d.Points[0].SpawnPlate, d.Points[1].SpawnPlate), Is.EqualTo(((ulong?)9, (ulong?)null)));
            Assert.That((d.Bases[0].Plate, d.Bases[1].Plate), Is.EqualTo(((ulong?)9, (ulong?)null)));
            Assert.That((d.Units[0].Plate, d.Units[1].Plate), Is.EqualTo(((ulong?)5, (ulong?)null)));
            Assert.That(Encoding.UTF8.GetString(MapDescJson.Write(d)), Is.EqualTo(text), "version 2 bytes round trip");
            StringAssert.Contains("\"spawn\": [11500, 7500],\n      \"spawnPlate\": 9\n", text);
            StringAssert.Contains("\"at\": [11500, 8500],\n      \"plate\": 9\n", text);
            StringAssert.Contains("\"reserve\": false,\n      \"plate\": 5\n", text);
        }

        // Invariant 3 and acceptance 1: a description without plate ids writes none of the keys, and version 1 stays version 1
        // byte for byte.
        [Test]
        public void VersionOneStaysVersionOne()
        {
            MapDesc flat = Ml4Maps.Make(0, 6, 4, null, new MapPlate[0], units: new[] { Unit(1, 1500, 1500) });
            string text = Text(flat);
            Assert.That(flat.Version, Is.EqualTo(1));
            StringAssert.Contains("\"version\": 1,", text);
            Assert.That(text.Contains("plate") || text.Contains("Plate"), Is.False);
            Assert.That(Encoding.UTF8.GetString(MapDescJson.Write(Read(text))), Is.EqualTo(text));
            Assert.That(Text(House()).Contains("spawnPlate") || Text(House()).Contains("\"plate\": 9\n"), Is.False, "no ids, no keys, even with plates");
        }

        // Each key alone makes the model version 2; on the bare tree each key alone counts as a version 2 key (the version line
        // passes and the reader goes on to the missing plate), and without it version 2 is a version error.
        [TestCase("spawnPlate")]
        [TestCase("base")]
        [TestCase("unit")]
        public void EachKeyAloneNeedsVersionTwo(string key)
        {
            MapPoint[] points = { Point(1, 1500, 1500, new MapMm(1500, 1500), key == "spawnPlate" ? 7 : (ulong?)null) };
            MapBase[] bases = { Base(1, 1, 2500, 1500, key == "base" ? 7 : (ulong?)null) };
            MapUnit[] units = { Unit(1, 3500, 1500, key == "unit" ? 7 : (ulong?)null) };
            MapDesc d = Ml4Maps.Make(0, 6, 4, null, new MapPlate[0], units: units);
            d = new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null, new MapPlate[0],
                new MapCutout[0], new MapWall[0], points, bases, units, d.Run, d.Owners.ToArray(), null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(1, 2, 2, 2, 1, 1));
            Assert.That(d.Version, Is.EqualTo(2));
            string text = Text(d);
            string fragment = key == "spawnPlate" ? "\"spawnPlate\": 7" : "\"plate\": 7";
            string entry = key == "spawnPlate" ? "point 1" : key == "base" ? "base 1" : "unit 1";
            Assert.That(Error(text), Is.EqualTo("MAPDESC_PLATE " + entry + " line " + Line(text, fragment)));
            string without = text.Replace(",\n      " + fragment, "");
            Assert.That(Error(without), Is.EqualTo("MAPDESC_FORMAT line 3"));
            // Under version 1 the key is unknown, as "baseRaw" (Ml1DescTests.VersionOneDoesNotKnowBaseRaw): pass 1, before any
            // plate id is looked up.
            string one = text.Replace("\"version\": 2,", "\"version\": 1,");
            Assert.That(Error(one), Is.EqualTo("MAPDESC_KEY " + entry + " line " + Line(one, fragment)));
        }

        [Test]
        public void AnUnknownPlateIsRefused()
        {
            string text = Text(OnPlates());
            string spawn = text.Replace("\"spawnPlate\": 9", "\"spawnPlate\": 8");
            Assert.That(Error(spawn), Is.EqualTo("MAPDESC_PLATE point 1 line " + Line(spawn, "\"spawnPlate\": 8")));
            string anchor = text.Replace("\"at\": [11500, 8500],\n      \"plate\": 9", "\"at\": [11500, 8500],\n      \"plate\": 1");
            Assert.That(Error(anchor), Is.EqualTo("MAPDESC_PLATE base 1 line " + Line(anchor, "\"plate\": 1")));
            string unit = text.Replace("\"plate\": 5", "\"plate\": 10");
            Assert.That(Error(unit), Is.EqualTo("MAPDESC_PLATE unit 1 line " + Line(unit, "\"plate\": 10")));
            string zero = text.Replace("\"plate\": 5", "\"plate\": 0");
            Assert.That(Error(zero), Is.EqualTo("MAPDESC_PLATE unit 1 line " + Line(zero, "\"plate\": 0")), "ids start at 1");
        }

        [Test]
        public void ASpawnPlateNeedsASpawn()
        {
            MapDesc d = House(new[] { Point(1, 5000, 13000, null, 9), Point(2, 19000, 13000, new MapMm(19000, 13000)) });
            string text = Text(d);
            Assert.That(Error(text), Is.EqualTo("MAPDESC_POINT point 1 line " + Line(text, "\"spawnPlate\": 9")));
        }

        [Test]
        public void AReserveStaysOnTheGround()
        {
            MapDesc d = House(units: new[] { Unit(1, 11500, 7500, 9, reserve: true) });
            string text = Text(d);
            Assert.That(Error(text), Is.EqualTo("MAPDESC_PLATE unit 1 line " + (Line(text, "\"reserve\": true,") + 1)));
            Assert.That(Read(Text(House(units: new[] { Unit(1, 11500, 7500, 9) }))).Units[0].Plate, Is.EqualTo((ulong?)9), "not a reserve");
        }
    }
}
