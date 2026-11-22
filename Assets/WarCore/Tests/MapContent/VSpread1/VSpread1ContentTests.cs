using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Stance;

namespace WarCore.Tests.VSpread1
{
    // VSPREAD-1 V1 (off: digests, draws and hashes as on main), V2 (reading combat.verticalSpread) and V3 (the digest section).
    public sealed class VSpread1ContentTests
    {
        // ---- V1: off changes nothing (first recorded on main 89dfb1a, before any code of this pack) ----

        internal const ulong StanceDigest = 5280425556807205711UL, AimDigest = 17471878450968030008UL, BlastDigest = 2272965899723657996UL;
        // The stance-wall demo world (rwr-stance) over 300 steps: the fold of every step's state hash, the last hash and the
        // combat.spread draws.
        internal const ulong StanceWorldFold = 11931767340028296599UL, StanceWorldHash = 8512682664766950886UL;
        internal const int StanceWorldDraws = 240;

        [Test]
        public void V1_ShippedDigestsAreUnchanged()
        {
            Assert.That(RwrPack.Load("rwr-field").BuildCombat().PolicyDigest, Is.EqualTo(Stance1ContentTests.FieldDigest));
            Assert.That(RwrPack.Load("rwr-stance").BuildCombat().PolicyDigest, Is.EqualTo(StanceDigest));
            Assert.That(RwrPack.Load("rwr-aim").BuildCombat().PolicyDigest, Is.EqualTo(AimDigest));
            Assert.That(RwrPack.Load("rwr-blast").BuildCombat().PolicyDigest, Is.EqualTo(BlastDigest));
            Assert.That(RwrPack.Load("rwr-stance").BuildCombat().VerticalSpread, Is.Null);
        }

        [Test]
        public void V1_AStanceWorldDrawsAndHashesAsBefore()
        {
            SimSession s = WorldBuilder.Build(StanceDemo.Recipe(), StanceDemo.MapBytes(), false);
            ulong fold = 1469598103934665603UL;
            for (int i = 0; i < 300; i++) { s.StepOnce(); fold = unchecked((fold ^ s.StateHash) * 1099511628211UL); }
            Assert.That(fold, Is.EqualTo(StanceWorldFold));
            Assert.That(s.StateHash, Is.EqualTo(StanceWorldHash));
            Assert.That(VSpread1Worlds.SpreadDraws(s), Is.EqualTo(StanceWorldDraws));
        }

        // ---- V2: reading ----

        internal const string Example = "{ \"permille\": 1000, \"byStance\": { \"crouch\": 800, \"prone\": 600 }, \"aim\": \"center\", \"snapMm\": 600 }";

        // A layer over parent (rwr-stance by default) whose combat section writes this vertical spread.
        internal static ContentPack Load(string vertical, string parent = "rwr-stance") =>
            Ml3Content.Load("vspread1", Ml3Content.Layer("vspread1", parent, "\"combat\": { \"verticalSpread\": " + vertical + " }"));

        private static void Refused(string vertical, string field, string parent = "rwr-stance")
        {
            ContentException e = Assert.Throws<ContentException>(() => Load(vertical, parent).BuildCombat());
            StringAssert.Contains("'" + field + "'", e.Message);
        }

        private static VerticalSpread Accepted(string vertical, string parent = "rwr-stance")
        {
            VerticalSpread v = null;
            Assert.DoesNotThrow(() => v = Load(vertical, parent).BuildCombat().VerticalSpread);
            Assert.That(v, Is.Not.Null);
            return v;
        }

        private static string Spread(string permille = "1000", string byStance = null, string aim = null, string snap = null, string extra = null) =>
            "{ \"permille\": " + permille + (byStance == null ? "" : ", \"byStance\": " + byStance) + (aim == null ? "" : ", \"aim\": \"" + aim + "\"")
            + (snap == null ? "" : ", \"snapMm\": " + snap) + (extra == null ? "" : ", " + extra) + " }";

        [Test]
        public void V2_TheShippedLayerReads()
        {
            VerticalSpread v = Layer("rwr-vspread");
            Assert.That(v.Permille, Is.EqualTo(1000));
            Assert.That(v.StancePermille.Length, Is.EqualTo(2));
            Assert.That(v.StancePermille[0], Is.EqualTo(800));
            Assert.That(v.StancePermille[1], Is.EqualTo(600));
            Assert.That(v.Aim, Is.EqualTo(VerticalAim.Center));
            Assert.That(v.SnapMm, Is.EqualTo(600));
            Assert.That(v.SnapRaw, Is.EqualTo(39321L));
        }

        private static VerticalSpread Layer(string layer) => RwrPack.Load(layer).BuildCombat().VerticalSpread;

        [Test]
        public void V2_Refusals()
        {
            Refused(Spread(), "verticalSpread", "rwr-surfaces");
            Refused("{ \"aim\": \"eye\" }", "permille");
            Refused(Spread("0"), "permille");
            Refused(Spread("4001"), "permille");
            Refused(Spread(byStance: "{ \"crouch\": 0 }"), "crouch");
            Refused(Spread(byStance: "{ \"crouch\": 4001 }"), "crouch");
            Refused(Spread(byStance: "{ \"prone\": 600 }"), "byStance", "rwr-field");
            Refused(Spread(byStance: "{ \"kneel\": 800 }"), "kneel");
            Refused(Spread(byStance: "[ 800 ]"), "byStance");
            Refused(Spread(aim: "head"), "aim");
            Refused(Spread(aim: "eye", snap: "600"), "snapMm");
            Refused(Spread(snap: "600"), "snapMm");
            Refused(Spread(aim: "center", snap: "0"), "snapMm");
            Refused(Spread(aim: "center", snap: "5001"), "snapMm");
            Refused(Spread(extra: "\"reload\": 1"), "reload");
            Refused(Spread(extra: "\"reload\": { }"), "reload");
            Refused("[ 1000 ]", "verticalSpread");
        }

        [Test]
        public void V2_AnUnknownAimNamesTheAllowedOnes()
        {
            ContentException e = Assert.Throws<ContentException>(() => Load(Spread(aim: "head")).BuildCombat());
            StringAssert.Contains("eye, center", e.Message);
        }

        // A child layer that replaced the stances keeps the parent's byStance keys: refused when the pack is built.
        [Test]
        public void V2_AChildsNewStancesRefuseTheParentsKeys()
        {
            string low = "{ \"id\": \"low\", \"height\": 1.0, \"eye\": 0.75, \"muzzle\": 0.75, \"speedPermille\": 500, \"spreadPermille\": 700, \"ticks\": 6 }";
            ContentException e = Assert.Throws<ContentException>(() => Ml3Content.Load("vs1-child",
                Ml3Content.Layer("vs1-parent", "rwr-stance", "\"combat\": { \"verticalSpread\": " + Spread(byStance: "{ \"crouch\": 800 }") + " }"),
                Ml3Content.Layer("vs1-child", "vs1-parent", "\"combat\": { \"stances\": [" + low + "] }")).BuildCombat());
            StringAssert.Contains("'crouch'", e.Message);
        }

        [Test]
        public void V2_Bounds()
        {
            Assert.That(Accepted(Spread("1")).Permille, Is.EqualTo(1));
            Assert.That(Accepted(Spread("4000")).Permille, Is.EqualTo(4000));
            Assert.That(Accepted(Spread(byStance: "{ \"crouch\": 1 }")).StancePermille[0], Is.EqualTo(1));
            Assert.That(Accepted(Spread(byStance: "{ \"crouch\": 4000 }")).StancePermille[0], Is.EqualTo(4000));
            Assert.That(Accepted(Spread(aim: "center", snap: "1")).SnapMm, Is.EqualTo(1));
            Assert.That(Accepted(Spread(aim: "center", snap: "5000")).SnapMm, Is.EqualTo(5000));
        }

        // Defaults: aim eye, snap 600; a stance byStance leaves out takes the standing value; no stances: no levels.
        [Test]
        public void V2_Defaults()
        {
            VerticalSpread v = Accepted(Spread("900", byStance: "{ \"prone\": 500 }"));
            Assert.That(v.Aim, Is.EqualTo(VerticalAim.Eye));
            Assert.That(v.SnapMm, Is.EqualTo(VerticalSpread.DefaultSnapMm));
            Assert.That(v.StancePermille[0], Is.EqualTo(900));
            Assert.That(v.StancePermille[1], Is.EqualTo(500));
            Assert.That(Accepted(Spread("900", aim: "center")).SnapMm, Is.EqualTo(600));
            Assert.That(Accepted(Spread("700"), "rwr-field").StancePermille.Length, Is.EqualTo(0));
            Assert.That(Accepted(Spread(aim: "eye")).Aim, Is.EqualTo(VerticalAim.Eye));
        }

        // A child layer's verticalSpread replaces the parent's whole (no field of the parent's stays).
        [Test]
        public void V2_AChildReplacesTheWholeObject()
        {
            VerticalSpread v = null;
            Assert.DoesNotThrow(() => v = Ml3Content.Load("vs1-child",
                Ml3Content.Layer("vs1-parent", "rwr-stance", "\"combat\": { \"verticalSpread\": " + Spread("900", "{ \"prone\": 500 }", "center", "700") + " }"),
                Ml3Content.Layer("vs1-child", "vs1-parent", "\"combat\": { \"verticalSpread\": " + Spread("1200") + " }")).BuildCombat().VerticalSpread);
            Assert.That(v.Permille, Is.EqualTo(1200));
            Assert.That(v.StancePermille[0], Is.EqualTo(1200));
            Assert.That(v.StancePermille[1], Is.EqualTo(1200));
            Assert.That(v.Aim, Is.EqualTo(VerticalAim.Eye));
            Assert.That(v.SnapMm, Is.EqualTo(600));
        }

        // The pack refuses a vertical spread without a body or with a wrong number of stance values.
        [Test]
        public void V2_ThePackChecksBodyAndLevels()
        {
            CombatPack shipped = VSpread1Worlds.Layer("rwr-stance");
            WeaponDef[] weapons = { VSpread1Worlds.Fixed() };
            Assert.Throws<ArgumentException>(() => new CombatPack(weapons, null, new NearestVisibleEnemy(), 100 * SimFix.Scale,
                verticalSpread: new VerticalSpread(1000)));
            Assert.Throws<ArgumentException>(() => new CombatPack(weapons, null, new NearestVisibleEnemy(), 100 * SimFix.Scale,
                body: shipped.Body, stances: shipped.Stances.ToArray(), verticalSpread: new VerticalSpread(1000, new[] { 800 })));
            Assert.Throws<ArgumentException>(() => new CombatPack(weapons, null, new NearestVisibleEnemy(), 100 * SimFix.Scale,
                body: shipped.Body, verticalSpread: new VerticalSpread(1000, new[] { 800 })));
            Assert.DoesNotThrow(() => new CombatPack(weapons, null, new NearestVisibleEnemy(), 100 * SimFix.Scale,
                body: shipped.Body, stances: shipped.Stances.ToArray(), verticalSpread: new VerticalSpread(1000, new[] { 800, 600 })));
            Assert.DoesNotThrow(() => new CombatPack(weapons, null, new NearestVisibleEnemy(), 100 * SimFix.Scale,
                body: shipped.Body, verticalSpread: new VerticalSpread(1000)));
        }

        [Test]
        public void V2_TheSpecChecksItsNumbers()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpread(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpread(4001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpread(1000, new[] { 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpread(1000, new[] { 4001 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpread(1000, null, (VerticalAim)2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpread(1000, null, VerticalAim.Center, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VerticalSpread(1000, null, VerticalAim.Center, 5001));
            Assert.DoesNotThrow(() => new VerticalSpread(1, new[] { 1, 4000 }, VerticalAim.Center, 1));
            Assert.DoesNotThrow(() => new VerticalSpread(4000, null, VerticalAim.Eye, 5000));
            var levels = new[] { 800, 600 };
            var v = new VerticalSpread(1000, levels);
            levels[0] = 1;
            Assert.That(v.StancePermille[0], Is.EqualTo(800), "the levels are copied");
        }

        // ---- V3: the digest ----

        // A layer over rwr-blast with the shipped vertical spread but no byStance (rwr-blast has no stances), first recorded by
        // this pack: it pins the section's place before the blasts (X2).
        internal const ulong BlastVerticalDigest = 3182191351449087475UL;

        [Test]
        public void V3_TheSectionAndEveryNumberChangeTheDigest()
        {
            ulong stance = RwrPack.Load("rwr-stance").BuildCombat().PolicyDigest;
            ulong with = Load(Example).BuildCombat().PolicyDigest;
            Assert.That(with, Is.Not.EqualTo(stance));
            Assert.That(RwrPack.Load("rwr-vspread").BuildCombat().PolicyDigest, Is.EqualTo(with));
            string[] variants =
            {
                Example.Replace("\"permille\": 1000", "\"permille\": 1001"), Example.Replace("800", "801"), Example.Replace("\"prone\": 600", "\"prone\": 601"),
                Example.Replace("\"snapMm\": 600", "\"snapMm\": 601"), "{ \"permille\": 1000, \"byStance\": { \"crouch\": 800, \"prone\": 600 } }",
            };
            var seen = new System.Collections.Generic.HashSet<ulong> { stance, with };
            foreach (string v in variants)
                Assert.That(seen.Add(Load(v).BuildCombat().PolicyDigest), Is.True, v);
            Assert.That(Load(Example.Replace("\"snapMm\": 600", "\"snapMm\": 601")).BuildCombat().VerticalSpread.SnapMm, Is.EqualTo(601));
        }

        [Test]
        public void V3_WithSelectorKeepsTheSection()
        {
            CombatPack pack = RwrPack.Load("rwr-vspread").BuildCombat();
            CombatPack copy = pack.WithSelector(pack.Selector);
            Assert.That(copy.PolicyDigest, Is.EqualTo(pack.PolicyDigest));
            Assert.That(copy.VerticalSpread, Is.SameAs(pack.VerticalSpread));
        }

        [Test]
        public void V3_TheSectionSitsBeforeTheBlasts()
        {
            ulong digest = Load("{ \"permille\": 1000, \"aim\": \"center\", \"snapMm\": 600 }", "rwr-blast").BuildCombat().PolicyDigest;
            Assert.That(digest, Is.Not.EqualTo(BlastDigest));
            Assert.That(digest, Is.EqualTo(BlastVerticalDigest));
        }
    }
}
