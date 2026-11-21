using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests.Stance
{
    // STANCE-1 S1 and S2: combat.stances read from content (the card's example, every refusal naming its field) and the pack
    // digest (stances in, every number and the deny list in it, rwr-field unchanged, WithSelector keeps them).
    public sealed class Stance1ContentTests
    {
        internal const string Crouch = "{ \"id\": \"crouch\", \"height\": 1.0, \"eye\": 0.75, \"muzzle\": 0.75, \"speedPermille\": 500, \"spreadPermille\": 700, \"ticks\": 6 }";
        internal const string Prone = "{ \"id\": \"prone\", \"height\": 0.5, \"eye\": 0.25, \"muzzle\": 0.25, \"speedPermille\": 250, \"spreadPermille\": 400, \"ticks\": 15, \"deny\": [\"dash\", \"traverse\"] }";

        // A layer over rwr-field (or another parent) whose combat section writes these stances (JSON array items).
        internal static ContentPack Load(string items, string parent = "rwr-field", string extra = "") =>
            Ml3Content.Load("stance1", Ml3Content.Layer("stance1", parent, "\"combat\": { " + extra + "\"stances\": [" + items + "] }"));

        internal static string Item(string id = "crouch", string height = "1.0", string eye = "0.75", string muzzle = "0.75", string speed = "500",
            string spread = "700", string ticks = "6", string deny = null) =>
            "{ \"id\": \"" + id + "\", \"height\": " + height + ", \"eye\": " + eye + ", \"muzzle\": " + muzzle + ", \"speedPermille\": " + speed
            + ", \"spreadPermille\": " + spread + ", \"ticks\": " + ticks + (deny == null ? "" : ", \"deny\": " + deny) + " }";

        private static void Refused(string items, string field, string parent = "rwr-field")
        {
            ContentException e = Assert.Throws<ContentException>(() => Load(items, parent));
            StringAssert.Contains("'" + field + "'", e.Message);
        }

        private static StanceDef[] Accepted(string items)
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = Load(items));
            return pack.BuildCombat().Stances.ToArray();
        }

        // S1: the card's example (the shipped layer rwr-stance): six raw heights, the factors, the ticks and the deny lists.
        [Test]
        public void S1_TheShippedLayerReadsAsTheCardSays()
        {
            CombatPack pack = RwrPack.Load("rwr-stance").BuildCombat();
            Assert.That(pack.Body.Value.Height.Raw, Is.EqualTo(114688L));
            StanceDef[] s = pack.Stances.ToArray();
            Assert.That(s.Length, Is.EqualTo(2));
            Assert.That((s[0].Id, s[0].Body.Height.Raw, s[0].Body.Eye.Raw, s[0].Body.Muzzle.Raw), Is.EqualTo(("crouch", 65536L, 49152L, 49152L)));
            Assert.That((s[1].Id, s[1].Body.Height.Raw, s[1].Body.Eye.Raw, s[1].Body.Muzzle.Raw), Is.EqualTo(("prone", 32768L, 16384L, 16384L)));
            Assert.That((s[0].SpeedPermille, s[0].SpreadPermille, s[0].Ticks), Is.EqualTo((500, 700, 6)));
            Assert.That((s[1].SpeedPermille, s[1].SpreadPermille, s[1].Ticks), Is.EqualTo((250, 400, 15)));
            // crouch writes no deny: the default; prone writes it out.
            Assert.That(s[0].Deny, Is.EqualTo(UnitAction.Dash | UnitAction.Traverse));
            Assert.That(s[1].Deny, Is.EqualTo(UnitAction.Dash | UnitAction.Traverse));
        }

        [Test]
        public void S1_DenyNamesAndTheEmptyList()
        {
            Assert.That(Accepted(Item(deny: "[]"))[0].Deny, Is.EqualTo(UnitAction.None));
            Assert.That(Accepted(Item(deny: "[\"fire\", \"melee\", \"block\", \"throw\", \"reload\", \"interact\", \"capture\"]"))[0].Deny,
                Is.EqualTo(UnitAction.Fire | UnitAction.Melee | UnitAction.Block | UnitAction.Throw | UnitAction.Reload | UnitAction.Interact | UnitAction.Capture));
            Assert.That(Accepted(Item(deny: "[\"traverse\"]"))[0].Deny, Is.EqualTo(UnitAction.Traverse));
        }

        // A child layer that writes stances replaces the parent's whole array; one that does not keeps it, and the body with it.
        [Test]
        public void S1_AChildLayerReplacesTheWholeArray()
        {
            ContentPack child = Ml3Content.Load("stance1b", Ml3Content.Layer("stance1b", "rwr-stance", "\"combat\": { \"stances\": [" + Item("kneel", "1.2", "0.9", "0.9") + "] }"));
            StanceDef[] s = child.BuildCombat().Stances.ToArray();
            Assert.That(s.Length, Is.EqualTo(1));
            Assert.That(s[0].Id, Is.EqualTo("kneel"));
            ContentPack plain = Ml3Content.Load("stance1c", Ml3Content.Layer("stance1c", "rwr-stance", "\"combat\": { \"maxHealth\": 100 }"));
            Assert.That(plain.BuildCombat().Stances.Length, Is.EqualTo(2));
            Assert.That(plain.Body.Value.Height.Raw, Is.EqualTo(114688L));
        }

        [Test]
        public void S1_NoBody() => Refused(Crouch, "stances", "rwr");

        [Test]
        public void S1_NoBodyMessageNamesTheBody()
        {
            ContentException e = Assert.Throws<ContentException>(() => Load(Crouch, "rwr"));
            StringAssert.Contains("body", e.Message);
        }

        [Test]
        public void S1_Count()
        {
            Refused("", "stances");
            var seven = new List<string>();
            string[] heights = { "1.7", "1.5", "1.3", "1.1", "0.9", "0.7", "0.5" };
            for (int i = 0; i < 7; i++) seven.Add(Item("s" + i, heights[i], "0.25", "0"));
            Assert.That(Accepted(string.Join(", ", seven)).Length, Is.EqualTo(7));
            Assert.That(Accepted(Crouch).Length, Is.EqualTo(1));
            seven.Add(Item("s7", "0.5", "0.25", "0"));
            Refused(string.Join(", ", seven), "stances");
        }

        [Test]
        public void S1_NotAnArrayOrNotAnObject()
        {
            Assert.Throws<ContentException>(() => Ml3Content.Load("stance1", Ml3Content.Layer("stance1", "rwr-field", "\"combat\": { \"stances\": 3 }")));
            Refused("3", "stances");
        }

        [Test]
        public void S1_Ids()
        {
            Refused(Item("crouch") + ", " + Item("crouch", "0.5", "0.25", "0.25"), "id");
            Refused(Item(""), "id");
        }

        [Test]
        public void S1_Heights()
        {
            Refused(Item(height: "1.75"), "height");
            Assert.That(Accepted(Item(height: "1.74998", eye: "1", muzzle: "1"))[0].Body.Height.Raw, Is.EqualTo(114686L));
            Refused(Crouch + ", " + Item("prone", "1.0", "0.25", "0.25"), "height");
            Refused(Item(height: "0.49999", eye: "0.24", muzzle: "0"), "height");
            Refused(Crouch + ", " + Item("prone", eye: "0.3", height: "0.5", muzzle: "0.25"), "eye");
            Refused(Item(eye: "0.24"), "eye");
            Refused(Item(muzzle: "1.00002"), "muzzle");
            Assert.That(Accepted(Item(eye: "0.25", muzzle: "0"))[0].Body.Muzzle.Raw, Is.EqualTo(0L));
            Assert.That(Accepted(Item(eye: "0.75", muzzle: "1.0"))[0].Body.Muzzle.Raw, Is.EqualTo(65536L));
        }

        [TestCase("speed", "0", false)]
        [TestCase("speed", "1", true)]
        [TestCase("speed", "1000", true)]
        [TestCase("speed", "1001", false)]
        [TestCase("spread", "0", false)]
        [TestCase("spread", "1", true)]
        [TestCase("spread", "4000", true)]
        [TestCase("spread", "4001", false)]
        [TestCase("ticks", "0", false)]
        [TestCase("ticks", "1", true)]
        [TestCase("ticks", "300", true)]
        [TestCase("ticks", "301", false)]
        public void S1_Numbers(string which, string value, bool ok)
        {
            string item = which == "speed" ? Item(speed: value) : which == "spread" ? Item(spread: value) : Item(ticks: value);
            string field = which == "speed" ? "speedPermille" : which == "spread" ? "spreadPermille" : "ticks";
            if (!ok) { Refused(item, field); return; }
            StanceDef s = Accepted(item)[0];
            Assert.That(which == "speed" ? s.SpeedPermille : which == "spread" ? s.SpreadPermille : s.Ticks, Is.EqualTo(int.Parse(value)));
        }

        [TestCase("[\"run\"]")]
        [TestCase("[\"move\"]")]
        [TestCase("[\"stance\"]")]
        [TestCase("[\"dash\", \"dash\"]")]
        [TestCase("[3]")]
        [TestCase("\"dash\"")]
        public void S1_Deny(string deny) => Refused(Item(deny: deny), "deny");

        [Test]
        public void S1_UnknownField() => Refused(Item().Replace("\"ticks\"", "\"tick\": 1, \"ticks\""), "tick");

        // The pack itself refuses what the reader refuses, for packs built in code.
        [Test]
        public void S1_PackChecks()
        {
            CombatPack basis = RwrPack.Load("rwr-field").BuildCombat();
            WeaponDef[] weapons = basis.Weapons.ToArray();
            CombatBody body = basis.Body.Value, low = Ml3World.Body(65536, 49152, 49152);
            StanceDef crouch = new StanceDef("crouch", low, 500, 700, 6);
            CombatPack Make(CombatBody? b, params StanceDef[] s) => new CombatPack(weapons, basis.Resolver, basis.Selector, basis.MaxHealthRaw, body: b, stances: s);
            Assert.DoesNotThrow(() => Make(body, crouch));
            Assert.That(Make(body).HasStances, Is.False);
            Assert.That(Make(body, null).HasStances, Is.False);
            Assert.Throws<ArgumentException>(() => Make(null, crouch));
            Assert.Throws<ArgumentException>(() => Make(body, crouch, crouch));
            Assert.Throws<ArgumentException>(() => Make(body, new StanceDef("tall", body, 500, 700, 6)));
            Assert.Throws<ArgumentException>(() => Make(body, default(StanceDef)));
            var many = new StanceDef[8];
            for (int i = 0; i < 8; i++) many[i] = new StanceDef("s" + i, Ml3World.Body(114688 - 1 - i, 16384, 0), 500, 700, 6);
            Assert.Throws<ArgumentException>(() => Make(body, many));
            Assert.DoesNotThrow(() => Make(body, many[0], many[1], many[2], many[3], many[4], many[5], many[6]));
            Assert.Throws<ArgumentException>(() => new StanceDef("", low, 500, 700, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 0, 700, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 1001, 700, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 500, 0, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 500, 4001, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 500, 700, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 500, 700, 301));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 500, 700, 6, UnitAction.Move));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StanceDef("a", low, 500, 700, 6, UnitAction.Stance));
            Assert.DoesNotThrow(() => new StanceDef("a", low, 1, 1, 1, UnitAction.None));
            Assert.DoesNotThrow(() => new StanceDef("a", low, 1000, 4000, 300, StanceDef.Deniable));
        }

        // ---- S2: the digest ----

        // The PolicyDigest of rwr-field read before STANCE-1 (main a1251bd): adding stances to the reader changed nothing for it.
        internal const ulong FieldDigest = 9491449277226543737UL;

        [Test]
        public void S2_FieldDigestIsUnchanged() => Assert.That(RwrPack.Load("rwr-field").BuildCombat().PolicyDigest, Is.EqualTo(FieldDigest));

        [Test]
        public void S2_StancesAndEveryNumberChangeTheDigest()
        {
            ulong field = RwrPack.Load("rwr-field").BuildCombat().PolicyDigest;
            ulong with = Load(Crouch + ", " + Prone).BuildCombat().PolicyDigest;
            Assert.That(with, Is.Not.EqualTo(field));
            Assert.That(RwrPack.Load("rwr-stance").BuildCombat().PolicyDigest, Is.EqualTo(with));
            var seen = new HashSet<ulong> { field, with };
            string[] variants =
            {
                Item("crouch2") + ", " + Prone, Item(height: "1.01") + ", " + Prone, Item(eye: "0.7") + ", " + Prone, Item(muzzle: "0.7") + ", " + Prone,
                Item(speed: "501") + ", " + Prone, Item(spread: "701") + ", " + Prone, Item(ticks: "7") + ", " + Prone,
                Item(deny: "[\"dash\"]") + ", " + Prone, Item(deny: "[]") + ", " + Prone, Crouch,
            };
            foreach (string v in variants)
                Assert.That(seen.Add(Load(v).BuildCombat().PolicyDigest), Is.True, v);
        }

        [Test]
        public void S2_WithSelectorKeepsTheStances()
        {
            CombatPack pack = RwrPack.Load("rwr-stance").BuildCombat();
            CombatPack copy = pack.WithSelector(pack.Selector);
            Assert.That(copy.Stances.ToArray(), Is.EqualTo(pack.Stances.ToArray()));
            Assert.That(copy.PolicyDigest, Is.EqualTo(pack.PolicyDigest));
        }

        // A pack without stances sizes its world as before; with stances the events and the task table grow by the unit count.
        [Test]
        public void S2_ConfigGrowsOnlyWithStances()
        {
            SimConfig field = RwrPack.Load("rwr-field").BuildCombat().BuildConfig(20, 8), stance = RwrPack.Load("rwr-stance").BuildCombat().BuildConfig(20, 8);
            Assert.That((field.EventHardLimit, field.EventInitialCapacity, field.TimerCapacity), Is.EqualTo((24, 24, 1024)));
            Assert.That((stance.EventHardLimit, stance.EventInitialCapacity, stance.TimerCapacity), Is.EqualTo((32, 32, 1032)));
        }
    }
}
