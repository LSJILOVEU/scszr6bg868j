using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    // A layer may extend several layers. Each is merged once. Of the layers that set one field, those that no other of them
    // extends must agree: the order of the list decides no value, nor whether the pack loads.
    public sealed class ContentComposeTests
    {
        private const string W1 = "rwr.weapon-1", W2 = "rwr.weapon-2";
        private const string Body = "\"height\": 1.75, \"eye\": 1.5, \"muzzle\": 1.5";
        private const string Band = "{ \"aboveMmPerSec\": 500, \"permille\": 1500 }";

        private static readonly Dictionary<string, string> Parts = new Dictionary<string, string>
        {
            ["mag7"] = OnRwr("mag7", "\"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 7 } ]"),
            ["mag7-again"] = OnRwr("mag7-again", "\"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 7 } ]"),
            ["mag8"] = OnRwr("mag8", "\"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 8 } ]"),
            ["mag30"] = OnRwr("mag30", "\"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 30 } ]"),
            ["second9"] = OnRwr("second9", "\"weapons\": [ { \"id\": \"" + W2 + "\", \"magazine\": 9 } ]"),
            ["scout"] = OnRwr("scout", "\"unitTypes\": [ { \"id\": \"x.scout\", \"weapon\": \"" + W1 + "\" } ]"),
            ["sniper"] = OnRwr("sniper", "\"unitTypes\": [ { \"id\": \"x.sniper\", \"weapon\": \"" + W2 + "\" } ]"),
            ["respawn"] = Part("respawn", "\"match\": { \"respawnTicks\": 150 }"),
            ["slow"] = Part("slow", "\"match\": { \"respawnTicks\": 300 }"),
            ["fast"] = "{ \"id\": \"fast\", \"extends\": \"slow\", \"match\": { \"respawnTicks\": 150 } }",
            ["faster"] = "{ \"id\": \"faster\", \"extends\": \"slow\", \"match\": { \"respawnTicks\": 100 } }",
            ["body"] = Part("body", "\"combat\": { \"body\": { " + Body + " } }"),
            ["body-reordered"] = Part("body-reordered", "\"combat\": { \"body\": { \"muzzle\": 1.5, \"eye\": 1.5, \"height\": 1.75 } }"),
            ["body-taller"] = Part("body-taller", "\"combat\": { \"body\": { \"height\": 1.8, \"eye\": 1.5, \"muzzle\": 1.5 } }"),
            ["body-wide"] = Part("body-wide", "\"combat\": { \"body\": { " + Body + ", \"radius\": 0.25 } }"),
            ["body-zero"] = Part("body-zero", "\"combat\": { \"body\": { \"height\": 1.750, \"eye\": 1.5, \"muzzle\": 1.5 } }"),
            ["body-text"] = Part("body-text", "\"combat\": { \"body\": { \"height\": 1.75, \"eye\": \"1.5\", \"muzzle\": 1.5 } }"),
            ["band"] = Part("band", "\"combat\": { \"moveSpread\": { \"bands\": [ " + Band + " ] } }"),
            ["band-steep"] = Part("band-steep", "\"combat\": { \"moveSpread\": { \"bands\": [ { \"aboveMmPerSec\": 500, \"permille\": 1600 } ] } }"),
            ["bands"] = Part("bands", "\"combat\": { \"moveSpread\": { \"bands\": [ " + Band + ", { \"aboveMmPerSec\": 4000, \"permille\": 3000 } ] } }"),
            ["fire-on"] = Part("fire-on", "\"combat\": { \"friendlyFire\": true }"),
            ["fire-off"] = Part("fire-off", "\"combat\": { \"friendlyFire\": false }"),
        };

        private static string Part(string id, string body) => "{ \"id\": \"" + id + "\", " + body + " }";
        private static string OnRwr(string id, string body) => "{ \"id\": \"" + id + "\", \"extends\": \"rwr\", " + body + " }";
        private static string Top(string extends, string body = null) => "{ \"id\": \"top\", \"extends\": " + extends + (body == null ? "" : ", " + body) + " }";

        private static IContentSource Source(string top, KeyValuePair<string, string>[] more)
        {
            var layers = new Dictionary<string, string>(Parts) { ["top"] = top };
            foreach (var pair in more) layers[pair.Key] = pair.Value;
            return new MemoryContentSource(layers, FileContentSource.Rwr());
        }

        // The pack, after checking that it loads at all: a refusal fails the test where it is made.
        private static ContentPack Load(string top, params KeyValuePair<string, string>[] more)
        {
            ContentPack pack = null;
            Assert.That(() => pack = ContentLoader.Load(Source(top, more), "top"), Throws.Nothing);
            return pack;
        }

        private static string Refusal(string top, params KeyValuePair<string, string>[] more) =>
            Assert.Throws<ContentException>(() => ContentLoader.Load(Source(top, more), "top")).Message;

        private static long Magazine(ContentPack pack, string weapon) => pack.BuildCombat().Weapons[pack.WeaponIndex(weapon)].Magazine;

        private static string Differ(string field, string first, string second) =>
            "content pack 'top' field '" + field + "': layers '" + first + "' and '" + second
            + "' set it differently and neither extends the other; set it in a layer that extends both";

        [Test]
        public void ALayerTakesEveryLayerItLists()
        {
            ContentPack pack = Load(Top("[\"mag7\", \"second9\", \"respawn\"]"));
            Assert.That(Magazine(pack, W1), Is.EqualTo(7));
            Assert.That(Magazine(pack, W2), Is.EqualTo(9));
            Assert.That(pack.Match.RespawnTicks, Is.EqualTo(150));
            Assert.That(pack.Match.CaptureTicks, Is.EqualTo(300), "the base they share is still there");
        }

        [Test]
        public void ALayerReachedTwiceIsMergedOnce()
        {
            // Merged a second time after mag7, the base would put the magazine back to 30.
            ContentPack pack = Load(Top("[\"mag7\", \"second9\"]"));
            Assert.That(Magazine(pack, W1), Is.EqualTo(7));
            Assert.That(pack.WeaponCount, Is.EqualTo(3));
        }

        [TestCase("[\"mag7\", \"second9\", \"respawn\"]")]
        [TestCase("[\"respawn\", \"second9\", \"mag7\"]")]
        [TestCase("[\"second9\", \"rwr\", \"respawn\", \"mag7\"]")]
        public void TheOrderOfTheListDecidesNoValue(string extends)
        {
            ContentPack pack = Load(Top(extends));
            Assert.That(Magazine(pack, W1), Is.EqualTo(7));
            Assert.That(Magazine(pack, W2), Is.EqualTo(9));
            Assert.That(pack.Match.RespawnTicks, Is.EqualTo(150));
        }

        [Test]
        public void NewEntriesStandInTheOrderOfTheList()
        {
            ContentPack pack = Load(Top("[\"scout\", \"sniper\"]"));
            Assert.That(new[] { pack.UnitTypeId(2), pack.UnitTypeId(3) }, Is.EqualTo(new[] { "x.scout", "x.sniper" }));
            pack = Load(Top("[\"sniper\", \"scout\"]"));
            Assert.That(new[] { pack.UnitTypeId(2), pack.UnitTypeId(3) }, Is.EqualTo(new[] { "x.sniper", "x.scout" }));
        }

        [Test]
        public void TwoLayersThatSetAFieldDifferentlyAreRefused()
        {
            Assert.That(Refusal(Top("[\"mag7\", \"mag8\"]")), Is.EqualTo(Differ("weapons[" + W1 + "].magazine", "mag7", "mag8")));
            Assert.That(Refusal(Top("[\"mag8\", \"mag7\"]")), Is.EqualTo(Differ("weapons[" + W1 + "].magazine", "mag8", "mag7")));
        }

        [Test]
        public void ALayerMayNotQuietlyPutBackTheValueAnotherChanged()
        {
            // mag30 writes what the base says. Listed after mag7 it would undo mag7 without extending it.
            Assert.That(Refusal(Top("[\"mag7\", \"mag30\"]")), Is.EqualTo(Differ("weapons[" + W1 + "].magazine", "mag7", "mag30")));
            Assert.That(Refusal(Top("[\"mag30\", \"mag7\"]")), Is.EqualTo(Differ("weapons[" + W1 + "].magazine", "mag30", "mag7")));
        }

        [Test]
        public void ALayerOverriddenInItsOwnLineIsNotCompared()
        {
            // fast extends slow and overrides it, so what is left is fast and respawn: they agree, in either order.
            Assert.That(Load(Top("[\"rwr\", \"fast\", \"respawn\"]")).Match.RespawnTicks, Is.EqualTo(150));
            Assert.That(Load(Top("[\"rwr\", \"respawn\", \"fast\"]")).Match.RespawnTicks, Is.EqualTo(150));
            // When the two that are left disagree it is refused in either order, and slow is not the one named.
            Assert.That(Refusal(Top("[\"rwr\", \"faster\", \"respawn\"]")), Is.EqualTo(Differ("match.respawnTicks", "faster", "respawn")));
            Assert.That(Refusal(Top("[\"rwr\", \"respawn\", \"faster\"]")), Is.EqualTo(Differ("match.respawnTicks", "respawn", "faster")));
        }

        [Test]
        public void TheLayerThatExtendsBothSettlesIt()
        {
            ContentPack pack = Load(Top("[\"mag7\", \"mag8\"]", "\"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 8 } ]"));
            Assert.That(Magazine(pack, W1), Is.EqualTo(8));
            // A layer in between settles it as well: the top layer only has to extend the one that did.
            var settled = new KeyValuePair<string, string>("settled",
                "{ \"id\": \"settled\", \"extends\": [\"mag7\", \"mag8\"], \"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 12 } ] }");
            Assert.That(Magazine(Load(Top("[\"settled\", \"second9\"]"), settled), W1), Is.EqualTo(12));
        }

        [Test]
        public void ALayerOverridesEveryLayerBelowIt()
        {
            var child = new KeyValuePair<string, string>("child", "{ \"id\": \"child\", \"extends\": \"mag7\", \"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 11 } ] }");
            Assert.That(Magazine(Load(Top("[\"child\", \"mag7\"]"), child), W1), Is.EqualTo(11), "mag7 is merged once, under its child");
            Assert.That(Magazine(Load(Top("[\"mag7\", \"child\"]"), child), W1), Is.EqualTo(11));
            // The base is two steps below this one, through a layer that leaves the magazine alone.
            var far = new KeyValuePair<string, string>("far", "{ \"id\": \"far\", \"extends\": \"second9\", \"weapons\": [ { \"id\": \"" + W1 + "\", \"magazine\": 5 } ] }");
            Assert.That(Magazine(Load(Top("\"far\""), far), W1), Is.EqualTo(5));
        }

        [Test]
        public void TheSameValueTwiceIsNoDisagreement()
        {
            Assert.That(Magazine(Load(Top("[\"mag7\", \"mag7-again\"]")), W1), Is.EqualTo(7));
            Assert.That(Load(Top("[\"rwr\", \"body\", \"body-reordered\"]")).Body.HasValue, Is.True, "the same members in another order are the same value");
        }

        // A value is compared whole and as it is written.
        [TestCase("body", "body-taller", "combat.body", TestName = "OneMemberDiffers")]
        [TestCase("body", "body-wide", "combat.body", TestName = "OneMemberMore")]
        [TestCase("band", "band-steep", "combat.moveSpread", TestName = "OneItemDiffers")]
        [TestCase("band", "bands", "combat.moveSpread", TestName = "OneItemMore")]
        [TestCase("fire-on", "fire-off", "combat.friendlyFire", TestName = "TrueAndFalse")]
        [TestCase("body", "body-zero", "combat.body", TestName = "ATrailingZero")]
        [TestCase("body", "body-text", "combat.body", TestName = "ANumberAndItsText")]
        public void ValuesThatDifferAreRefused(string first, string second, string field) =>
            Assert.That(Refusal(Top("[\"rwr\", \"" + first + "\", \"" + second + "\"]")), Is.EqualTo(Differ(field, first, second)));

        [Test]
        public void ABadListIsRefused()
        {
            Assert.That(Refusal(Top("[]")), Is.EqualTo("content layer 'top' field 'extends' lists no layer"));
            Assert.That(Refusal(Top("[\"rwr\", \"mag7\", \"rwr\"]")), Is.EqualTo("content layer 'top' field 'extends' lists 'rwr' twice"));
            Assert.That(Refusal(Top("[\"rwr\", 5]")), Is.EqualTo("content layer 'top' field 'extends' must list layer ids (line 1)"));
            Assert.That(Refusal(Top("[\"rwr\", \"nowhere\"]")), Does.StartWith("content layer 'nowhere' not found (extended by 'top'), looked at: "));
        }

        [Test]
        public void ACycleThroughAListIsRefused()
        {
            var back = new KeyValuePair<string, string>("back", "{ \"id\": \"back\", \"extends\": [\"mag7\", \"top\"] }");
            Assert.That(Refusal(Top("[\"rwr\", \"back\"]"), back), Is.EqualTo("content layer inheritance cycle: top -> back -> top"));
        }

        [Test]
        public void TheShippedPartsAreNotLayersOfTheirCartridge()
        {
            ContentCatalog catalog = ContentCatalog.Find();
            string[] parts = Directory.GetFiles(Path.Combine(catalog.DirectoryOf("Rwr"), "parts"), "*.json");
            Assert.That(parts, Is.Not.Empty);
            foreach (string file in parts)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                Assert.That(catalog.Layers("Rwr"), Has.No.Member(name).And.No.Member("parts/" + name));
            }
            // A part is reached through the layer that lists it, by the id that names its folder.
            Assert.That(catalog.Load("rwr-showcase-house").Body.HasValue, Is.True);
        }
    }
}
