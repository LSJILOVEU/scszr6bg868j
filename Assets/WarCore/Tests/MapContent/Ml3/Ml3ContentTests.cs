using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    // ML-3 acceptance 6: the combat section's body, read from content, and gate D6 on each side of every bound.
    public sealed class Ml3ContentTests
    {
        private static ContentException Refused(string height, string eye, string muzzle) =>
            Assert.Throws<ContentException>(() => Ml3Content.Rwr(height, eye, muzzle));

        // An accepted body: a refusal fails the assertion here instead of erroring the test.
        private static CombatBody Loaded(string height, string eye, string muzzle)
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = Ml3Content.Rwr(height, eye, muzzle));
            return pack.Body.Value;
        }

        [Test]
        public void BodyIsReadInMetres()
        {
            ContentPack pack = Ml3Content.Rwr();
            Assert.That(pack.Body, Is.EqualTo(Ml3World.Rwr));
            Assert.That(pack.BuildCombat().Body, Is.EqualTo(Ml3World.Rwr));
            Assert.That(RwrPack.Load("rwr").Body, Is.Null);
        }

        // Every shipped pack stays without a body (none is turned on by this package).
        [TestCase("rwr")]
        [TestCase("rwr-m6")]
        [TestCase("rwr-m7-playtest")]
        [TestCase("rwr-ai-playtest")]
        [TestCase("rwr-melee")]
        [TestCase("rwr-terrain-demo")]
        public void ShippedPacksHaveNoBody(string id) => Assert.That(RwrPack.Load(id).Body, Is.Null);

        // 1.7 and 1.8 m load (decimals truncate to raw): there is no even-raw rule.
        [TestCase("1.7", "1.4", "1.4", 111411L)]
        [TestCase("1.8", "1.5", "1.5", 117964L)]
        public void DecimalHeightsLoad(string height, string eye, string muzzle, long raw) =>
            Assert.That(Ml3Content.Rwr(height, eye, muzzle).Body.Value.Height.Raw, Is.EqualTo(raw));

        // D6-1: 2r <= height <= 2 m (r = 0.25 m). 0.49999 m is 32767 raw, 2.00002 m is 131073 raw (2.00001 m truncates to 131072).
        [Test]
        public void HeightBounds()
        {
            Assert.That(Loaded("0.5", "0.25", "0").Height.Raw, Is.EqualTo(32768));
            Assert.That(Loaded("2", "1.75", "2").Height.Raw, Is.EqualTo(131072));
            ContentException low = Refused("0.49999", "0.25", "0");
            Assert.That(low.Message, Is.EqualTo("content pack 'ml3-body' layer 'ml3-body' combat.body field 'height': is 0.49999 m (32767 raw), allowed 0.5 to 2 m (32768 to 131072 raw)"));
            ContentException high = Refused("2.00002", "1.5", "1.5");
            Assert.That(high.Message, Is.EqualTo("content pack 'ml3-body' layer 'ml3-body' combat.body field 'height': is 2.00002 m (131073 raw), allowed 0.5 to 2 m (32768 to 131072 raw)"));
        }

        // D6-2: r <= eye <= height - r. 0.24999 m is 16383 raw, 1.50002 m is 98305 raw (height 1.75: limit 98304).
        [Test]
        public void EyeBounds()
        {
            Assert.That(Loaded("1.75", "0.25", "1.5").Eye.Raw, Is.EqualTo(16384));
            Assert.That(Loaded("1.75", "1.5", "1.5").Eye.Raw, Is.EqualTo(98304));
            Assert.That(Refused("1.75", "0.24999", "1.5").Message, Does.EndWith("combat.body field 'eye': is 0.24999 m (16383 raw), allowed 0.25 to 1.5 m (16384 to 98304 raw)"));
            Assert.That(Refused("1.75", "1.50002", "1.5").Message, Does.EndWith("combat.body field 'eye': is 1.50002 m (98305 raw), allowed 0.25 to 1.5 m (16384 to 98304 raw)"));
        }

        // D6-3: 0 <= muzzle <= height. -0.00002 m is -1 raw, 1.75002 m is 114689 raw.
        [Test]
        public void MuzzleBounds()
        {
            Assert.That(Loaded("1.75", "1.5", "0").Muzzle.Raw, Is.EqualTo(0));
            Assert.That(Loaded("1.75", "1.5", "1.75").Muzzle.Raw, Is.EqualTo(114688));
            Assert.That(Refused("1.75", "1.5", "-0.00002").Message, Does.EndWith("combat.body field 'muzzle': is -0.00002 m (-1 raw), allowed 0 to 1.75 m (0 to 114688 raw)"));
            Assert.That(Refused("1.75", "1.5", "1.75002").Message, Does.EndWith("combat.body field 'muzzle': is 1.75002 m (114689 raw), allowed 0 to 1.75 m (0 to 114688 raw)"));
        }

        // All three fields are required; a child's body replaces the parent's whole body (no field-by-field inheritance).
        [Test]
        public void AllThreeFieldsAreRequiredAndAChildReplacesTheWholeBody()
        {
            ContentException missing = Assert.Throws<ContentException>(() => Ml3Content.Load("kid",
                Ml3Content.Layer("kid", "rwr", "\"combat\": { \"body\": { \"height\": 1.75, \"eye\": 1.5 } }")));
            Assert.That(missing.Message, Is.EqualTo("content pack 'kid' layer 'kid' combat.body is missing field 'muzzle'"));
            ContentException partial = Assert.Throws<ContentException>(() => Ml3Content.Load("kid",
                Ml3Content.Layer("mid", "rwr", "\"combat\": { " + Ml3Content.BodyJson + " }"),
                Ml3Content.Layer("kid", "mid", "\"combat\": { \"body\": { \"eye\": 1.4 } }")));
            Assert.That(partial.Message, Is.EqualTo("content pack 'kid' layer 'kid' combat.body is missing field 'height'"));
            ContentPack inherited = Ml3Content.Load("kid",
                Ml3Content.Layer("mid", "rwr", "\"combat\": { " + Ml3Content.BodyJson + " }"),
                Ml3Content.Layer("kid", "mid", "\"combat\": { \"maxHealth\": 50 }"));
            Assert.That(inherited.Body, Is.EqualTo(Ml3World.Rwr));
        }

        [Test]
        public void BodyMustBeAnObjectOfNumbers()
        {
            Assert.That(Assert.Throws<ContentException>(() => Ml3Content.Load("kid", Ml3Content.Layer("kid", "rwr", "\"combat\": { \"body\": 1.75 }"))).Message,
                Is.EqualTo("content pack 'kid' layer 'kid' combat field 'body': must be an object"));
            Assert.That(Assert.Throws<ContentException>(() => Ml3Content.Load("kid", Ml3Content.Layer("kid", "rwr",
                "\"combat\": { \"body\": { \"height\": 1.75, \"eye\": 1.5, \"muzzle\": 1.5, \"neck\": 1.6 } }"))).Message,
                Is.EqualTo("content pack 'kid' layer 'kid' combat.body field 'neck': unknown field name (misspelled?)"));
            Assert.That(Assert.Throws<ContentException>(() => Ml3Content.Load("kid", Ml3Content.Layer("kid", "rwr",
                "\"combat\": { \"body\": { \"height\": { }, \"eye\": 1.5, \"muzzle\": 1.5 } }"))).Message,
                Is.EqualTo("content layer 'kid' combat.body field 'height' must be a number, string or boolean"));
            // Only "body" may hold an object in the combat section.
            Assert.That(Assert.Throws<ContentException>(() => Ml3Content.Load("kid", Ml3Content.Layer("kid", "rwr", "\"combat\": { \"shape\": { } }"))).Message,
                Is.EqualTo("content layer 'kid' combat field 'shape' must be a number, string or boolean"));
        }
    }
}
