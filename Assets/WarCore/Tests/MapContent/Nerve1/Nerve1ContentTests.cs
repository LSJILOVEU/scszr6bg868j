using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using static WarCore.Tests.Nerve1.Nerve1Fixture;

namespace WarCore.Tests.Nerve1
{
    // NERVE-1 content: combat.status and stats.spread are read, merged whole across layers, refused when wrong, carried by the
    // combat pack's copies and written into its digest.
    public sealed class Nerve1ContentTests
    {
        private static ContentPack Over(string body, string extends = "rwr-field") => Load("n1-test", Layer("n1-test", extends, body));

        private static void Refused(string body, params string[] words)
        {
            var e = Assert.Throws<ContentException>(() => Over(body), body);
            foreach (string word in words) Assert.That(e.Message, Does.Contain(word), body);
        }

        private static void Accepted(string body) => Assert.DoesNotThrow(() => Over(body).BuildCombat(), body);

        private static string Status(string rows) => "\"combat\": { \"status\": { " + rows + " } }";
        private static string Stats(string rows) => "\"stats\": { " + rows + " }";

        [Test]
        public void N0_TheShippedLayerAndWholeReplacement()
        {
            CombatPack pack = null;
            Assert.DoesNotThrow(() => pack = ContentLoader.Load(FileContentSource.Rwr(), "rwr-nerve").BuildCombat());
            Assert.That(pack.Status, Is.Not.Null);
            Assert.That(pack.Status.PinnedPermille, Is.EqualTo(1400));
            Assert.That(pack.Status.BrokenPermille, Is.EqualTo(1800));
            Assert.That(pack.Status.WoundedBelowPermille, Is.EqualTo(500));
            Assert.That(pack.Status.WoundedPermille, Is.EqualTo(1250));
            Assert.That(pack.SpreadDragFloorPermille, Is.EqualTo(0));
            Assert.That(pack.SpreadDragCeilPermille, Is.EqualTo(3000));

            ContentPack merged = null;
            Assert.DoesNotThrow(() => merged = Load("n1-child",
                Layer("n1-parent", "rwr-field", "\"combat\": { \"status\": { \"pinned\": { \"spreadPermille\": 1400 }, "
                    + "\"wounded\": { \"belowPermille\": 500, \"spreadPermille\": 1250 } } }, \"stats\": { \"spread\": { \"dragFloorPermille\": 900, \"dragCeilPermille\": 2000 } }"),
                Layer("n1-child", "n1-parent", "\"combat\": { \"status\": { \"broken\": { \"spreadPermille\": 1700 } } }, \"stats\": { \"spread\": { \"dragCeilPermille\": 2500 } }")));
            CombatPack child = null;
            Assert.DoesNotThrow(() => child = merged.BuildCombat());
            Assert.That(child.Status.HasPinned, Is.False, "the child's status replaces the parent's whole");
            Assert.That(child.Status.HasWounded, Is.False);
            Assert.That(child.Status.BrokenPermille, Is.EqualTo(1700));
            Assert.That(child.SpreadDragFloorPermille, Is.EqualTo(0), "the child's spread row replaces the parent's whole");
            Assert.That(child.SpreadDragCeilPermille, Is.EqualTo(2500));

            CombatPack ceilOnly = null, floorOnly = null, inherited = null;
            Assert.DoesNotThrow(() => ceilOnly = Over(Stats("\"spread\": { \"dragCeilPermille\": 3000 }")).BuildCombat());
            Assert.That(ceilOnly.SpreadDragFloorPermille, Is.EqualTo(0));
            Assert.That(ceilOnly.SpreadDragCeilPermille, Is.EqualTo(3000));
            Assert.That(ceilOnly.Status, Is.Null);
            Assert.DoesNotThrow(() => floorOnly = Over(Stats("\"spread\": { \"dragFloorPermille\": 800 }")).BuildCombat());
            Assert.That(floorOnly.SpreadDragFloorPermille, Is.EqualTo(800));
            Assert.That(floorOnly.SpreadDragCeilPermille, Is.EqualTo(0));

            Assert.DoesNotThrow(() => inherited = Load("n1-plain", Layer("n1-plain", "rwr-nerve", "")).BuildCombat());
            Assert.That(inherited.PolicyDigest, Is.EqualTo(pack.PolicyDigest), "a child that writes neither inherits both");
        }

        [Test]
        public void N1_APackWithoutTheSectionsBuildsTheSameCombatPack()
        {
            CombatPack field = ContentLoader.Load(FileContentSource.Rwr(), "rwr-field").BuildCombat();
            Assert.That(field.Status, Is.Null);
            Assert.That(field.SpreadDragFloorPermille, Is.Zero);
            Assert.That(field.SpreadDragCeilPermille, Is.Zero);
            Assert.That(Over("").BuildCombat().PolicyDigest, Is.EqualTo(field.PolicyDigest));
        }

        [Test]
        public void N11_StatusRefusals()
        {
            Refused(Status(""), "status needs at least one of pinned, broken, wounded");
            Refused(Status("\"pined\": { \"spreadPermille\": 1400 }"), "pined");
            Refused(Status("\"pinned\": {}"), "spreadPermille");
            Refused(Status("\"broken\": {}"), "spreadPermille");
            Refused(Status("\"wounded\": { \"spreadPermille\": 1250 }"), "belowPermille");
            Refused(Status("\"wounded\": { \"belowPermille\": 500 }"), "spreadPermille");
            Refused(Status("\"pinned\": 1400"), "pinned");
            Refused("\"combat\": { \"status\": 3 }", "status");
            foreach (string row in new[] { "pinned", "broken" })
            {
                Refused(Status("\"" + row + "\": { \"spreadPermille\": 0 }"), "spreadPermille");
                Refused(Status("\"" + row + "\": { \"spreadPermille\": 4001 }"), "spreadPermille");
                Accepted(Status("\"" + row + "\": { \"spreadPermille\": 1 }"));
                Accepted(Status("\"" + row + "\": { \"spreadPermille\": 4000 }"));
            }
            Refused(Status("\"wounded\": { \"belowPermille\": 500, \"spreadPermille\": 0 }"), "spreadPermille");
            Refused(Status("\"wounded\": { \"belowPermille\": 500, \"spreadPermille\": 4001 }"), "spreadPermille");
            Accepted(Status("\"wounded\": { \"belowPermille\": 500, \"spreadPermille\": 1 }"));
            Accepted(Status("\"wounded\": { \"belowPermille\": 500, \"spreadPermille\": 4000 }"));
            Refused(Status("\"wounded\": { \"belowPermille\": 0, \"spreadPermille\": 1250 }"), "belowPermille");
            Refused(Status("\"wounded\": { \"belowPermille\": 1001, \"spreadPermille\": 1250 }"), "belowPermille");
            Accepted(Status("\"wounded\": { \"belowPermille\": 1, \"spreadPermille\": 1250 }"));
            Accepted(Status("\"wounded\": { \"belowPermille\": 1000, \"spreadPermille\": 1250 }"));
            Refused(Status("\"wounded\": { \"belowPermille\": 500, \"spreadPermille\": 1250, \"speedPermille\": 700 }"), "speedPermille");
            Refused(Status("\"pinned\": { \"spreadPermille\": 1400, \"below\": 3 }"), "below");
        }

        [Test]
        public void N11_StatsRefusals()
        {
            var empty = Assert.Throws<ContentException>(() => Over(Stats("")));
            Assert.That(empty.Message, Is.EqualTo("content layer 'n1-test' stats {} must not be empty"));
            Accepted(Stats("\"spread\": { \"dragFloorPermille\": 1000 }"));
            Refused(Stats("\"spread\": {}"), "dragFloorPermille", "dragCeilPermille");
            Refused(Stats("\"reload\": { \"dragCeilPermille\": 2000 }"), "reload");
            Refused(Stats("\"spread\": 3"), "spread");
            Refused("\"stats\": 3", "stats");
            Refused(Stats("\"spread\": { \"dragFloorPermille\": 0 }"), "dragFloorPermille");
            Refused(Stats("\"spread\": { \"dragFloorPermille\": 1001 }"), "dragFloorPermille");
            Accepted(Stats("\"spread\": { \"dragFloorPermille\": 1 }"));
            Refused(Stats("\"spread\": { \"dragCeilPermille\": 999 }"), "dragCeilPermille");
            Refused(Stats("\"spread\": { \"dragCeilPermille\": 4001 }"), "dragCeilPermille");
            Accepted(Stats("\"spread\": { \"dragCeilPermille\": 1000 }"));
            Accepted(Stats("\"spread\": { \"dragCeilPermille\": 4000 }"));
            Accepted(Stats("\"spread\": { \"dragFloorPermille\": 1000, \"dragCeilPermille\": 1000 }"));
            Refused(Stats("\"spread\": { \"dragCeilPermille\": 2000, \"floor\": 900 }"), "floor");
        }

        [Test]
        public void N11_ConstructorsCheckTheirRanges()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatus(4001, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatus(-1, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatus(0, 4001, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatus(0, 0, 500, 4001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatus(0, 0, 0, 1250), "wounded without a line");
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatus(0, 0, 1001, 1250));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatStatus(1400, 0, 500, 0), "a line without wounded");
            Assert.Throws<ArgumentException>(() => new CombatStatus(0, 0, 0, 0));
            Assert.DoesNotThrow(() => new CombatStatus(1, 4000, 1000, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Pack(null, 1001, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Pack(null, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Pack(null, 0, 999));
            Assert.Throws<ArgumentOutOfRangeException>(() => Pack(null, 0, 4001));
            Assert.DoesNotThrow(() => Pack(null, 1, 4000));
            Assert.DoesNotThrow(() => Pack(null, 1000, 1000));
        }

        [Test]
        public void N12_ASelectorCopyKeepsTheStatusAndTheClamp()
        {
            CombatPack pack = Pack(All(), 900, 3000), copy = pack.WithSelector(new NearestVisibleEnemy());
            Assert.That(copy.Status, Is.SameAs(pack.Status));
            Assert.That(copy.SpreadDragFloorPermille, Is.EqualTo(900));
            Assert.That(copy.SpreadDragCeilPermille, Is.EqualTo(3000));
            Assert.That(copy.PolicyDigest, Is.EqualTo(pack.PolicyDigest), "same selector policy: same digest");
            var sight = new WarCore.Battle.SightLimited(new NearestVisibleEnemy(), WarCore.Sim.SimFix.FromInt(40));
            CombatPack other = pack.WithSelector(sight);
            Assert.That(other.PolicyDigest, Is.Not.EqualTo(pack.PolicyDigest));
            CombatPack direct = new CombatPack(pack.Weapons.ToArray(), pack.Resolver, sight, pack.MaxHealthRaw, status: All(),
                spreadDragFloorPermille: 900, spreadDragCeilPermille: 3000);
            Assert.That(other.PolicyDigest, Is.EqualTo(direct.PolicyDigest), "only the selector changed");
        }

        [Test]
        public void N13_TheDigestTellsEveryNumberApart()
        {
            ulong plain = Pack(null).PolicyDigest, all = Pack(All()).PolicyDigest;
            Assert.That(all, Is.Not.EqualTo(plain));
            Assert.That(Pack(All(1401)).PolicyDigest, Is.Not.EqualTo(all));
            Assert.That(Pack(All(b: 1801)).PolicyDigest, Is.Not.EqualTo(all));
            Assert.That(Pack(All(below: 501)).PolicyDigest, Is.Not.EqualTo(all));
            Assert.That(Pack(All(w: 1251)).PolicyDigest, Is.Not.EqualTo(all));
            Assert.That(Pack(new CombatStatus(1400, 1800, 0, 0)).PolicyDigest, Is.Not.EqualTo(all), "an absent row counts");
            ulong clamp = Pack(null, 900, 3000).PolicyDigest;
            Assert.That(clamp, Is.Not.EqualTo(plain));
            Assert.That(Pack(null, 901, 3000).PolicyDigest, Is.Not.EqualTo(clamp));
            Assert.That(Pack(null, 900, 3001).PolicyDigest, Is.Not.EqualTo(clamp));
            Assert.That(Pack(null, 0, 3000).PolicyDigest, Is.Not.EqualTo(clamp));
            Assert.That(Pack(All(), 900, 3000).PolicyDigest, Is.Not.EqualTo(clamp));
            Assert.That(Pack(All(), 900, 3000).PolicyDigest, Is.Not.EqualTo(all));

            // With blasts too: both the blast numbers and the status numbers count.
            BlastRules blasts = new BlastRules(new[] { new BlastSpec("frag", 1000, 5000, 100, true, true) }, 4, 64);
            BlastRules other = new BlastRules(new[] { new BlastSpec("frag", 1000, 6000, 100, true, true) }, 4, 64);
            ulong Make(CombatStatus status, BlastRules b) => new CombatPack(Pack(null).Weapons.ToArray(), new ZeroResolver(), new NearestVisibleEnemy(),
                100 * S, blasts: b, status: status).PolicyDigest;
            ulong both = Make(All(), blasts);
            Assert.That(Make(All(), other), Is.Not.EqualTo(both));
            Assert.That(Make(All(1401), blasts), Is.Not.EqualTo(both));
            Assert.That(Make(null, blasts), Is.Not.EqualTo(both));
        }
    }
}
