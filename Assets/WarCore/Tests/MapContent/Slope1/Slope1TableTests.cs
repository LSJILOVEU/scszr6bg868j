using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;
using static WarCore.Tests.Slope1;

namespace WarCore.Tests
{
    // SLOPE-1 S2 (bands, pure functions) and S18 (reading the two new sections).
    public sealed class Slope1TableTests
    {
        private static SlopeTable Rwr() => new SlopeTable(new[] { 100, 300, 500 }, new[] { 900, 700, 500 }, new[] { 400 }, new[] { 800 });

        // S2. floor(|rise| * 1000 / 32768): 3276 -> 99.98, 3277 -> 100.006, 3309 -> 100.98, 3310 -> 101.01.
        [TestCase(3276L, 99)]
        [TestCase(3277L, 100)]
        [TestCase(3309L, 100)]
        [TestCase(3310L, 101)]
        [TestCase(-3310L, 101)]
        [TestCase(0L, 0)]
        public void S2_SlopePermille_Floors(long rise, int expected)
        {
            Assert.DoesNotThrow(() =>
            {
                Assert.That(SlopeTable.SlopePermille(rise), Is.EqualTo(expected));
            });
        }

        // S2. The last band whose abovePermille the slope exceeds (strictly); none matched is 1000.
        [TestCase(0, true, 1000)]
        [TestCase(100, true, 1000)]
        [TestCase(101, true, 900)]
        [TestCase(300, true, 900)]
        [TestCase(301, true, 700)]
        [TestCase(500, true, 700)]
        [TestCase(501, true, 500)]
        [TestCase(2000, true, 500)]
        [TestCase(400, false, 1000)]
        [TestCase(401, false, 800)]
        [TestCase(1999, false, 800)]
        public void S2_Bands_TakeTheLastExceeded(int slope, bool up, int expected)
        {
            Assert.DoesNotThrow(() =>
            {
                Assert.That(Rwr().SpeedPermille(slope, up), Is.EqualTo(expected));
            });
        }

        [Test]
        public void S2_OnlyUp_DownhillIsAlways1000()
        {
            Assert.DoesNotThrow(() =>
            {
                var upOnly = new SlopeTable(new[] { 100 }, new[] { 900 }, null, null);
                foreach (int slope in new[] { 0, 101, 401, 2000 }) Assert.That(upOnly.SpeedPermille(slope, false), Is.EqualTo(1000));
                Assert.That(upOnly.SpeedPermille(101, true), Is.EqualTo(900));
                Assert.That(upOnly.HasDown, Is.False);
            });
        }

        [Test]
        public void S2_Constructor_RefusesBadTables()
        {
            Assert.DoesNotThrow(() =>
            {
                Assert.Throws<ArgumentException>(() => new SlopeTable(null, null, null, null));
                Assert.Throws<ArgumentException>(() => new SlopeTable(new[] { 100, 100 }, new[] { 900, 800 }, null, null));
                Assert.Throws<ArgumentOutOfRangeException>(() => new SlopeTable(new[] { 2001 }, new[] { 900 }, null, null));
                Assert.Throws<ArgumentOutOfRangeException>(() => new SlopeTable(new[] { 100 }, new[] { 1001 }, null, null));
                Assert.Throws<ArgumentOutOfRangeException>(() => new SlopeTable(new[] { 100 }, new[] { 0 }, null, null));
                Assert.Throws<ArgumentOutOfRangeException>(() => new SlopeTable(new int[0], new int[0], null, null));
                Assert.DoesNotThrow(() => new SlopeTable(new[] { 0, 2000 }, new[] { 1, 1000 }, null, null));
            });
        }

        [Test]
        public void S2_Digest_FollowsEveryBand()
        {
            Assert.DoesNotThrow(() =>
            {
                ulong d = Rwr().Digest;
                Assert.That(new SlopeTable(new[] { 100, 300, 500 }, new[] { 900, 700, 500 }, new[] { 400 }, new[] { 800 }).Digest, Is.EqualTo(d));
                Assert.That(new SlopeTable(new[] { 100, 300, 501 }, new[] { 900, 700, 500 }, new[] { 400 }, new[] { 800 }).Digest, Is.Not.EqualTo(d));
                Assert.That(new SlopeTable(new[] { 100, 300, 500 }, new[] { 900, 700, 501 }, new[] { 400 }, new[] { 800 }).Digest, Is.Not.EqualTo(d));
                Assert.That(new SlopeTable(new[] { 400 }, new[] { 800 }, new[] { 100, 300, 500 }, new[] { 900, 700, 500 }).Digest, Is.Not.EqualTo(d));
            });
        }

        // ---- S18 ----

        private static string Band(int above, int speed) => "{ \"abovePermille\": " + above + ", \"speedPermille\": " + speed + " }";
        private static string Movement(string slopeBody) => "\"movement\": { \"slope\": { " + slopeBody + " } }";

        private static void Refused(string extra, params string[] fragments)
        {
            var e = Assert.Throws<ContentException>(() => Layer("slope-bad", extra));
            foreach (string f in fragments) StringAssert.Contains(f, e.Message);
        }

        [Test]
        public void S18_SlopeBands_Refused()
        {
            Assert.DoesNotThrow(() =>
            {
                Refused(Movement("\"up\": [ " + Band(100, 900) + ", " + Band(50, 700) + " ]"), "movement.slope.up[1]", "abovePermille");
                Refused(Movement("\"up\": [ " + Band(100, 900) + ", " + Band(100, 700) + " ]"), "movement.slope.up[1]", "abovePermille");
                Refused(Movement("\"up\": [ " + Band(2001, 900) + " ]"), "movement.slope.up[0]", "abovePermille");
                Refused(Movement("\"down\": [ " + Band(100, 0) + " ]"), "movement.slope.down[0]", "speedPermille");
                Refused(Movement("\"down\": [ " + Band(100, 1001) + " ]"), "movement.slope.down[0]", "speedPermille");
                Refused(Movement(""), "movement.slope", "'up'", "'down'");
                Refused(Movement("\"up\": [ ]"), "movement.slope", "up", "0 bands");
                var nine = new string[9];
                for (int i = 0; i < 9; i++) nine[i] = Band(i * 10, 900);
                Refused(Movement("\"down\": [ " + string.Join(", ", nine) + " ]"), "movement.slope", "down", "9 bands");
                Refused(Movement("\"up\": [ " + Band(100, 900) + " ], \"reload\": [ ]"), "movement.slope", "reload");
                Refused("\"movement\": { \"slope\": 5 }", "movement", "slope", "must be an object");
            });
        }

        [Test]
        public void S18_Sections_Refused()
        {
            Assert.DoesNotThrow(() =>
            {
                Refused("\"movement\": { }", "movement {} must not be empty");
                Refused("\"stats\": { }", "stats {} must not be empty");
                Refused("\"movement\": { " + SlopeJson + ", \"wounded\": { \"speedPermille\": 700 } }", "movement", "wounded", "unknown field");
                Refused("\"movement\": { \"reload\": { \"speedPermille\": 700 } }", "movement", "reload", "unknown field");
                Refused("\"stats\": { \"reload\": { \"dragFloorPermille\": 400 } }", "stats", "reload", "unknown stat line");
                Refused("\"stats\": { \"speed\": { } }", "stats 'speed'", "dragFloorPermille", "dragCeilPermille");
                Refused("\"stats\": { \"speed\": { \"dragFloorPermille\": 0 } }", "'speed'", "dragFloorPermille");
                Refused("\"stats\": { \"speed\": { \"dragFloorPermille\": 1001 } }", "'speed'", "dragFloorPermille");
                Refused("\"stats\": { \"speed\": { \"dragCeilPermille\": 999 } }", "'speed'", "dragCeilPermille");
                Refused("\"stats\": { \"speed\": { \"dragCeilPermille\": 4001 } }", "'speed'", "dragCeilPermille");
                Refused("\"stats\": { \"speed\": { \"dragFloorPermille\": 400, \"reload\": 1 } }", "'speed'", "reload");
            });
        }

        [Test]
        public void S18_Accepted()
        {
            Assert.DoesNotThrow(() =>
            {
                ContentPack down = Layer("slope-ok", Movement("\"down\": [ " + Band(400, 800) + " ]"));
                SlopeTable t = down.SectionOrNull<MovementRules>().Slope;
                Assert.That(t.HasUp, Is.False);
                Assert.That(t.SpeedPermille(401, false), Is.EqualTo(800));
                foreach (string stats in new[] { Stats(floor: 1), Stats(floor: 1000), Stats(ceil: 1000), Stats(ceil: 4000), Stats(1000, 1000) })
                    Layer("slope-ok", stats);
                StatClamps row = Layer("slope-ok", Stats(400, 1100)).SectionOrNull<StatClamps>();
                Assert.That(row.FloorOf(StatClamps.Speed), Is.EqualTo(400));
                Assert.That(row.CeilOf(StatClamps.Speed), Is.EqualTo(1100));
                // A spread row alone publishes no speed row.
                Assert.That(Layer("slope-ok", "\"stats\": { \"spread\": { \"dragFloorPermille\": 900 } }").SectionOrNull<StatClamps>().Has(StatClamps.Speed), Is.False);
                // Without the sections nothing is published.
                Assert.That(Plain().SectionOrNull<MovementRules>(), Is.Null);
                Assert.That(Plain().SectionOrNull<StatClamps>(), Is.Null);
            });
        }

        // A child layer's slope (and stats row) replaces the parent's whole.
        [Test]
        public void S18_ChildLayerReplacesTheSlopeWhole()
        {
            Assert.DoesNotThrow(() =>
            {
                var files = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["slope-parent"] = "{ \"id\": \"slope-parent\", \"extends\": \"rwr-field\", " + MovementJson + ", " + Stats(400, 1100) + " }",
                    ["slope-child"] = "{ \"id\": \"slope-child\", \"extends\": \"slope-parent\", " + Movement("\"down\": [ " + Band(200, 600) + " ]")
                        + ", " + Stats(floor: 300) + " }",
                };
                ContentPack child = ContentLoader.Load(new MemoryContentSource(files, FileContentSource.Rwr()), "slope-child");
                SlopeTable t = child.SectionOrNull<MovementRules>().Slope;
                Assert.That(t.HasUp, Is.False);
                Assert.That(t.SpeedPermille(201, false), Is.EqualTo(600));
                StatClamps row = child.SectionOrNull<StatClamps>();
                Assert.That(row.FloorOf(StatClamps.Speed), Is.EqualTo(300));
                Assert.That(row.CeilOf(StatClamps.Speed), Is.EqualTo(0));
            });
        }

        // The shipped layer rwr-slope carries the card's numbers; rwr itself has neither section.
        [Test]
        public void S18_ShippedLayer()
        {
            Assert.DoesNotThrow(() =>
            {
                ContentPack pack = WarCore.Content.Rwr.RwrPack.Load("rwr-slope");
                SlopeTable t = pack.SectionOrNull<MovementRules>().Slope;
                Assert.That(new[] { t.UpAbove(0), t.UpAbove(1), t.UpAbove(2), t.DownAbove(0) }, Is.EqualTo(new[] { 100, 300, 500, 400 }));
                Assert.That(new[] { t.UpSpeed(0), t.UpSpeed(1), t.UpSpeed(2), t.DownSpeed(0) }, Is.EqualTo(new[] { 900, 700, 500, 800 }));
                Assert.That(pack.SectionOrNull<StatClamps>().FloorOf(StatClamps.Speed), Is.EqualTo(400));
                Assert.That(WarCore.Content.Rwr.RwrPack.Load("rwr").SectionOrNull<MovementRules>(), Is.Null);
            });
        }
    }
}
