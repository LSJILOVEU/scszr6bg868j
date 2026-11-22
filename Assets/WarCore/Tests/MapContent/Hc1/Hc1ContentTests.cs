using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // HC-1 S2 to S5 and S12: combat.body.radius read from content, the body and stance checks that follow it, the digest, the
    // CombatBody constructor and the window gate. r = 0.15 m is 9830 raw (9830.4 truncated); the bounds are 0.05 m (3276 raw)
    // and 0.25 m (16384 raw).
    public sealed class Hc1ContentTests
    {
        internal const long R15 = 9830, Min = 3276, Max = 16384;

        internal static string BodyJson(string radius, string height = "1.75", string eye = "1.5", string muzzle = "1.5") =>
            "\"body\": { \"height\": " + height + ", \"eye\": " + eye + ", \"muzzle\": " + muzzle + (radius == null ? "" : ", \"radius\": " + radius) + " }";

        // rwr plus a combat body with an optional radius, in memory.
        internal static ContentPack Pack(string radius, string height = "1.75", string eye = "1.5", string muzzle = "1.5") =>
            Ml3Content.Load("hc1", Ml3Content.Layer("hc1", "rwr", "\"combat\": { " + BodyJson(radius, height, eye, muzzle) + " }"));

        private static CombatBody Loaded(string radius, string height = "1.75", string eye = "1.5", string muzzle = "1.5")
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = Pack(radius, height, eye, muzzle));
            return pack.Body.Value;
        }

        private static ContentException Refused(string radius, string height = "1.75", string eye = "1.5", string muzzle = "1.5") =>
            Assert.Throws<ContentException>(() => Pack(radius, height, eye, muzzle));

        // ---- S2: the key ----

        [Test]
        public void S2_RadiusIsReadInMetres()
        {
            Assert.That(Loaded("0.15").Radius.Raw, Is.EqualTo(R15));
            Assert.That(Loaded("0.15").Radius.Raw, Is.EqualTo(SimFix.Ratio(15, 100).Raw));
            Assert.That(Loaded("0.05").Radius.Raw, Is.EqualTo(Min));
            Assert.That(Loaded("0.25").Radius.Raw, Is.EqualTo(Max));
            Assert.That(Loaded(null).Radius.Raw, Is.EqualTo(Max));
            Assert.That(Pack("0.15").UnitRadius.Raw, Is.EqualTo(R15));
            Assert.That(Pack(null).UnitRadius.Raw, Is.EqualTo(BodyRadius.DefaultRaw));
            Assert.That(RwrPack.Load("rwr").UnitRadius.Raw, Is.EqualTo(BodyRadius.DefaultRaw), "a pack without a body");
        }

        // 0.04998 m is 3275 raw (one under the floor), 0.25002 m is 16385 raw (one over the ceiling); 0.04999 m already truncates to 3276.
        [TestCase("0.04998", 3275L)]
        [TestCase("0.25002", 16385L)]
        [TestCase("0", 0L)]
        [TestCase("-0.1", -6553L)]
        public void S2_RadiusOutsideTheRangeIsRefused(string radius, long raw)
        {
            ContentException e = Refused(radius);
            Assert.That(e.Message, Is.EqualTo("content pack 'hc1' layer 'hc1' combat.body field 'radius': is " + radius + " m (" + raw
                + " raw), allowed 0.05 to 0.25 m (3276 to 16384 raw)"));
        }

        [Test]
        public void S2_RadiusMustBeANumber()
        {
            Assert.That(Refused("true").Message, Does.Contain("field 'radius'"));
            Assert.That(Refused("\"thin\"").Message, Does.Contain("field 'radius'"));
        }

        // The matrix: a child's body replaces the parent's whole, so a child that writes none goes back to the default.
        [Test]
        public void S2_ChildBodyDoesNotInheritTheParentsRadius()
        {
            ContentPack kid = Ml3Content.Load("kid",
                Ml3Content.Layer("mid", "rwr", "\"combat\": { " + BodyJson("0.15") + " }"),
                Ml3Content.Layer("kid", "mid", "\"combat\": { " + BodyJson(null) + " }"));
            Assert.That(kid.Body.Value.Radius.Raw, Is.EqualTo(Max));
            ContentPack parent = Ml3Content.Load("mid", Ml3Content.Layer("mid", "rwr", "\"combat\": { " + BodyJson("0.15") + " }"));
            Assert.That(parent.Body.Value.Radius.Raw, Is.EqualTo(R15));
            ContentPack thin = Ml3Content.Load("kid2",
                Ml3Content.Layer("mid", "rwr", "\"combat\": { " + BodyJson("0.15") + " }"),
                Ml3Content.Layer("kid2", "mid", "\"combat\": { " + BodyJson("0.1") + " }"));
            Assert.That(thin.Body.Value.Radius.Raw, Is.EqualTo(SimFix.Ratio(1, 10).Raw));
        }

        [Test]
        public void S2_ThePackAndItsCombatPackAgree()
        {
            ContentPack pack = Pack("0.15");
            Assert.That(pack.BuildCombat().Body.Value.Radius.Raw, Is.EqualTo(R15));
        }

        // ---- S3: the body checks follow r ----

        // r = 0.15: 2r = 0.3 m (19660 raw) is the lowest height; 0.29 m is 19005 raw.
        [Test]
        public void S3_HeightFollowsTheRadius()
        {
            Assert.That(Loaded("0.15", "0.3", "0.15", "0").Height.Raw, Is.EqualTo(19660));
            ContentException low = Refused("0.15", "0.29", "0.15", "0");
            Assert.That(low.Message, Does.EndWith("combat.body field 'height': is 0.29 m (19005 raw), allowed 0.3 to 2 m (19660 to 131072 raw)"));
            // The default radius refuses the same heights.
            Assert.That(Refused(null, "0.3", "0.25", "0").Message, Does.Contain("field 'height'"));
        }

        // r = 0.15, height 0.5 m (32768 raw): eye from 0.15 m (9830 raw) to height - r = 22938 raw; 0.35 m is 22937 raw, 0.36 m is 23592 raw.
        [Test]
        public void S3_EyeFollowsTheRadiusOnBothSides()
        {
            Assert.That(Loaded("0.15", "0.5", "0.15", "0").Eye.Raw, Is.EqualTo(R15));
            Assert.That(Loaded("0.15", "0.5", "0.35", "0").Eye.Raw, Is.EqualTo(22937));
            Assert.That(Refused("0.15", "0.5", "0.14", "0").Message, Does.EndWith("combat.body field 'eye': is 0.14 m (9175 raw), allowed 0.15 to 0.35 m (9830 to 22938 raw)"));
            Assert.That(Refused("0.15", "0.5", "0.36", "0").Message, Does.EndWith("combat.body field 'eye': is 0.36 m (23592 raw), allowed 0.15 to 0.35 m (9830 to 22938 raw)"));
        }

        // The default radius: the messages are the ones every earlier body test pins (r = 0.25 m), unchanged.
        [Test]
        public void S3_DefaultRadiusKeepsTheOldMessages()
        {
            Assert.That(Refused(null, "0.49999", "0.25", "0").Message,
                Is.EqualTo("content pack 'hc1' layer 'hc1' combat.body field 'height': is 0.49999 m (32767 raw), allowed 0.5 to 2 m (32768 to 131072 raw)"));
            Assert.That(Refused("0.25", "1.75", "0.24999", "1.5").Message,
                Does.EndWith("combat.body field 'eye': is 0.24999 m (16383 raw), allowed 0.25 to 1.5 m (16384 to 98304 raw)"));
        }

        // ---- S4: the stance checks follow r ----

        private static string Stance(string id, string height, string eye, string muzzle) =>
            "{ \"id\": \"" + id + "\", \"height\": " + height + ", \"eye\": " + eye + ", \"muzzle\": " + muzzle
            + ", \"speedPermille\": 500, \"spreadPermille\": 700, \"ticks\": 6 }";

        private static ContentPack WithStance(string radius, string height, string eye, string muzzle) =>
            Ml3Content.Load("hc1s", Ml3Content.Layer("hc1s", "rwr-field", "\"combat\": { " + BodyJson(radius)
                + ", \"stances\": [" + Stance("low", height, eye, muzzle) + "] }"));

        [Test]
        public void S4_StanceHeightFollowsTheRadius()
        {
            ContentPack ok = null;
            Assert.DoesNotThrow(() => ok = WithStance("0.15", "0.3", "0.15", "0.15"));
            Assert.That(ok.BuildCombat().Stances[0].Body.Height.Raw, Is.EqualTo(19660));
            ContentException e = Assert.Throws<ContentException>(() => WithStance("0.15", "0.29", "0.15", "0.15"));
            Assert.That(e.Message, Does.EndWith("field 'height': is 0.29 m (19005 raw), allowed 0.3 to 2 m (19660 to 131072 raw)"));
            Assert.Throws<ContentException>(() => WithStance(null, "0.3", "0.25", "0.25"));
        }

        [Test]
        public void S4_StanceEyeFollowsTheRadiusOnBothSides()
        {
            ContentPack low = null, high = null;
            Assert.DoesNotThrow(() => low = WithStance("0.15", "0.5", "0.15", "0.15"));
            Assert.DoesNotThrow(() => high = WithStance("0.15", "0.5", "0.35", "0.35"));
            Assert.That(low.BuildCombat().Stances[0].Body.Eye.Raw, Is.EqualTo(R15));
            Assert.That(high.BuildCombat().Stances[0].Body.Eye.Raw, Is.EqualTo(22937));
            Assert.That(Assert.Throws<ContentException>(() => WithStance("0.15", "0.5", "0.14", "0.15")).Message, Does.Contain("field 'eye': is 0.14 m"));
            Assert.That(Assert.Throws<ContentException>(() => WithStance("0.15", "0.5", "0.36", "0.15")).Message, Does.Contain("field 'eye': is 0.36 m"));
        }

        [Test]
        public void S4_EveryStanceBodyCarriesThePacksRadius()
        {
            ContentPack pack = Ml3Content.Load("hc1s", Ml3Content.Layer("hc1s", "rwr-stance", "\"combat\": { " + BodyJson("0.15") + " }"));
            CombatPack combat = null;
            Assert.DoesNotThrow(() => combat = pack.BuildCombat());
            Assert.That(combat.Stances.Length, Is.EqualTo(2));
            Assert.That(combat.Body.Value.Radius.Raw, Is.EqualTo(R15));
            for (int i = 0; i < combat.Stances.Length; i++) Assert.That(combat.Stances[i].Body.Radius.Raw, Is.EqualTo(R15), "stance " + i);
            CombatPack plain = Ml3Content.Load("hc1d", Ml3Content.Layer("hc1d", "rwr-stance", "")).BuildCombat();
            for (int i = 0; i < plain.Stances.Length; i++) Assert.That(plain.Stances[i].Body.Radius.Raw, Is.EqualTo(Max), "default stance " + i);
        }

        // A pack made in code may not give a stance another radius than the body.
        [Test]
        public void S4_APackWithAStanceOfAnotherRadiusIsRefused()
        {
            CombatPack basis = RwrPack.Load("rwr-stance").BuildCombat();
            CombatBody stand = new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(R15));
            CombatBody same = new CombatBody(SimFix.FromRaw(65536), SimFix.FromRaw(49152), SimFix.FromRaw(49152), SimFix.FromRaw(R15));
            CombatBody other = new CombatBody(SimFix.FromRaw(65536), SimFix.FromRaw(49152), SimFix.FromRaw(49152));
            CombatPack Make(CombatBody crouch) => new CombatPack(basis.Weapons.ToArray(), basis.Resolver, basis.Selector, basis.MaxHealthRaw, body: stand,
                stances: new[] { new StanceDef("crouch", crouch, 500, 700, 6) });
            Assert.DoesNotThrow(() => Make(same));
            Assert.That(Assert.Throws<ArgumentException>(() => Make(other)).Message, Does.Contain("another radius"));
        }

        // ---- S5: the digest and the constructor ----

        private static CombatPack BodyPack(CombatBody body)
        {
            CombatPack basis = RwrPack.Load("rwr-field").BuildCombat();
            return new CombatPack(basis.Weapons.ToArray(), basis.Resolver, basis.Selector, basis.MaxHealthRaw, body: body);
        }

        // The digests before HC-1, written out: a body that sets no radius hashes the bytes it hashed before.
        [Test]
        public void S5_DefaultRadiusKeepsTheDigestOfBefore()
        {
            Assert.That(Ml3World.Pack(Ml3World.Rwr).PolicyDigest, Is.EqualTo(BaselineBodyDigest));
            Assert.That(RwrPack.Load("rwr-stance").BuildCombat().PolicyDigest, Is.EqualTo(BaselineStanceDigest));
        }

        private const ulong BaselineBodyDigest = 146258096439376818UL, BaselineStanceDigest = 5280425556807205711UL;

        [Test]
        public void S5_ThreeAndFourArgumentConstructorsAgreeOnTheDefault()
        {
            var three = new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304));
            var four = new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(Max));
            Assert.That(three.Radius.Raw, Is.EqualTo(Max));
            Assert.That(BodyPack(four).PolicyDigest, Is.EqualTo(BodyPack(three).PolicyDigest));
        }

        [Test]
        public void S5_ARadiusOtherThanTheDefaultChangesTheDigest()
        {
            CombatBody Of(long radius) => new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(radius));
            ulong standard = BodyPack(Of(Max)).PolicyDigest, thin = BodyPack(Of(R15)).PolicyDigest, thinner = BodyPack(Of(Min)).PolicyDigest;
            Assert.That(thin, Is.Not.EqualTo(standard));
            Assert.That(thinner, Is.Not.EqualTo(standard));
            Assert.That(thinner, Is.Not.EqualTo(thin));
            Assert.That(BodyPack(Of(R15)).PolicyDigest, Is.EqualTo(thin), "the same radius, the same digest");
            Assert.That(Pack("0.15").BuildCombat().PolicyDigest, Is.Not.EqualTo(Pack(null).BuildCombat().PolicyDigest));
        }

        [TestCase(3275L)]
        [TestCase(16385L)]
        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(long.MaxValue)]
        public void S5_ConstructorRefusesARadiusOutsideTheRange(long radius) =>
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(radius)));

        [TestCase(3276L)]
        [TestCase(R15)]
        [TestCase(16384L)]
        public void S5_ConstructorAcceptsTheRangeIncludingBothEnds(long radius) =>
            Assert.That(new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(radius)).Radius.Raw, Is.EqualTo(radius));

        [Test]
        public void S5_TheRangeIsTheDocumentedOne()
        {
            Assert.That(BodyRadius.MinRaw, Is.EqualTo(Min));
            Assert.That(BodyRadius.DefaultRaw, Is.EqualTo(Max));
            Assert.That(BodyRadius.MaxRaw, Is.EqualTo(NavGrid.MaxClearRadiusRaw));
            Assert.That(BodyRadius.IsValid(SimFix.FromRaw(Min - 1)), Is.False);
            Assert.That(BodyRadius.IsValid(SimFix.FromRaw(Min)), Is.True);
            Assert.That(BodyRadius.IsValid(SimFix.FromRaw(Max)), Is.True);
            Assert.That(BodyRadius.IsValid(SimFix.FromRaw(Max + 1)), Is.False);
        }

        // ---- S12: the window gate follows the body's radius ----

        private static readonly MapNext Next = new MapNext(1, 1, 1, 1);

        private static MapDesc Flat() =>
            new MapDesc("hc1-flat", "none", "ml3-body", new MapXZ(12, 4), new MapXZ(0, 0), 0, 10 * SimFix.Scale, new[] { new MapLayer("walls", 1) },
                null, null, new MapWall[0], new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(41, "teamIdle"),
                new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0], Next);

        private static MapStampPlan Window(long sill, long radius)
        {
            var body = new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(radius));
            return MapPrefabs.Window(Flat(), "win", new MapXZ(0, 1), true, 10, 4, 2, 4 * SimFix.Scale, "walls", "wall", body, sill, MapPrefabs.DefaultOpeningRaw);
        }

        // r = 9830: 2r = 19660 raw. An odd sill grows 1 raw first; 19658 stays and is below 2r.
        [Test]
        public void S12_SillAtLeastTwiceThePacksRadius()
        {
            Assert.That(Window(19660, R15).Walls[1].Template.HeightRaw, Is.EqualTo(19660));
            Assert.That(Window(19659, R15).Walls[1].Template.HeightRaw, Is.EqualTo(19660));
            Assert.That(Assert.Throws<ArgumentException>(() => Window(19658, R15)).Message, Does.StartWith("the sill is lower than 2 x the unit radius"));
            // The default radius refuses what the thin body allows.
            Assert.That(Assert.Throws<ArgumentException>(() => Window(19660, Max)).Message, Does.StartWith("the sill is lower than 2 x the unit radius"));
        }

        private static MapStampPlan WindowOnFloor(long sill, long radius)
        {
            var body = new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(radius));
            return MapPrefabs.WindowOn(Flat(), "win", new MapXZ(0, 1), true, 10, 4, 2, 3 * SimFix.Scale, 6 * SimFix.Scale, "walls", "wall", body, sill, MapPrefabs.DefaultOpeningRaw);
        }

        // The window on a floor plate: the same gate, the same r.
        [Test]
        public void S12_TheFloorWindowFollowsTheRadiusToo()
        {
            Assert.DoesNotThrow(() => WindowOnFloor(19660, R15));
            Assert.DoesNotThrow(() => WindowOnFloor(19659, R15));
            Assert.That(Assert.Throws<ArgumentException>(() => WindowOnFloor(19658, R15)).Message, Does.StartWith("the sill is lower than 2 x the unit radius"));
            Assert.That(Assert.Throws<ArgumentException>(() => WindowOnFloor(19660, Max)).Message, Does.StartWith("the sill is lower than 2 x the unit radius"));
        }

        // The upper bounds use the same r: with eye = muzzle = 1.5 m the sill limit is 1.5 m - r and the opening limit 1.5 m + r.
        [Test]
        public void S12_SillAndOpeningLimitsFollowTheRadius()
        {
            long sillLimit = 98304 - R15, openingLimit = 98304 + R15;
            Assert.DoesNotThrow(() => Window(sillLimit - (sillLimit & 1), R15));
            Assert.DoesNotThrow(() => MapPrefabs.Window(Flat(), "win", new MapXZ(0, 1), true, 10, 4, 2, 4 * SimFix.Scale, "walls", "wall",
                new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(R15)), MapPrefabs.DefaultSillRaw, openingLimit));
            Assert.That(Assert.Throws<ArgumentException>(() => Window(sillLimit + 2 - (sillLimit & 1), R15)).Message, Does.StartWith("the sill is higher than"));
            var body = new CombatBody(SimFix.FromRaw(114688), SimFix.FromRaw(98304), SimFix.FromRaw(98304), SimFix.FromRaw(R15));
            long belowOpening = openingLimit - 2 - (openingLimit & 1);
            Assert.That(Assert.Throws<ArgumentException>(() => MapPrefabs.Window(Flat(), "win", new MapXZ(0, 1), true, 10, 4, 2, 4 * SimFix.Scale, "walls", "wall",
                body, MapPrefabs.DefaultSillRaw, belowOpening)).Message, Does.StartWith("the opening top is lower than"));
        }
    }
}
