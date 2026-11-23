using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using static WarCore.Tests.Scale2bFixtures;

namespace WarCore.Tests
{
    // SCALE-2b acceptance SC1-SC8, SC12 and SC14: the optional "scale" section, its reading, its conversion to raw and its
    // relations against the pack's body and stances.
    public sealed class Scale2bSectionTests
    {
        // SC1: no shipped layer but rwr-scale writes the section; the others publish nothing and Of gives Default.
        [Test]
        public void SC1_OldLayersPublishNothingAndGetTheDefault()
        {
            ContentCatalog catalog = ContentCatalog.Find();
            int checkedLayers = 0;
            foreach (string cartridge in catalog.Cartridges)
                foreach (string id in catalog.Layers(cartridge))
                {
                    if (id == "rwr-scale") continue;
                    ContentPack pack = null;
                    Assert.DoesNotThrow(() => pack = catalog.Load(id), id);
                    Assert.That(pack.SectionOrNull<ScaleRules>(), Is.Null, id);
                    Assert.That(ScaleRules.Of(pack), Is.SameAs(ScaleRules.Default), id);
                    checkedLayers++;
                }
            Assert.That(checkedLayers, Is.GreaterThan(10));
        }

        // SC2: the default window equals the prefab's default parameters, and Default passes every relation on a 3/4 layer
        // with stances and a 2/1 layer without.
        [Test]
        public void SC2_DefaultMatchesThePrefabAndPassesEveryRelation()
        {
            Assert.That(ScaleRules.Default.WindowSillRaw, Is.EqualTo(MapPrefabs.DefaultSillRaw));
            Assert.That(ScaleRules.Default.WindowTopRaw, Is.EqualTo(MapPrefabs.DefaultOpeningRaw));
            ContentPack stance = Shipped("rwr-stance"), body = Shipped("rwr-body");
            Assert.That(stance.Terrain.MaxWalkableNum * 4, Is.EqualTo(3 * stance.Terrain.MaxWalkableDen), "rwr-stance walks 3/4");
            Assert.That(body.Terrain, Is.Null, "rwr-body has the height field's 2/1");
            Assert.That(ScaleRules.Problems(ScaleRules.Default, stance.Body.Value, stance.BuildCombat().Stances.ToArray(), 3, 4, null), Is.Empty);
            Assert.That(ScaleRules.Problems(ScaleRules.Default, body.Body.Value, body.BuildCombat().Stances.ToArray(), 2, 1, null), Is.Empty);
        }

        // SC3: rwr-scale reads with the thirteen numbers; odd raw values of the five wall and storey heights grow 1 raw, the
        // other three stay odd.
        [Test]
        public void SC3_ShippedNumbersAndEvenPadding()
        {
            ScaleRules s = null;
            Assert.DoesNotThrow(() => s = Shipped("rwr-scale").SectionOrNull<ScaleRules>());
            Assert.That(s, Is.Not.Null);
            Assert.That(new[] { s.StoreyMm, s.PlateMm, s.LowWallMm, s.ChestWallMm, s.HighWallMm, s.WindowSillMm, s.WindowTopMm, s.RailingMm,
                s.StairRiseMm, s.StairRunMm, s.LaneMm, s.SidewalkMm, s.AlleyMm },
                Is.EqualTo(new[] { 3000, 500, 1000, 1400, 2500, 900, 2000, 1000, 175, 280, 3250, 2000, 3000 }));
            Assert.That(new[] { s.StoreyRaw, s.PlateRaw, s.LowWallRaw, s.ChestWallRaw, s.HighWallRaw, s.WindowSillRaw, s.WindowTopRaw,
                s.RailingRaw, s.LaneRaw, s.SidewalkRaw, s.AlleyRaw },
                Is.EqualTo(new[] { 196608L, 32768, 65536, 91750, 163840, 58982, 131072, 65536, 212992, 131072, 196608 }));

            ScaleRules odd = Rules("rwr-stance", ("lowWallMm", 1001), ("chestWallMm", 1401), ("highWallMm", 2501), ("railingMm", 1001),
                ("storeyMm", 3001), ("plateMm", 501), ("windowSillMm", 901), ("windowTopMm", 2001));
            Assert.That(odd.LowWallRaw, Is.EqualTo(65602));
            Assert.That(odd.ChestWallRaw, Is.EqualTo(91816));
            Assert.That(odd.HighWallRaw, Is.EqualTo(163906));
            Assert.That(odd.RailingRaw, Is.EqualTo(65602));
            Assert.That(odd.StoreyRaw, Is.EqualTo(196674));
            Assert.That(odd.PlateRaw, Is.EqualTo(32833));
            Assert.That(odd.WindowSillRaw, Is.EqualTo(59047));
            Assert.That(odd.WindowTopRaw, Is.EqualTo(131137));
        }

        // SC4: format errors name the field; a child layer changes one field and inherits the rest.
        [TestCase("storeyMm", null, "is missing field 'storeyMm'")]
        [TestCase("doorMm", "900", "field 'doorMm': unknown field name")]
        [TestCase("plateMm", "0", "field 'plateMm': out of range [1, 100000]")]
        [TestCase("alleyMm", "100001", "field 'alleyMm': out of range [1, 100000]")]
        [TestCase("lowWallMm", "1000.5", "field 'lowWallMm': must be an integer")]
        [TestCase("railingMm", "\"1000\"", "field 'railingMm': must be an integer")]
        public void SC4_FormatErrorsNameTheField(string field, string value, string expected)
        {
            string message = Refused(Layer("scale2b-test", "rwr-stance", ScaleText((field, value))));
            Assert.That(message, Does.Contain("layer 'scale2b-test' scale"));
            Assert.That(message, Does.Contain(expected));
        }

        [Test]
        public void SC4_ChildLayerChangesOneField()
        {
            string parent = Layer("scale2b-parent", "rwr-stance", ScaleJson(("windowTopMm", 2100)));
            ScaleRules s = Loads(Layer("scale2b-child", "scale2b-parent", "\"scale\": { \"storeyMm\": 3500 }"), parent).SectionOrNull<ScaleRules>();
            Assert.That(s.StoreyMm, Is.EqualTo(3500));
            Assert.That(s.StoreyRaw, Is.EqualTo(229376));
            Assert.That(s.WindowTopMm, Is.EqualTo(2100));
            Assert.That(s.PlateMm, Is.EqualTo(500));
            Assert.That(s.AlleyMm, Is.EqualTo(3000));
        }

        // SC5: a layer without a body cannot write the section.
        [Test]
        public void SC5_ScaleNeedsABody()
        {
            string message = Refused(Layer("scale2b-test", "rwr-m6", ScaleJson()));
            Assert.That(message, Does.Contain("scale needs the combat section's body"));
            Assert.That(message, Does.StartWith("content pack 'scale2b-test' layer 'scale2b-test' scale: "));
        }

        // SC6: low wall within [L + r, min(E, M) - r]; L is the highest eye or muzzle of every stance, 2r - r without stances.
        [Test]
        public void SC6_LowWall()
        {
            Accepts("rwr-stance", ("lowWallMm", 1000));
            RefusesOnly("lowWallMm", "rwr-stance", ("lowWallMm", 999));
            Accepts("rwr-stance", ("lowWallMm", 1250));
            RefusesOnly("lowWallMm", "rwr-stance", ("lowWallMm", 1251));
            Accepts("rwr-body", ("lowWallMm", 500));
            RefusesOnly("lowWallMm", "rwr-body", ("lowWallMm", 499));

            string muzzleAbove = Layer("scale2b-muzzle", "rwr-body", "\"combat\": { \"stances\": " + Stances(("1.0", "0.5", "0.75")) + " }");
            Assert.That(Loads(Layer("scale2b-test", "scale2b-muzzle", ScaleJson(("lowWallMm", 1000))), muzzleAbove).SectionOrNull<ScaleRules>(), Is.Not.Null);
            Assert.That(Fields(Refused(Layer("scale2b-test", "scale2b-muzzle", ScaleJson(("lowWallMm", 999))), muzzleAbove)), Is.EqualTo(new[] { "lowWallMm" }));

            string laterHigher = Layer("scale2b-later", "rwr-body", "\"combat\": { \"stances\": " + Stances(("1.2", "0.5", "0.5"), ("1.0", "0.75", "0.75")) + " }");
            Assert.That(Loads(Layer("scale2b-test", "scale2b-later", ScaleJson(("lowWallMm", 1000))), laterHigher).SectionOrNull<ScaleRules>(), Is.Not.Null);
            Assert.That(Fields(Refused(Layer("scale2b-test", "scale2b-later", ScaleJson(("lowWallMm", 999))), laterHigher)), Is.EqualTo(new[] { "lowWallMm" }));
        }

        // SC7: chest wall above the low wall and below min(E, M).
        [Test]
        public void SC7_ChestWall()
        {
            Accepts("rwr-stance", ("chestWallMm", 1499));
            RefusesOnly("chestWallMm", "rwr-stance", ("chestWallMm", 1500));
            RefusesOnly("chestWallMm", "rwr-stance", ("chestWallMm", 1000));
            Accepts("rwr-stance", ("chestWallMm", 1001));
        }

        // SC7b: eye and muzzle differ (1.5 / 1.4 and 1.4 / 1.5): min(E, M) and max(E, M) are both read.
        [TestCase("1.5", "1.4")]
        [TestCase("1.4", "1.5")]
        public void SC7b_EyeAndMuzzleDiffer(string eye, string muzzle)
        {
            string body = Layer("scale2b-body", "rwr-body", Body("1.75", eye, muzzle));
            void Accepted(string field, int mm) =>
                Assert.That(Loads(Layer("scale2b-test", "scale2b-body", ScaleJson(("chestWallMm", 1399), (field, mm))), body).SectionOrNull<ScaleRules>(),
                    Is.Not.Null, field + " " + mm);
            void RefusedOnly(string field, int mm) =>
                Assert.That(Fields(Refused(Layer("scale2b-test", "scale2b-body", ScaleJson(("chestWallMm", 1399), (field, mm))), body)),
                    Is.EqualTo(new[] { field }), field + " " + mm);
            Accepted("lowWallMm", 1150); RefusedOnly("lowWallMm", 1151);
            Accepted("chestWallMm", 1399); RefusedOnly("chestWallMm", 1400);
            Accepted("windowSillMm", 1149); RefusedOnly("windowSillMm", 1150);
            Accepted("windowTopMm", 1750); RefusedOnly("windowTopMm", 1749);
            Accepted("railingMm", 1150); RefusedOnly("railingMm", 1151);
        }

        // r is the pack's body radius (HC-1): a 0.2 m body moves the bounds that read r (2r = 26214, min(E, M) - r = 85197,
        // max(E, M) + r = 111411 raw), past where the 0.25 m default would put them.
        [Test]
        public void SC7c_BoundsReadTheBodyRadius()
        {
            string thin = Layer("scale2b-thin", "rwr-body", "\"combat\": { \"body\": { \"height\": 1.75, \"eye\": 1.5, \"muzzle\": 1.5, \"radius\": 0.2 } }");
            Assert.That(Loads(thin).Body.Value.Radius.Raw, Is.EqualTo(13107));
            void Accepted(string field, int mm) =>
                Assert.That(Loads(Layer("scale2b-test", "scale2b-thin", ScaleJson((field, mm))), thin).SectionOrNull<ScaleRules>(), Is.Not.Null, field + " " + mm);
            void RefusedOnly(string field, int mm) =>
                Assert.That(Fields(Refused(Layer("scale2b-test", "scale2b-thin", ScaleJson((field, mm))), thin)), Is.EqualTo(new[] { field }), field + " " + mm);
            Accepted("lowWallMm", 400); RefusedOnly("lowWallMm", 399);
            Accepted("lowWallMm", 1300); RefusedOnly("lowWallMm", 1301);
            Accepted("windowSillMm", 400); RefusedOnly("windowSillMm", 399);
            Accepted("windowSillMm", 1300); RefusedOnly("windowSillMm", 1301);
            Accepted("windowTopMm", 1700); RefusedOnly("windowTopMm", 1699);
        }

        // SC8: the high wall is at least the standing height.
        [Test]
        public void SC8_HighWall()
        {
            Accepts("rwr-stance", ("highWallMm", 1750));
            RefusesOnly("highWallMm", "rwr-stance", ("highWallMm", 1749));
        }

        // SC12: railing within [2r, min(E, M) - r]; each street width at least one cell.
        [Test]
        public void SC12_RailingAndStreets()
        {
            Accepts("rwr-stance", ("railingMm", 500));
            RefusesOnly("railingMm", "rwr-stance", ("railingMm", 499));
            Accepts("rwr-stance", ("railingMm", 1250));
            RefusesOnly("railingMm", "rwr-stance", ("railingMm", 1251));
            foreach (string street in new[] { "laneMm", "sidewalkMm", "alleyMm" })
            {
                Accepts("rwr-stance", (street, 1000));
                RefusesOnly(street, "rwr-stance", (street, 999));
            }
        }

        // SC14: a short body (1.3 / 1.05 / 1.0, eye exactly H - r) with the rwr-scale numbers: one exception lists every
        // failing row in table order, on a 2/1 and a 3/4 layer alike.
        [Test]
        public void SC14_EveryFailureInOneMessage()
        {
            var messages = new List<string>();
            foreach (string parent in new[] { "rwr-body", "rwr-field" })
            {
                string body = Layer("scale2b-short", parent, Body("1.3", "1.05", "1.0"));
                Assert.That(Loads(body).Body.Value.Eye.Raw, Is.EqualTo(Loads(body).Body.Value.Height.Raw - R), "the eye is at H - r");
                string message = Refused(Layer("scale2b-test", "scale2b-short", ScaleJson()), body);
                Assert.That(Fields(message), Is.EqualTo(new[] { "lowWallMm", "chestWallMm", "windowSillMm", "railingMm" }), parent);
                messages.Add(message);
            }
            Assert.That(messages[1], Is.EqualTo(messages[0]));
            TestContext.WriteLine(messages[0]);
        }
    }
}
