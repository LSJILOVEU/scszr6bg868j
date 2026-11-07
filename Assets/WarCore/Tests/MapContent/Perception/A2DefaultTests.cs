using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests.Perception
{
    // Acceptance 1: without cover nothing changes. The constants are the ones A2BaselineTests recorded before A2.
    public sealed class A2DefaultTests
    {
        private const string Explicit = ", \"concealMilli\": 1000";

        // 1a: canonical rule bytes and the terrain digest equal the pre-A2 recordings, field absent or 1000 on every surface.
        [TestCase("", "")]
        [TestCase(Explicit, Explicit)]
        public void CanonicalBytesAndDigest_AsRecorded(string dirt, string grass)
        {
            ContentPack pack = A2.Pack(dirt, grass);
            Assert.That(A2.Hex(pack.Terrain.Canonical()), Is.EqualTo(A2BaselineTests.CanonicalHex));
            Assert.That(TerrainDerivation.Derive(A2.Map(A2.DWalls), pack).Digest, Is.EqualTo(A2BaselineTests.TablesDigest));
        }

        // 1b: the combat system keeps the recipe's own pack and fixture D runs exactly as recorded for 600 ticks.
        [TestCase("", "")]
        [TestCase(Explicit, Explicit)]
        public void FixtureD_SamePackAndRecordedRun(string dirt, string grass)
        {
            M6World w = A2.D(A2.Pack(dirt, grass));
            Assert.That(w.Combat.Pack, Is.SameAs(w.Recipe.Combat));
            Assert.That(A2.HashRun(w, 600, null, out int fires), Is.EqualTo(A2BaselineTests.DRun600));
            Assert.That(fires, Is.EqualTo(A2BaselineTests.DFires600));
        }

        // 1b tick by tick: the default and explicit-1000 worlds agree on every one of the 601 hashes, and both fold to the recording.
        [Test]
        public void FixtureD_DefaultAndExplicit_EqualEveryTick()
        {
            var plain = new List<ulong>(); var explicitAll = new List<ulong>();
            Assert.That(A2.HashRun(A2.D(A2.Pack()), 600, plain, out _), Is.EqualTo(A2BaselineTests.DRun600));
            Assert.That(A2.HashRun(A2.D(A2.Pack(Explicit, Explicit)), 600, explicitAll, out _), Is.EqualTo(A2BaselineTests.DRun600));
            Assert.That(plain.Count, Is.EqualTo(601));
            Assert.That(explicitAll, Is.EqualTo(plain));
        }

        // What the recorded run does with the grass unit (key 3): unit 2 shoots it on ticks 1, 31, 61, 121, 151, 181 and the
        // last round kills it. With grass 500 the pack is wrapped, nobody targets unit 3, and unit 3 kills unit 2 instead.
        [TestCase(false)]
        [TestCase(true)]
        public void FixtureD_GrassUnitFate(bool cover)
        {
            M6World w = A2.D(cover ? A2.Pack("", ", \"concealMilli\": 500") : A2.Pack());
            var log = new List<(int, FireRecord)>();
            ulong fold = A2.HashRun(w, 600, null, out _, log);
            StableId two = w.Sim.Entities.StableOf(w.Unit(2)), three = w.Sim.Entities.StableOf(w.Unit(3));
            var atThree = new List<int>(); var lethal = new List<(int, ulong)>();
            foreach (var (tick, f) in log)
            {
                if (f.Target.Equals(three)) { Assert.That(f.Shooter, Is.EqualTo(two)); atThree.Add(tick); }
                if (f.DamageRaw > 0) lethal.Add((tick, f.Target.Value));
            }
            if (cover)
            {
                Assert.That(w.Combat.Pack.Selector, Is.InstanceOf<ConcealedSight>());
                Assert.That(fold, Is.Not.EqualTo(A2BaselineTests.DRun600));
                Assert.That(atThree, Is.Empty);
                Assert.That(lethal, Is.EqualTo(new List<(int, ulong)> { (421, two.Value) }));
            }
            else
            {
                Assert.That(atThree, Is.EqualTo(new List<int> { 1, 31, 61, 121, 151, 181 }));
                Assert.That(lethal, Is.EqualTo(new List<(int, ulong)> { (181, three.Value) }));
            }
            Assert.That((w.Dead(1), w.Dead(2), w.Dead(3), w.Dead(4)), Is.EqualTo((false, cover, !cover, false)));
        }

        // Fixture E: shooter S (key 1, team 1) on cell (10, 10); enemies at the same distance 10 m on cells (20, 10) and
        // (10, 20); an enemy on cell (4, 10) behind the wall x = 6..6, z = 8..12; a team-1 soldier on cell (12, 4). Every
        // weapon has effect 0 (nobody dies); all units walk to the point. The wrapper's grid is 15 x 30, so x >= 15 m is
        // outside it. No surface has cover, so the world itself is not wrapped.
        private static readonly (int, int, int, int)[] EWalls = { (6, 8, 1, 5) };
        private static M6World E()
        {
            var units = new[]
            {
                A2.Soldier(1, 1, A2.Cell(10, 10), 0, 3000), A2.Soldier(2, 2, A2.Cell(20, 10), 0, 3000),
                A2.Soldier(3, 2, A2.Cell(10, 20), 0, 3000), A2.Soldier(4, 2, A2.Cell(4, 10), 0, 3000),
                A2.Soldier(5, 1, A2.Cell(12, 4), 0, 3000),
            };
            return new M6World(A2.Recipe(units, A2.Combat(0), A2.Pack()), WcMapWriter.Write(A2.Map(EWalls)));
        }

        // 1c: a wrapper forced on with every factor 1000 picks exactly what the bare selector picks, unit by unit, tick by tick.
        [Test]
        public void ForcedAll1000_MatchesBareSelectors()
        {
            M6World w = E();
            var grid = A2.Grid(15, 30);
            var all = A2.Cover(15, 30, (x, z) => 1000);
            var pairs = new (ITargetSelector bare, ITargetSelector wrapped)[]
            {
                (new NearestVisibleEnemy(), new ConcealedSight(new NearestVisibleEnemy(), grid, all)),
                (A2.Limited(), new ConcealedSight(A2.Limited(), grid, all)),
            };
            SimFix range = A2.Rifle(0).FalloffEnd;
            // The tie is real at the start: the two enemies are equally far, and the bare selector takes the lower StableId.
            Assert.That(CombatSystem.Horizontal(w.Combat.Point(w.Unit(2)) - w.Combat.Point(w.Unit(1))).LengthSquared,
                Is.EqualTo(CombatSystem.Horizontal(w.Combat.Point(w.Unit(3)) - w.Combat.Point(w.Unit(1))).LengthSquared));
            Assert.That(new NearestVisibleEnemy().TrySelect(w.Combat, w.Unit(1), range, out EntityId first), Is.True);
            StableId low = w.Sim.Entities.StableOf(w.Unit(2)).CompareTo(w.Sim.Entities.StableOf(w.Unit(3))) < 0
                ? w.Sim.Entities.StableOf(w.Unit(2)) : w.Sim.Entities.StableOf(w.Unit(3));
            Assert.That(w.Sim.Entities.StableOf(first), Is.EqualTo(low));
            // The walled enemy is within range of S but hidden.
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(4)), Is.False);
            int compared = 0, found = 0;
            for (int tick = 0; tick < 300; tick++)
            {
                if (tick > 0) w.Step();
                for (ulong key = 1; key <= 5; key++)
                    foreach (var (bare, wrapped) in pairs)
                    {
                        bool a = bare.TrySelect(w.Combat, w.Unit(key), range, out EntityId ta);
                        bool b = wrapped.TrySelect(w.Combat, w.Unit(key), range, out EntityId tb);
                        Assert.That((b, tb), Is.EqualTo((a, ta)), "tick " + tick + " unit " + key);
                        compared++; if (a) found++;
                    }
            }
            Assert.That(compared, Is.EqualTo(300 * 5 * 2));
            Assert.That(found, Is.GreaterThan(0));
        }
    }
}
