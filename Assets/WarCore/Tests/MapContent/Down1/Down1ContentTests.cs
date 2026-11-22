using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D1: combat.downed and a blast's stunTicks read from content (the shipped example, both ends of every range, each
    // refusal naming its field), the pack digest (older layers unchanged, every field in it) and the default-off worlds.
    public sealed class Down1ContentTests
    {
        // The PolicyDigest of the older layers read on main before DOWN-1 changed any code.
        internal const ulong FieldDigest = 9491449277226543737UL, StanceDigest = 5280425556807205711UL, BlastDigest = 2272965899723657996UL;

        internal static string Downed(string when = "\"lethal\"", string ticks = "1800", string stance = "\"prone\"", string crawl = "150",
            string finish = "true", string finishAfter = "15", string stun = "60", string targetable = "false", string extra = "")
        {
            var parts = new List<string>();
            if (when != null) parts.Add("\"when\": " + when);
            if (ticks != null) parts.Add("\"ticks\": " + ticks);
            if (stance != null) parts.Add("\"stance\": " + stance);
            if (crawl != null) parts.Add("\"crawlPermille\": " + crawl);
            if (finish != null) parts.Add("\"finish\": " + finish);
            if (finishAfter != null) parts.Add("\"finishAfterTicks\": " + finishAfter);
            if (stun != null) parts.Add("\"stunTicks\": " + stun);
            if (targetable != null) parts.Add("\"targetable\": " + targetable);
            if (extra.Length > 0) parts.Add(extra);
            return "{ " + string.Join(", ", parts) + " }";
        }

        // A layer over parent whose combat section writes this downed object.
        internal static ContentPack Load(string downed, string parent = "rwr-stance") =>
            Ml3Content.Load("down1", Ml3Content.Layer("down1", parent, "\"combat\": { \"downed\": " + downed + " }"));

        // A layer over rwr-field with the frag blast, its stunTicks written as given (null: not written).
        internal static ContentPack Frag(string stun) =>
            Ml3Content.Load("down1b", Ml3Content.Layer("down1b", "rwr-field", "\"combat\": { \"blasts\": [ { \"id\": \"rwr.frag\", \"innerMm\": 2000, "
                + "\"outerMm\": 6000, \"damage\": 100, \"friendly\": true, \"walls\": true" + (stun == null ? "" : ", \"stunTicks\": " + stun)
                + " } ], \"blastPerTick\": 4, \"blastQueue\": 64 }"));

        private static void Refused(Func<object> load, string field)
        {
            ContentException e = Assert.Throws<ContentException>(() => load());
            StringAssert.Contains("'" + field + "'", e.Message);
        }

        private static void RefusedDowned(string downed, string field, string parent = "rwr-stance")
        {
            ContentException e = Assert.Throws<ContentException>(() => Load(downed, parent).BuildCombat());
            StringAssert.Contains("combat.downed", e.Message);
            StringAssert.Contains("'" + field + "'", e.Message);
        }

        private static DownedRules Accepted(string downed)
        {
            DownedRules r = null;
            Assert.DoesNotThrow(() => r = Load(downed).BuildCombat().Downed);
            return r;
        }

        [Test]
        public void D1_TheShippedLayerReadsAsTheCardSays()
        {
            CombatPack pack = RwrPack.Load("rwr-down").BuildCombat();
            DownedRules d = pack.Downed;
            Assert.That(d.When, Is.EqualTo(DownedWhen.Lethal));
            Assert.That(d.Ticks, Is.EqualTo(1800));
            Assert.That(d.StanceLevel, Is.EqualTo(2), "prone, the second stance");
            Assert.That(d.CrawlPermille, Is.EqualTo(150));
            Assert.That(d.Finish, Is.True);
            Assert.That(d.FinishAfterTicks, Is.EqualTo(15));
            Assert.That(d.StunTicks, Is.EqualTo(60));
            Assert.That(d.Targetable, Is.False);
            Assert.That(pack.Blasts.Kinds[0].StunTicks, Is.EqualTo(60));
            Assert.That(pack.UsesCasualties, Is.True);
        }

        [Test]
        public void D1_OptionalFieldsTakeTheirDefaults()
        {
            DownedRules d = Accepted(Downed(when: "\"gear\"", stance: null, finish: null, finishAfter: null, stun: null, targetable: null));
            Assert.That(d.When, Is.EqualTo(DownedWhen.Gear));
            Assert.That((d.StanceLevel, d.Finish, d.FinishAfterTicks, d.StunTicks, d.Targetable), Is.EqualTo((0, true, 0, 0, true)));
            Assert.That(Accepted(Downed(stance: "\"crouch\"")).StanceLevel, Is.EqualTo(1));
            Assert.That(Accepted(Downed(finish: "false")).Finish, Is.False);
        }

        [Test]
        public void D1_BothEndsOfEveryRangeAreTaken()
        {
            Assert.That(Accepted(Downed(ticks: "1", finishAfter: "1")).Ticks, Is.EqualTo(1));
            Assert.That(Accepted(Downed(ticks: "1", finishAfter: "1")).FinishAfterTicks, Is.EqualTo(1));
            Assert.That(Accepted(Downed(ticks: "108000")).Ticks, Is.EqualTo(108000));
            Assert.That(Accepted(Downed(crawl: "1")).CrawlPermille, Is.EqualTo(1));
            Assert.That(Accepted(Downed(crawl: "1000")).CrawlPermille, Is.EqualTo(1000));
            Assert.That(Accepted(Downed(finishAfter: "0")).FinishAfterTicks, Is.EqualTo(0));
            Assert.That(Accepted(Downed(finishAfter: "1800")).FinishAfterTicks, Is.EqualTo(1800));
            Assert.That(Accepted(Downed(stun: "0")).StunTicks, Is.EqualTo(0));
            Assert.That(Accepted(Downed(stun: "600")).StunTicks, Is.EqualTo(600));
            Assert.That(Frag("0").BuildCombat().Blasts.Kinds[0].StunTicks, Is.EqualTo(0));
            Assert.That(Frag("600").BuildCombat().Blasts.Kinds[0].StunTicks, Is.EqualTo(600));
        }

        [Test]
        public void D1_OneStepPastEitherEndIsRefused()
        {
            RefusedDowned(Downed(ticks: "0", finishAfter: "0"), "ticks");
            RefusedDowned(Downed(ticks: "108001"), "ticks");
            RefusedDowned(Downed(crawl: "0"), "crawlPermille");
            RefusedDowned(Downed(crawl: "1001"), "crawlPermille");
            RefusedDowned(Downed(finishAfter: "-1"), "finishAfterTicks");
            RefusedDowned(Downed(finishAfter: "1801"), "finishAfterTicks");
            RefusedDowned(Downed(stun: "-1"), "stunTicks");
            RefusedDowned(Downed(stun: "601"), "stunTicks");
            Refused(() => Frag("-1").BuildCombat(), "stunTicks");
            Refused(() => Frag("601").BuildCombat(), "stunTicks");
        }

        [Test]
        public void D1_OtherRefusals()
        {
            RefusedDowned(Downed(when: "\"always\""), "when");
            RefusedDowned(Downed(when: null), "when");
            RefusedDowned(Downed(ticks: null), "ticks");
            RefusedDowned(Downed(crawl: null), "crawlPermille");
            RefusedDowned(Downed(stance: "\"kneel\""), "stance");
            RefusedDowned(Downed(), "stance", "rwr-field");
            RefusedDowned(Downed(extra: "\"reload\": 1"), "reload");
            Refused(() => Load("1800").BuildCombat(), "downed");
        }

        // The DownedRules constructor checks the same ranges.
        [Test]
        public void D1_TheRulesRefuseOutOfRangeNumbers()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownedRules(DownedWhen.Lethal, 0, 150));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownedRules(DownedWhen.Lethal, 108001, 150));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownedRules(DownedWhen.Lethal, 10, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownedRules(DownedWhen.Lethal, 10, 1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownedRules(DownedWhen.Lethal, 10, 150, finishAfterTicks: 11));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownedRules(DownedWhen.Lethal, 10, 150, stunTicks: 601));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownedRules((DownedWhen)3, 10, 150));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastSpec("t", 1000, 2000, 1, true, true, 601));
            Assert.Throws<ArgumentException>(() => new CombatPack(new[] { Down1World.Gun() }, new ScriptedDamage(), new NearestVisibleEnemy(), 100,
                downed: new DownedRules(DownedWhen.Lethal, 10, 150, stanceLevel: 1)));
        }

        [Test]
        public void D1_TheOlderDigestsAreUnchanged()
        {
            Assert.That(RwrPack.Load("rwr-field").BuildCombat().PolicyDigest, Is.EqualTo(FieldDigest));
            Assert.That(RwrPack.Load("rwr-stance").BuildCombat().PolicyDigest, Is.EqualTo(StanceDigest));
            Assert.That(RwrPack.Load("rwr-blast").BuildCombat().PolicyDigest, Is.EqualTo(BlastDigest));
            Assert.That(Frag(null).BuildCombat().PolicyDigest, Is.EqualTo(BlastDigest));
            Assert.That(Frag("0").BuildCombat().PolicyDigest, Is.EqualTo(BlastDigest), "stunTicks 0 is not written");
            Assert.That(Frag("60").BuildCombat().PolicyDigest, Is.Not.EqualTo(BlastDigest));
        }

        [Test]
        public void D1_DownedAndEveryFieldChangeTheDigest()
        {
            var seen = new HashSet<ulong> { StanceDigest };
            string[] variants =
            {
                Downed(), Downed(when: "\"gear\""), Downed(ticks: "1799"), Downed(stance: "\"crouch\""), Downed(stance: null), Downed(crawl: "151"),
                Downed(finish: "false"), Downed(finishAfter: "16"), Downed(stun: "61"), Downed(targetable: "true"),
            };
            foreach (string v in variants) Assert.That(seen.Add(Load(v).BuildCombat().PolicyDigest), Is.True, v);
            Assert.That(RwrPack.Load("rwr-down").BuildCombat().PolicyDigest, Is.Not.EqualTo(StanceDigest));
        }

        [Test]
        public void D1_WithSelectorKeepsTheDownedRules()
        {
            CombatPack pack = RwrPack.Load("rwr-down").BuildCombat();
            CombatPack other = pack.WithSelector(new NearestVisibleEnemy());
            Assert.That(other.Downed, Is.SameAs(pack.Downed));
            Assert.That(other.PolicyDigest, Is.EqualTo(pack.PolicyDigest));
            Assert.That(other.UsesCasualties, Is.True);
        }

        // Units: key 1 (team 1) at (5, 8) m, key 2 (team 2) at (10, 8) m, never ordered.
        private static SimSession Plain(string layer) =>
            World(Pack(layer), new[] { Soldier(1, 1, 5000, 8000, 1), Soldier(2, 2, 10000, 8000, 2) }, Map());

        [TestCase("rwr-field")]
        [TestCase("rwr-stance")]
        [TestCase("rwr-blast")]
        public void D1_AWorldWithoutCasualtiesHasNoneOfTheParts(string layer)
        {
            Assert.DoesNotThrow(() =>
            {
                SimSession s = Plain(layer);
                WarCore.Sim.Sim sim = SimOf(s);
                CombatSystem combat = CombatOf(s);
                Assert.That(() => sim.Entities.Resolve<Casualty>(), Throws.Exception);
                Assert.That(combat.HasCasualties, Is.False);
                Assert.That(combat.Pack.UsesCasualties, Is.False);
                // Orders 0 and 33 are free: a fixture system takes them.
                Assert.DoesNotThrow(() => Run(s, 99, SimSystemPhase.Requests, 0, c => { }));
                Assert.DoesNotThrow(() => Run(s, 99, SimSystemPhase.Queries, 33, c => { }));
                Kill(s, 2, 2);
                for (int t = 0; t < 4; t++)
                {
                    foreach (ulong key in new ulong[] { 1, 2 })
                    {
                        EntityId u = Unit(s, key);
                        Assert.That(combat.CannotAct(u), Is.EqualTo(combat.IsDead(u)));
                        Assert.That(combat.IsDowned(u) || combat.IsStunned(u), Is.False);
                        Assert.That(combat.Targetable(u), Is.True);
                        foreach (UnitAction a in Enum.GetValues(typeof(UnitAction)))
                            if (a != UnitAction.None) Assert.That(combat.Allows(u, a), Is.True, layer + " " + a);
                    }
                    s.StepOnce();
                }
                Assert.That(combat.IsDead(Unit(s, 2)), Is.True);
                Assert.That(combat.GiveUp(Unit(s, 1)), Is.False);
            });
        }

        // Without stances there is no action gate and the motion model is not wrapped in a speed line.
        [TestCase("rwr-field")]
        [TestCase("rwr-blast")]
        public void D1_WithoutStancesThereIsNoGateNorSpeedLine(string layer)
        {
            Assert.DoesNotThrow(() =>
            {
                SimSession s = Plain(layer);
                Assert.That(s.Gate, Is.Null);
                Assert.That(M3TestWorld.Locomotion(s).Motion, Is.Not.InstanceOf<SpeedLine>());
            });
        }
    }
}
