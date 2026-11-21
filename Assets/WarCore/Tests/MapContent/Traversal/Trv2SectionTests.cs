using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // TRV-2 T2-01..03: the content section "traversal" and its fold; packs without it are unchanged.
    public sealed class Trv2SectionTests
    {
        private static ContentPack LoadWith(string kinds, string id = "trv-x") =>
            Trv2Maps.Load(id, (id, "{ \"id\": \"" + id + "\", \"extends\": \"rwr-m6\", \"traversal\": { \"kinds\": " + kinds + " } }"));

        private static string Kind(string id, string shape = "edge", string riseMin = "0", string riseMax = "10", string fallMin = "0", string fallMax = "10",
            string extra = "") =>
            "{ \"id\": \"" + id + "\", \"shape\": \"" + shape + "\", \"riseMin\": " + riseMin + ", \"riseMax\": " + riseMax + ", \"fallMin\": " + fallMin
            + ", \"fallMax\": " + fallMax + ", \"gapMin\": 0, \"ticksBase\": 1, \"ticksPerMetreUp\": 0, \"ticksPerMetreDown\": 0, \"pathCost\": 0" + extra + " }";

        private static string Error(string kinds) => Assert.Throws<ContentException>(() => LoadWith(kinds)).Message;

        [Test]
        public void T2_01_TheFourPlaceholderKindsReadFieldByFieldInTableOrder()
        {
            TraversalRules r = Trv2Maps.Rules;
            Assert.That(r, Is.Not.Null);
            Assert.That(r.Count, Is.EqualTo(4));
            Assert.That(Enumerable.Range(0, 4).Select(i => r[i].Id), Is.EqualTo(new[] { "step", "climb", "drop", "vault" }));
            TraversalKind climb = r[1], drop = r[2], vault = r[3];
            Assert.That(r[0].Shape, Is.EqualTo(TraversalShape.Edge));
            Assert.That(vault.Shape, Is.EqualTo(TraversalShape.Over));
            Assert.That(new[] { climb.RiseMin, climb.RiseMax, climb.FallMin, climb.FallMax, climb.GapMin }, Is.EqualTo(new long[] { 39321, 78643, 0, 19660, 131072 }));
            Assert.That(new[] { drop.RiseMin, drop.RiseMax, drop.FallMin, drop.FallMax, drop.GapMin }, Is.EqualTo(new long[] { 0, 19660, 39321, 163840, 131072 }));
            Assert.That(new[] { r[0].TicksBase, climb.TicksBase, drop.TicksBase, vault.TicksBase }, Is.EqualTo(new[] { 8, 30, 12, 24 }));
            Assert.That(new[] { r[0].PathCost, climb.PathCost, drop.PathCost, vault.PathCost }, Is.EqualTo(new long[] { 0, 2000, 1000, 2000 }));
            Assert.That(vault.NoiseRaw, Is.EqualTo(327680));
            Assert.That(r[0].NoiseRaw, Is.Zero, "noiseRaw defaults to 0");
            Assert.That(drop.Fire, Is.False);
            Assert.That(climb.Fire, Is.False, "fire defaults to false");
        }

        [Test]
        public void T2_01_FireAndPerMetreValuesAreRead()
        {
            TraversalKind k = LoadWith("[ " + Kind("a", extra: ", \"fire\": true").Replace("\"ticksPerMetreUp\": 0, \"ticksPerMetreDown\": 0", "\"ticksPerMetreUp\": 7, \"ticksPerMetreDown\": 300") + " ]")
                .SectionOrNull<TraversalRules>()[0];
            Assert.That(k.Fire, Is.True);
            Assert.That((long)k.TicksPerMetreUp * 1000 + k.TicksPerMetreDown, Is.EqualTo(7300));
        }

        [Test]
        public void T2_01_AChildLayerReplacesTheWholeTable()
        {
            ContentPack child = Trv2Maps.Load("trv-child", ("trv-child", "{ \"id\": \"trv-child\", \"extends\": \"trv-test\", \"traversal\": { \"kinds\": [ " + Kind("only") + " ] } }"));
            TraversalRules r = child.SectionOrNull<TraversalRules>();
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(r[0].Id, Is.EqualTo("only"));
            ContentPack heir = Trv2Maps.Load("trv-heir", ("trv-heir", "{ \"id\": \"trv-heir\", \"extends\": \"trv-test\" }"));
            Assert.That(heir.SectionOrNull<TraversalRules>().Count, Is.EqualTo(4), "a layer that does not write it inherits it");
        }

        [Test]
        public void T2_02_BadDataIsRejectedEachWithItsOwnMessage()
        {
            Assert.That(Error("[ " + Kind("a", riseMin: "11") + " ]"), Does.EndWith("traversal kind 'a': riseMin must not exceed riseMax"));
            Assert.That(Error("[ " + Kind("a", fallMin: "11") + " ]"), Does.EndWith("traversal kind 'a': fallMin must not exceed fallMax"));
            Assert.That(Error("[ " + Kind("a", riseMin: "-1") + " ]"), Does.EndWith("field 'riseMin': out of range [0, 536870912]"));
            Assert.That(Error("[ " + Kind("a", fallMax: "536870913") + " ]"), Does.EndWith("field 'fallMax': out of range [0, 536870912]"));
            Assert.That(Error("[ " + Kind("a").Replace("\"ticksBase\": 1", "\"ticksBase\": 0") + " ]"), Does.EndWith("field 'ticksBase': out of range [1, 300]"));
            Assert.That(Error("[ " + Kind("a").Replace("\"ticksBase\": 1", "\"ticksBase\": 301") + " ]"), Does.EndWith("field 'ticksBase': out of range [1, 300]"));
            Assert.That(Error("[ " + Kind("a").Replace("\"ticksPerMetreUp\": 0", "\"ticksPerMetreUp\": 301") + " ]"), Does.EndWith("field 'ticksPerMetreUp': out of range [0, 300]"));
            Assert.That(Error("[ " + Kind("a").Replace("\"pathCost\": 0", "\"pathCost\": 1000001") + " ]"), Does.EndWith("field 'pathCost': out of range [0, 1000000]"));
            Assert.That(Error("[ " + Kind("a", extra: ", \"noiseRaw\": 6553601") + " ]"), Does.EndWith("field 'noiseRaw': out of range [0, 6553600]"));
            Assert.That(Error("[ " + Kind("a") + ", " + Kind("a") + " ]"), Does.EndWith("traversal kind 'a' is duplicated"));
            Assert.That(Error("[ " + Kind("a", shape: "manual") + " ]"), Does.EndWith("shape 'manual' is not supported yet (hand-placed links come later)"));
            Assert.That(Error("[ " + Kind("a", shape: "ladder") + " ]"), Does.EndWith("unknown shape 'ladder' (allowed: edge, over)"));
            Assert.That(Error("[ " + string.Join(", ", Enumerable.Range(0, 33).Select(i => Kind("k" + i))) + " ]"), Does.EndWith("traversal has more than 32 kinds"));
            Assert.That(Error("[ ]"), Does.EndWith("traversal kinds [] must not be empty"));
            Assert.That(Error("[ " + Kind("a", extra: ", \"rize\": 1") + " ]"), Does.EndWith("field 'rize': unknown field name (misspelled?)"));
            Assert.That(Error("[ " + Kind("a").Replace("\"gapMin\": 0, ", "") + " ]"), Does.EndWith("is missing field 'gapMin'"));
            Assert.That(Assert.Throws<ContentException>(() => Trv2Maps.Load("t1", ("t1", "{ \"id\": \"t1\", \"extends\": \"rwr-m6\", \"traversal\": { \"kind\": [] } }"))).Message,
                Does.EndWith("traversal field 'kind': unknown field name (allowed: kinds)"));
            Assert.That(Assert.Throws<ContentException>(() => Trv2Maps.Load("t2", ("t2", "{ \"id\": \"t2\", \"extends\": \"rwr-m6\", \"traversal\": { } }"))).Message,
                Does.EndWith("traversal is missing field 'kinds'"));
        }

        [Test]
        public void T2_02_ThirtyTwoKindsAreAllowed()
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = LoadWith("[ " + string.Join(", ", Enumerable.Range(0, 32).Select(i => Kind("k" + i))) + " ]"));
            Assert.That(pack.SectionOrNull<TraversalRules>().Count, Is.EqualTo(32));
        }

        [Test]
        public void T2_02_EqualBoundsAreAllowed()
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = LoadWith("[ " + Kind("a", riseMin: "10", riseMax: "10", fallMin: "5", fallMax: "5") + " ]"));
            TraversalKind k = pack.SectionOrNull<TraversalRules>()[0];
            Assert.That(new[] { k.RiseMin, k.RiseMax, k.FallMin, k.FallMax }, Is.EqualTo(new long[] { 10, 10, 5, 5 }));
        }

        private static ulong Fold(TraversalRules r)
        {
            var hash = new StateHash();
            r.Fold(hash);
            return hash.Value;
        }

        // T2-03 as the card has it (A1): the fold is built but folded into no digest yet. The pack's digests do not change with
        // the section; the fold itself is stable and sees the table order.
        [Test]
        public void T2_03_TheSectionChangesNoPackDigestAndItsFoldSeesTheOrder()
        {
            ContentPack plain = Trv2Maps.Load("rwr-m6"), with = Trv2Maps.Test;
            Assert.That(plain.SectionOrNull<TraversalRules>(), Is.Null);
            Assert.That(with.BuildCombat().PolicyDigest, Is.EqualTo(plain.BuildCombat().PolicyDigest));
            Assert.That(with.WeaponCount, Is.EqualTo(plain.WeaponCount));
            Assert.That(with.UnitTypeCount, Is.EqualTo(plain.UnitTypeCount));
            Assert.That(with.Match.CaptureTicks, Is.EqualTo(plain.Match.CaptureTicks));
            Assert.That(with.Terrain == null, Is.EqualTo(plain.Terrain == null));

            ulong a = Fold(Trv2Maps.Rules), again = Fold(Trv2Maps.Load().SectionOrNull<TraversalRules>());
            Assert.That(again, Is.EqualTo(a), "same table, same fold");
            string swapped = Trv2Maps.KindsJson.Replace("{ \"id\": \"step\"", "{ \"id\": \"TMP\"").Replace("{ \"id\": \"climb\"", "{ \"id\": \"step\"").Replace("{ \"id\": \"TMP\"", "{ \"id\": \"climb\"");
            ContentPack renamed = LoadWith(swapped, "trv-sw");
            Assert.That(Fold(renamed.SectionOrNull<TraversalRules>()), Is.Not.EqualTo(a), "ids moved between rows");
            TraversalRules r = Trv2Maps.Rules;
            var reordered = new TraversalRules(new[] { r[1], r[0], r[2], r[3] });
            Assert.That(Fold(reordered), Is.Not.EqualTo(a), "two kinds swapped");
            var nudged = new TraversalRules(new[] { r[0], r[1], r[2], new TraversalKind("vault", TraversalShape.Over, 0, 78643, 0, 163840, 65536, 24, 0, 0, 2000, false, 327681) });
            Assert.That(Fold(nudged), Is.Not.EqualTo(a), "one field of one kind");
        }

        // D3: no shipped layer has the section, so every shipped pack reads as before.
        [Test]
        public void NoShippedLayerHasTheSection()
        {
            foreach ((string dir, string[] ids) in new[]
            {
                ("Assets/WarCore/Content/Rwr/Data", new[] { "rwr", "rwr-m6", "rwr-ai-playtest", "rwr-body", "rwr-field", "rwr-m7-playtest", "rwr-melee", "rwr-surfaces", "rwr-terrain-demo" }),
                ("Assets/WarCore/Content/Ancient/Data", new[] { "ancient", "ancient-field" }),
            })
                foreach (string id in ids)
                {
                    Assert.That(File.ReadAllText(Path.Combine(dir, id + ".json")), Does.Not.Contain("traversal"), id);
                    Assert.That(ContentLoader.Load(new FileContentSource(dir), id).SectionOrNull<TraversalRules>(), Is.Null, id);
                }
        }

        [Test]
        public void SectionOrNullIsNullWhereSectionThrows()
        {
            ContentPack plain = Trv2Maps.Load("rwr-m6");
            Assert.That(plain.SectionOrNull<TraversalRules>(), Is.Null);
            Assert.That(() => plain.Section<TraversalRules>(), Throws.TypeOf<ContentException>());
            Assert.That(Trv2Maps.Test.Section<TraversalRules>(), Is.SameAs(Trv2Maps.Test.SectionOrNull<TraversalRules>()));
        }

        // The key is accepted but stays out of the recorded "allowed" list until a shipped layer turns it on.
        [Test]
        public void TheUnknownKeyMessageIsUnchanged()
        {
            string message = Assert.Throws<ContentException>(() => Trv2Maps.Load("u1", ("u1", "{ \"id\": \"u1\", \"extends\": \"rwr\", \"traversl\": 1 }"))).Message;
            Assert.That(message, Is.EqualTo("content layer 'u1' has unknown field 'traversl' (line 1); allowed: id, extends, weapons, melee, block, unitTypes, combat, match, surfaces, slopeRule, ai, rules"));
        }

        [Test]
        public void KindsBuiltInCodeObeyTheSameBounds()
        {
            Assert.Throws<ArgumentException>(() => new TraversalKind("a", TraversalShape.Edge, 0, 1, 0, 1, 0, 0, 0, 0, 0));
            Assert.Throws<ArgumentException>(() => new TraversalKind("a", TraversalShape.Edge, 2, 1, 0, 1, 0, 1, 0, 0, 0));
            Assert.Throws<ArgumentException>(() => new TraversalKind("a", TraversalShape.Edge, 0, 1, 0, 1, -1, 1, 0, 0, 0));
            Assert.Throws<ArgumentException>(() => new TraversalKind("a", (TraversalShape)3, 0, 1, 0, 1, 0, 1, 0, 0, 0));
            Assert.Throws<ArgumentException>(() => new TraversalRules(new TraversalKind[0]));
            TraversalKind k = new TraversalKind("a", TraversalShape.Edge, 0, 1, 0, 1, 0, 1, 0, 0, 0);
            Assert.Throws<ArgumentException>(() => new TraversalRules(new[] { k, k }));
        }
    }
}
