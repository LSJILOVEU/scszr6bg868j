using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    // The section table is the only place a new data section is registered. These tests add a section that exists only here
    // and load through the production loader with a table that has one extra row.
    public sealed class ContentSectionTableTests
    {
        internal sealed class FakeValues
        {
            internal readonly int Alpha;
            internal readonly string Beta;
            internal FakeValues(int alpha, string beta) { Alpha = alpha; Beta = beta; }
        }

        // One key "fake" with two fields: alpha (an integer) and beta (a string).
        private sealed class FakeSection : ObjectSection
        {
            internal FakeSection() : base("fake") { }

            public override void Build(SectionBuild b)
            {
                ContentEntry entry = b.Single(Key);
                if (entry == null) return;
                var r = new EntryReader(b.Pack, entry);
                int alpha = r.Int("alpha", 0, 1000);
                string beta = r.Str("beta");
                r.Done();
                b.Publish(new FakeValues(alpha, beta));
            }
        }

        private sealed class RecordingSection : ObjectSection
        {
            private readonly int stage;
            private readonly List<string> log;
            internal RecordingSection(string key, int stage, List<string> log) : base(key) { this.stage = stage; this.log = log; }
            public override int BuildStage => stage;
            public override void Build(SectionBuild b) => log.Add(Key);
        }

        private static SectionTable WithExtra(params IContentSection[] extra)
        {
            var all = new List<IContentSection>(SectionTable.Default.Sections);
            all.AddRange(extra);
            return new SectionTable(all.ToArray());
        }

        private static readonly KeyValuePair<string, string>[] Layers =
        {
            new KeyValuePair<string, string>("fake-parent", "{ \"id\": \"fake-parent\", \"extends\": \"rwr\", \"fake\": { \"alpha\": 7, \"beta\": \"from-parent\" } }"),
            new KeyValuePair<string, string>("fake-child", "{ \"id\": \"fake-child\", \"extends\": \"fake-parent\", \"fake\": { \"beta\": \"from-child\" } }"),
            new KeyValuePair<string, string>("fake-zero", "{ \"id\": \"fake-zero\", \"extends\": \"fake-parent\", \"fake\": { \"alpha\": 0 } }"),
        };

        [Test]
        public void ANewSectionIsReadThroughTheProductionLoaderAndInheritsLikeTheOthers()
        {
            var source = new FixedLocationSource(Layers);
            SectionTable table = WithExtra(new FakeSection());
            FakeValues parent = null, child = null, zero = null;
            Assert.DoesNotThrow(() => parent = ContentLoader.Load(source, "fake-parent", table).Section<FakeValues>());
            Assert.DoesNotThrow(() => child = ContentLoader.Load(source, "fake-child", table).Section<FakeValues>());
            Assert.DoesNotThrow(() => zero = ContentLoader.Load(source, "fake-zero", table).Section<FakeValues>());
            Assert.That((parent.Alpha, parent.Beta), Is.EqualTo((7, "from-parent")));
            Assert.That((child.Alpha, child.Beta), Is.EqualTo((7, "from-child")), "the child overrides beta only");
            Assert.That((zero.Alpha, zero.Beta), Is.EqualTo((0, "from-parent")), "an explicit 0 is a value");
        }

        [Test]
        public void TheNewSectionLeavesEverythingElseInThePackUntouched()
        {
            var source = new FixedLocationSource(Layers);
            ContentPack plain = ContentLoader.Load(source, "rwr");
            ContentPack extra = ContentLoader.Load(source, "fake-parent", WithExtra(new FakeSection()));
            Assert.That(extra.BuildCombat().PolicyDigest, Is.EqualTo(plain.BuildCombat().PolicyDigest));
            Assert.That(extra.WeaponCount, Is.EqualTo(plain.WeaponCount));
            Assert.That(extra.Match.CaptureTicks, Is.EqualTo(plain.Match.CaptureTicks));
            Assert.That(() => plain.Section<FakeValues>(), Throws.TypeOf<ContentException>().With.Message.EqualTo("content pack 'rwr' has no section of type FakeValues"));
        }

        [Test]
        public void WithTheDefaultTableTheNewKeyIsAnUnknownKeyWithTheRecordedMessage()
        {
            var source = new FixedLocationSource(Layers);
            string message = Assert.Throws<ContentException>(() => ContentLoader.Load(source, "fake-parent")).Message;
            Assert.That(message, Is.EqualTo("content layer 'fake-parent' has unknown field 'fake' (line 1); allowed: id, extends, weapons, melee, unitTypes, combat, match, surfaces, slopeRule"));
            string withRow = Assert.Throws<ContentException>(() => ContentLoader.Load(source, "fake-parent", WithExtra(new RecordingSection("other", 0, new List<string>())))).Message;
            Assert.That(withRow, Is.EqualTo("content layer 'fake-parent' has unknown field 'fake' (line 1); allowed: id, extends, weapons, melee, unitTypes, combat, match, surfaces, slopeRule, other"));
        }

        [Test]
        public void ANewSectionReadsItsFieldsThroughTheSameStrictReader()
        {
            var source = new FixedLocationSource(new[]
            {
                new KeyValuePair<string, string>("typo", "{ \"id\": \"typo\", \"extends\": \"rwr\", \"fake\": { \"alpha\": 1, \"beta\": \"b\", \"gamma\": 2 } }"),
                new KeyValuePair<string, string>("range", "{ \"id\": \"range\", \"extends\": \"rwr\", \"fake\": { \"alpha\": 1001, \"beta\": \"b\" } }"),
            });
            SectionTable table = WithExtra(new FakeSection());
            Assert.That(Assert.Throws<ContentException>(() => ContentLoader.Load(source, "typo", table)).Message,
                Is.EqualTo("content pack 'typo' layer 'typo' fake field 'gamma': unknown field name (misspelled?)"));
            Assert.That(Assert.Throws<ContentException>(() => ContentLoader.Load(source, "range", table)).Message,
                Is.EqualTo("content pack 'range' layer 'range' fake field 'alpha': out of range [0, 1000]"));
        }

        [Test]
        public void SectionsBuildByStageAndThenByTablePositionAndMergeInTablePosition()
        {
            var log = new List<string>();
            var source = new FixedLocationSource(new[] { new KeyValuePair<string, string>("p", "{ \"id\": \"p\", \"extends\": \"rwr\", \"x\": { }, \"y\": { }, \"z\": { } }") });
            ContentLoader.Load(source, "p", WithExtra(new RecordingSection("x", 1, log), new RecordingSection("y", 0, log), new RecordingSection("z", 0, log)));
            Assert.That(log, Is.EqualTo(new[] { "y", "z", "x" }));
            log.Clear();
            ContentLoader.Load(source, "p", WithExtra(new RecordingSection("x", 0, log), new RecordingSection("y", 0, log), new RecordingSection("z", 0, log)));
            Assert.That(log, Is.EqualTo(new[] { "x", "y", "z" }));
        }
    }
}
