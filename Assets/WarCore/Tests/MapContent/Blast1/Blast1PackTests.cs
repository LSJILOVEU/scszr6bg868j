using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests.Blast1
{
    // BLAST-1 B1: the combat section's blasts, their limits, and the pack digest.
    public sealed class Blast1PackTests
    {
        private const string Frag = "{ \"id\": \"t.frag\", \"innerMm\": 2000, \"outerMm\": 6000, \"damage\": 100, \"friendly\": true, \"walls\": true }";

        // A layer over rwr-field whose combat section holds the given members.
        private static ContentPack Load(string combat) =>
            Ml3Content.Load("blast-test", Ml3Content.Layer("blast-test", "rwr-field", "\"combat\": { " + combat + " }"));

        private static string Blasts(params string[] kinds) => "\"blasts\": [ " + string.Join(", ", kinds) + " ]";

        [Test]
        public void TheShippedLayerReadsBack()
        {
            CombatPack pack = RwrPack.Load("rwr-blast").BuildCombat();
            Assert.That(pack.HasBlasts, Is.True);
            BlastRules rules = pack.Blasts;
            Assert.That(rules.Kinds.Length, Is.EqualTo(1));
            BlastSpec frag = rules.Kinds[0];
            Assert.That((frag.Id, frag.InnerMm, frag.OuterMm, frag.Damage, frag.Friendly, frag.Walls), Is.EqualTo(("rwr.frag", 2000, 6000, 100, true, true)));
            Assert.That((frag.InnerRaw, frag.OuterRaw, frag.DamageRaw), Is.EqualTo((2 * SimFix.Scale, 6 * SimFix.Scale, 100L * SimFix.Scale)));
            Assert.That((rules.PerTick, rules.Queue), Is.EqualTo((4, 64)));
            Assert.That(rules.IndexOf("rwr.frag"), Is.EqualTo(0));
            Assert.That(rules.IndexOf("rwr.none"), Is.EqualTo(-1));
            // Everything else is rwr-field's.
            CombatPack field = RwrPack.Load("rwr-field").BuildCombat();
            Assert.That(field.HasBlasts, Is.False);
            Assert.That(pack.Body, Is.EqualTo(field.Body));
            Assert.That(pack.MaxHealthRaw, Is.EqualTo(field.MaxHealthRaw));
        }

        [Test]
        public void DefaultsAndExplicitLimits()
        {
            BlastRules plain = Load(Blasts(Frag)).BuildCombat().Blasts;
            Assert.That((plain.PerTick, plain.Queue), Is.EqualTo((4, 64)));
            BlastRules set = Load(Blasts(Frag) + ", \"blastPerTick\": 64, \"blastQueue\": 4096").BuildCombat().Blasts;
            Assert.That((set.PerTick, set.Queue), Is.EqualTo((64, 4096)));
            BlastRules low = Load(Blasts(Frag.Replace("2000", "0").Replace("6000", "1").Replace("100,", "1,")) + ", \"blastPerTick\": 1, \"blastQueue\": 1").BuildCombat().Blasts;
            Assert.That((low.Kinds[0].InnerMm, low.Kinds[0].OuterMm, low.Kinds[0].Damage, low.PerTick, low.Queue), Is.EqualTo((0, 1, 1, 1, 1)));
            BlastRules high = Load(Blasts(Frag.Replace("2000", "99999").Replace("6000", "100000").Replace("100,", "32767,"))).BuildCombat().Blasts;
            Assert.That((high.Kinds[0].InnerMm, high.Kinds[0].OuterMm, high.Kinds[0].Damage), Is.EqualTo((99999, 100000, 32767)));
            var kinds = new string[32];
            for (int i = 0; i < kinds.Length; i++) kinds[i] = Frag.Replace("t.frag", "t.frag" + i);
            Assert.That(Load(Blasts(kinds)).BuildCombat().Blasts.Kinds.Length, Is.EqualTo(32));
        }

        // Each case breaks one rule and names the field it broke.
        [TestCase("\"innerMm\": 2000", "\"innerMm\": 6000", "innerMm")]
        [TestCase("\"outerMm\": 6000", "\"outerMm\": 0", "outerMm")]
        [TestCase("\"outerMm\": 6000", "\"outerMm\": 100001", "outerMm")]
        [TestCase("\"damage\": 100", "\"damage\": 0", "damage")]
        [TestCase("\"damage\": 100", "\"damage\": 32768", "damage")]
        [TestCase("\"innerMm\": 2000", "\"innerMm\": -1", "innerMm")]
        [TestCase("\"walls\": true", "\"walls\": true, \"shape\": 1", "shape")]
        [TestCase("\"friendly\": true, ", "", "friendly")]
        public void ABadKindIsRefused(string from, string to, string field)
        {
            var ex = Assert.Throws<ContentException>(() => Load(Blasts(Frag.Replace(from, to))));
            StringAssert.Contains("'" + field + "'", ex.Message);
        }

        [TestCase(", \"blastPerTick\": 0", "blastPerTick")]
        [TestCase(", \"blastPerTick\": 65", "blastPerTick")]
        [TestCase(", \"blastQueue\": 0", "blastQueue")]
        [TestCase(", \"blastQueue\": 4097", "blastQueue")]
        public void ABadLimitIsRefused(string extra, string field)
        {
            var ex = Assert.Throws<ContentException>(() => Load(Blasts(Frag) + extra));
            StringAssert.Contains("'" + field + "'", ex.Message);
        }

        [Test]
        public void TheListItselfIsChecked()
        {
            StringAssert.Contains("is duplicated", Assert.Throws<ContentException>(() => Load(Blasts(Frag, Frag))).Message);
            StringAssert.Contains("'blasts'", Assert.Throws<ContentException>(() => Load(Blasts())).Message);
            var kinds = new string[33];
            for (int i = 0; i < kinds.Length; i++) kinds[i] = Frag.Replace("t.frag", "t.frag" + i);
            StringAssert.Contains("'blasts'", Assert.Throws<ContentException>(() => Load(Blasts(kinds))).Message);
            StringAssert.Contains("'blastPerTick'", Assert.Throws<ContentException>(() => Load("\"blastPerTick\": 4")).Message);
            StringAssert.Contains("'blastQueue'", Assert.Throws<ContentException>(() => Load("\"blastQueue\": 64")).Message);
            StringAssert.Contains("'blasts'", Assert.Throws<ContentException>(() => Load("\"blasts\": 3")).Message);
            // Another array in the combat section stays refused.
            StringAssert.Contains("'other'", Assert.Throws<ContentException>(() => Load(Blasts(Frag) + ", \"other\": [ 1 ]")).Message);
        }

        // A child layer's blasts replace the parent's whole.
        [Test]
        public void AChildLayerReplacesTheList()
        {
            ContentPack child = Ml3Content.Load("blast-child",
                Ml3Content.Layer("blast-parent", "rwr-field", "\"combat\": { " + Blasts(Frag, Frag.Replace("t.frag", "t.big")) + ", \"blastQueue\": 9 }"),
                Ml3Content.Layer("blast-child", "blast-parent", "\"combat\": { " + Blasts(Frag.Replace("t.frag", "t.small").Replace("100,", "7,")) + " }"));
            BlastRules rules = child.BuildCombat().Blasts;
            Assert.That(rules.Kinds.Length, Is.EqualTo(1));
            Assert.That((rules.Kinds[0].Id, rules.Kinds[0].Damage, rules.Queue), Is.EqualTo(("t.small", 7, 9)));
        }

        // The digest: a pack without blasts keeps the value it had; adding blasts or changing any number changes it.
        [Test]
        public void Digest()
        {
            Assert.That(RwrPack.Create().PolicyDigest, Is.EqualTo(0x8BE7D7DFA58533CDUL));
            Assert.That(RwrPack.CreateM6().PolicyDigest, Is.EqualTo(0x934F8562D85447D2UL));
            Assert.That(RwrPack.Load("rwr-melee").BuildCombat().PolicyDigest, Is.EqualTo(0x74B9EA11CFF86DF2UL));
            Assert.That(RwrPack.Load("rwr-field").BuildCombat().PolicyDigest, Is.EqualTo(FieldDigest));
            Assert.That(ContentLoader.Load(new FileContentSource("Assets/WarCore/Content/Ancient/Data"), "ancient").BuildCombat().PolicyDigest, Is.EqualTo(AncientDigest));
            ulong plain = Load(Blasts(Frag)).BuildCombat().PolicyDigest;
            Assert.That(plain, Is.Not.EqualTo(FieldDigest));
            Assert.That(RwrPack.Load("rwr-blast").BuildCombat().PolicyDigest, Is.Not.EqualTo(FieldDigest));
            string[] changed =
            {
                Blasts(Frag.Replace("2000", "2001")), Blasts(Frag.Replace("6000", "6001")), Blasts(Frag.Replace("100,", "99,")),
                Blasts(Frag.Replace("\"friendly\": true", "\"friendly\": false")), Blasts(Frag.Replace("\"walls\": true", "\"walls\": false")),
                Blasts(Frag) + ", \"blastPerTick\": 5", Blasts(Frag) + ", \"blastQueue\": 65", Blasts(Frag, Frag.Replace("t.frag", "t.two")),
            };
            foreach (string combat in changed)
                Assert.That(Load(combat).BuildCombat().PolicyDigest, Is.Not.EqualTo(plain), combat);
            // The id names the kind only.
            Assert.That(Load(Blasts(Frag.Replace("t.frag", "t.other"))).BuildCombat().PolicyDigest, Is.EqualTo(plain));
        }

        // Read on main before BLAST-1: rwr-field and ancient have no blasts, so these stay.
        internal const ulong FieldDigest = 0x83B866D46CB9EA79UL, AncientDigest = 0xB02D04256D7204EEUL;

        [Test]
        public void WithSelectorKeepsTheBlasts()
        {
            CombatPack pack = RwrPack.Load("rwr-blast").BuildCombat();
            CombatPack copy = pack.WithSelector(pack.Selector);
            Assert.That(copy.Blasts, Is.SameAs(pack.Blasts));
            Assert.That(copy.PolicyDigest, Is.EqualTo(pack.PolicyDigest));
        }

        // The specs refuse what the content reader refuses, for packs built in code.
        [Test]
        public void SpecsAndRulesCheckTheirArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastSpec("a", 6000, 6000, 1, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastSpec("a", -1, 6000, 1, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastSpec("a", 0, 0, 1, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastSpec("a", 0, 100001, 1, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastSpec("a", 0, 1, 0, true, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastSpec("a", 0, 1, 32768, true, true));
            Assert.Throws<ArgumentException>(() => new BlastSpec("", 0, 1, 1, true, true));
            Assert.DoesNotThrow(() => new BlastSpec("a", 99999, 100000, 32767, false, false));
            BlastSpec one = new BlastSpec("a", 0, 1, 1, true, true);
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastRules(new BlastSpec[0]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastRules(new BlastSpec[33]));
            Assert.Throws<ArgumentException>(() => new BlastRules(new[] { one, one }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastRules(new[] { one }, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastRules(new[] { one }, 65));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastRules(new[] { one }, 4, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BlastRules(new[] { one }, 4, 4097));
            Assert.DoesNotThrow(() => new BlastRules(new[] { one }, 64, 4096));
            Assert.DoesNotThrow(() => new BlastRules(new[] { one }, 1, 1));
        }
    }
}
