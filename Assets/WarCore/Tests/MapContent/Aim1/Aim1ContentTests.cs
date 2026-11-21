using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using static WarCore.Tests.Aim1.Aim1Worlds;

namespace WarCore.Tests.Aim1
{
    // AIM-1 A1: reading combat.moveSpread and combat.manualReload, refusing what breaks the rules, and the combat pack digest
    // (two sections appended only when present, so every shipped layer keeps its digest).
    public sealed class Aim1ContentTests
    {
        private const string Example = "{ \"bands\": [ { \"aboveMmPerSec\": 500, \"permille\": 1500 }, { \"aboveMmPerSec\": 4000, \"permille\": 3000 } ], "
            + "\"byWeapon\": { \"rwr.weapon-3\": [ { \"aboveMmPerSec\": 500, \"permille\": 2500 }, { \"aboveMmPerSec\": 4000, \"permille\": 4000 } ] } }";

        // A layer on rwr-field whose combat section is the given members.
        private static ContentPack Layer(string combatMembers) =>
            Load("aim1-case", ("aim1-case", "{ \"id\": \"aim1-case\", \"extends\": \"rwr-field\", \"combat\": { " + combatMembers + " } }"));

        private static ContentPack Spread(string moveSpread) => Layer("\"moveSpread\": " + moveSpread);

        private static string Bands(params (int above, int permille)[] bands)
        {
            var parts = new string[bands.Length];
            for (int i = 0; i < bands.Length; i++) parts[i] = "{ \"aboveMmPerSec\": " + bands[i].above + ", \"permille\": " + bands[i].permille + " }";
            return "[ " + string.Join(", ", parts) + " ]";
        }

        private static string Refused(string combatMembers) =>
            Assert.Throws<ContentException>(() => Layer(combatMembers).BuildCombat()).Message;

        [Test]
        public void A1_TheExampleReadsBandByBand()
        {
            CombatPack pack = Spread(Example).BuildCombat();
            MoveSpread spread = pack.MoveSpread;
            Assert.That(spread, Is.Not.Null);
            Assert.That(pack.ManualReload, Is.False);
            MoveSpreadTable d = spread.Default;
            Assert.That((d.Count, d.AboveMmPerSec(0), d.Permille(0), d.AboveMmPerSec(1), d.Permille(1)), Is.EqualTo((2, 500, 1500, 4000, 3000)));
            Assert.That(spread.WeaponSlots, Is.EqualTo(3));
            Assert.That(spread.For(0), Is.SameAs(d));
            Assert.That(spread.For(1), Is.SameAs(d));
            MoveSpreadTable own = spread.For(2);
            Assert.That((own.Count, own.AboveMmPerSec(0), own.Permille(0), own.AboveMmPerSec(1), own.Permille(1)), Is.EqualTo((2, 500, 2500, 4000, 4000)));
        }

        [Test]
        public void A1_TheShippedAimLayer()
        {
            CombatPack pack = RwrPack.Load("rwr-aim").BuildCombat();
            Assert.That(pack.ManualReload, Is.True);
            Assert.That(pack.Body, Is.EqualTo(RwrPack.Load("rwr-field").BuildCombat().Body), "the body comes from rwr-field");
            MoveSpreadTable d = pack.MoveSpread.Default;
            Assert.That((d.Count, d.AboveMmPerSec(0), d.Permille(0), d.AboveMmPerSec(1), d.Permille(1)), Is.EqualTo((2, 500, 1500, 4000, 3000)));
            Assert.That(pack.MoveSpread.WeaponSlots, Is.Zero);
        }

        [Test]
        public void A1_BandCountAndOrderRules()
        {
            StringAssert.Contains("has 0 bands, allowed 1 to 8", Assert.Throws<ContentException>(() => Spread("{ \"bands\": [] }").BuildCombat()).Message);
            string nine = Bands((0, 1000), (1, 1000), (2, 1000), (3, 1000), (4, 1000), (5, 1000), (6, 1000), (7, 1000), (8, 1000));
            StringAssert.Contains("has 9 bands, allowed 1 to 8", Assert.Throws<ContentException>(() => Spread("{ \"bands\": " + nine + " }").BuildCombat()).Message);
            string eight = Bands((0, 1000), (1, 1000), (2, 1000), (3, 1000), (4, 1000), (5, 1000), (6, 1000), (300000, 4000));
            Assert.That(Spread("{ \"bands\": " + eight + " }").BuildCombat().MoveSpread.Default.Count, Is.EqualTo(8));
            string equal = Refused("\"moveSpread\": { \"bands\": " + Bands((500, 1500), (500, 3000)) + " }");
            StringAssert.Contains("combat.moveSpread.bands[1] field 'aboveMmPerSec': must be greater than the band before (500)", equal);
            StringAssert.Contains("layer 'aim1-case'", equal);
        }

        [TestCase(-1, 1500, "aboveMmPerSec")]
        [TestCase(300001, 1500, "aboveMmPerSec")]
        [TestCase(500, 0, "permille")]
        [TestCase(500, 4001, "permille")]
        public void A1_BandValueRanges(int above, int permille, string field)
        {
            string message = Refused("\"moveSpread\": { \"bands\": " + Bands((above, permille)) + " }");
            StringAssert.Contains("combat.moveSpread.bands[0] field '" + field + "': out of range", message);
        }

        [Test]
        public void A1_BandEdgesAreAllowed()
        {
            MoveSpreadTable t = Spread("{ \"bands\": " + Bands((0, 1), (300000, 4000)) + " }").BuildCombat().MoveSpread.Default;
            Assert.That((t.AboveMmPerSec(0), t.Permille(0), t.AboveMmPerSec(1), t.Permille(1)), Is.EqualTo((0, 1, 300000, 4000)));
        }

        [Test]
        public void A1_ShapeErrors()
        {
            StringAssert.Contains("combat.moveSpread.byWeapon field 'rwr.soldier-1': is not a weapon of this pack",
                Refused("\"moveSpread\": { \"bands\": " + Bands((500, 1500)) + ", \"byWeapon\": { \"rwr.soldier-1\": " + Bands((500, 1500)) + " } }"));
            StringAssert.Contains("combat.moveSpread.byWeapon field 'nope': is not a weapon of this pack",
                Refused("\"moveSpread\": { \"bands\": " + Bands((500, 1500)) + ", \"byWeapon\": { \"nope\": " + Bands((500, 1500)) + " } }"));
            StringAssert.Contains("combat.moveSpread field 'byWeapon': must be an object",
                Refused("\"moveSpread\": { \"bands\": " + Bands((500, 1500)) + ", \"byWeapon\": 3 }"));
            StringAssert.Contains("combat.moveSpread.byWeapon.rwr.weapon-1[0] field 'permille': out of range",
                Refused("\"moveSpread\": { \"bands\": " + Bands((500, 1500)) + ", \"byWeapon\": { \"rwr.weapon-1\": " + Bands((500, 0)) + " } }"));
            StringAssert.Contains("combat.moveSpread is missing field 'bands'", Refused("\"moveSpread\": { }"));
            StringAssert.Contains("combat.moveSpread field 'band': unknown field name", Refused("\"moveSpread\": { \"bands\": " + Bands((500, 1500)) + ", \"band\": 1 }"));
            StringAssert.Contains("combat.moveSpread.bands: must be an array of bands", Refused("\"moveSpread\": { \"bands\": 5 }"));
            StringAssert.Contains("combat.moveSpread.bands[0]: must be an object", Refused("\"moveSpread\": { \"bands\": [ 5 ] }"));
            StringAssert.Contains("combat.moveSpread.bands[0] field 'extra': unknown field name",
                Refused("\"moveSpread\": { \"bands\": [ { \"aboveMmPerSec\": 1, \"permille\": 2, \"extra\": 3 } ] }"));
            StringAssert.Contains("combat.moveSpread.bands[0] is missing field 'permille'", Refused("\"moveSpread\": { \"bands\": [ { \"aboveMmPerSec\": 1 } ] }"));
            StringAssert.Contains("field 'moveSpread' must be a number, string or boolean", Refused("\"moveSpread\": [ 1 ]"));
            StringAssert.Contains("field 'manualReload': must be true or false", Refused("\"manualReload\": 1"));
        }

        [Test]
        public void A1_ManualReloadReads()
        {
            Assert.That(Layer("\"manualReload\": true").BuildCombat().ManualReload, Is.True);
            Assert.That(Layer("\"manualReload\": false").BuildCombat().ManualReload, Is.False);
            Assert.That(Layer("\"manualReload\": true").BuildCombat().MoveSpread, Is.Null);
        }

        [Test]
        public void A1_DigestChangesOnlyWithTheSections()
        {
            ulong field = RwrPack.Load("rwr-field").BuildCombat().PolicyDigest;
            Assert.That(field, Is.EqualTo(0x83B866D46CB9EA79UL), "rwr-field as on main before AIM-1");
            ulong spread = Spread(Example).BuildCombat().PolicyDigest, reload = Layer("\"manualReload\": true").BuildCombat().PolicyDigest;
            ulong both = Layer("\"moveSpread\": " + Example + ", \"manualReload\": true").BuildCombat().PolicyDigest;
            Assert.That(spread, Is.Not.EqualTo(field), "moveSpread changes the digest");
            Assert.That(reload, Is.Not.EqualTo(field), "manualReload: true changes the digest");
            Assert.That(both, Is.Not.EqualTo(spread));
            Assert.That(both, Is.Not.EqualTo(reload));
            Assert.That(Layer("\"manualReload\": false").BuildCombat().PolicyDigest, Is.EqualTo(field), "manualReload: false is no section");
            Assert.That(Spread("{ \"bands\": " + Bands((500, 1500), (4000, 3000)) + " }").BuildCombat().PolicyDigest, Is.Not.EqualTo(spread), "byWeapon is hashed");
            Assert.That(Spread("{ \"bands\": " + Bands((500, 1500), (4000, 3001)) + " }").BuildCombat().PolicyDigest,
                Is.Not.EqualTo(Spread("{ \"bands\": " + Bands((500, 1500), (4000, 3000)) + " }").BuildCombat().PolicyDigest), "a permille is hashed");
            Assert.That(Spread("{ \"bands\": " + Bands((500, 1500), (4001, 3000)) + " }").BuildCombat().PolicyDigest,
                Is.Not.EqualTo(Spread("{ \"bands\": " + Bands((500, 1500), (4000, 3000)) + " }").BuildCombat().PolicyDigest), "a speed is hashed");
            Assert.That(Spread("{ \"bands\": " + Bands((500, 1500)) + " }").BuildCombat().PolicyDigest,
                Is.Not.EqualTo(Spread("{ \"bands\": " + Bands((500, 1500), (4000, 3000)) + " }").BuildCombat().PolicyDigest), "the band count is hashed");
            string oneOwn = "{ \"bands\": " + Bands((500, 1500)) + ", \"byWeapon\": { \"rwr.weapon-1\": " + Bands((500, 1500)) + " } }";
            string otherOwn = "{ \"bands\": " + Bands((500, 1500)) + ", \"byWeapon\": { \"rwr.weapon-2\": " + Bands((500, 1500)) + " } }";
            Assert.That(Spread(oneOwn).BuildCombat().PolicyDigest, Is.Not.EqualTo(Spread(otherOwn).BuildCombat().PolicyDigest), "which weapon owns a table is hashed");
        }

        // Values recorded on main before AIM-1 (Wp15ContentGoldenTests and Proj1DigestTests lock rwr, rwr-m6 and ancient too).
        [TestCase("rwr", 0x8BE7D7DFA58533CDUL)]
        [TestCase("rwr-m6", 0x934F8562D85447D2UL)]
        [TestCase("rwr-melee", 0x74B9EA11CFF86DF2UL)]
        [TestCase("rwr-body", 0x2BF814C85F2DFB14UL)]
        [TestCase("ancient", 0xB02D04256D7204EEUL)]
        public void A1_ShippedLayersKeepTheirDigest(string id, ulong expected)
        {
            ContentPack pack = id == "ancient" ? ContentLoader.Load(new FileContentSource("Assets/WarCore/Content/Ancient/Data"), id) : RwrPack.Load(id);
            CombatPack combat = pack.BuildCombat();
            Assert.That(combat.PolicyDigest, Is.EqualTo(expected));
            Assert.That(combat.MoveSpread, Is.Null);
            Assert.That(combat.ManualReload, Is.False);
        }

        [Test]
        public void A1_WithSelectorKeepsBothSections()
        {
            CombatPack pack = Layer("\"moveSpread\": " + Example + ", \"manualReload\": true").BuildCombat();
            CombatPack copy = pack.WithSelector(pack.Selector);
            Assert.That(copy.MoveSpread, Is.SameAs(pack.MoveSpread));
            Assert.That(copy.ManualReload, Is.True);
            Assert.That(copy.PolicyDigest, Is.EqualTo(pack.PolicyDigest));
        }

        [Test]
        public void A1_APerWeaponTableNeedsOneSlotPerWeapon()
        {
            CombatPack pack = RwrPack.Load("rwr-field").BuildCombat();
            var d = new MoveSpreadTable(new[] { 500 }, new[] { 1500 });
            var weapons = pack.Weapons.ToArray();
            Assert.Throws<ArgumentException>(() => new CombatPack(weapons, pack.Resolver, pack.Selector, pack.MaxHealthRaw, body: pack.Body,
                moveSpread: new MoveSpread(d, new MoveSpreadTable[2])));
            Assert.DoesNotThrow(() => new CombatPack(weapons, pack.Resolver, pack.Selector, pack.MaxHealthRaw, body: pack.Body,
                moveSpread: new MoveSpread(d, new MoveSpreadTable[3])));
            Assert.DoesNotThrow(() => new CombatPack(weapons, pack.Resolver, pack.Selector, pack.MaxHealthRaw, body: pack.Body, moveSpread: new MoveSpread(d)));
        }

        [Test]
        public void A1_AChildLayerReplacesTheWholeTable()
        {
            ContentPack child = Load("aim1-child", ("aim1-child", "{ \"id\": \"aim1-child\", \"extends\": \"aim1-by-weapon\", \"combat\": { \"moveSpread\": { \"bands\": "
                + Bands((1000, 2000)) + " } } }"));
            MoveSpread spread = child.BuildCombat().MoveSpread;
            Assert.That(spread.WeaponSlots, Is.Zero, "the parent's byWeapon is gone with the parent's object");
            Assert.That((spread.Default.Count, spread.Default.AboveMmPerSec(0), spread.Default.Permille(0)), Is.EqualTo((1, 1000, 2000)));
            Assert.That(child.BuildCombat().ManualReload, Is.True, "a sibling field of the parent stays");
        }
    }
}
