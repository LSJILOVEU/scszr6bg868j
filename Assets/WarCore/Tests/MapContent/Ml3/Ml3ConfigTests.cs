using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Perception;

namespace WarCore.Tests
{
    // ML-3 acceptance 7 (digest and saves), 8 (terrain cover map), 9 (wrong configuration throws) and D2a-1 (WithSelector).
    public sealed class Ml3ConfigTests
    {
        private const long M = Ml3World.M;
        private static Ml3World.Unit U(long x, int team, int weapon = 0) => new Ml3World.Unit(x, 0, 0, team, weapon);
        private static Ml3World.Unit[] Pair() => new[] { U(0, 1), U(10 * M, 2, -1) };

        // Acceptance 7: a body changes the digest; each of height, eye and muzzle does; a pack without one keeps the digest it
        // had before ML-3 (the WP-17 value locked by Wp13StateTests).
        [Test]
        public void BodyIsInTheDigestOnlyWhenPresent()
        {
            CombatPack plain = RwrPack.Create();
            Assert.That(plain.PolicyDigest, Is.EqualTo(0x8BE7D7DFA58533CDUL));
            Assert.That(plain.Body, Is.Null);
            CombatPack With(CombatBody body) => new CombatPack(plain.Weapons.ToArray(), plain.Resolver, plain.Selector, plain.MaxHealthRaw, body: body);
            ulong rwr = With(Ml3World.Rwr).PolicyDigest;
            Assert.That(rwr, Is.Not.EqualTo(plain.PolicyDigest));
            Assert.That(new[] { With(Ml3World.Body(114690, 98304, 98304)).PolicyDigest, With(Ml3World.Body(114688, 98306, 98304)).PolicyDigest,
                With(Ml3World.Body(114688, 98304, 98306)).PolicyDigest }, Has.None.EqualTo(rwr));
            Assert.That(With(Ml3World.Rwr).PolicyDigest, Is.EqualTo(rwr), "stable");
        }

        // Writing the legacy numbers as a body ({ 1, 0.5, 0 }) is still new mode: other digest, and the body grows from the foot.
        [Test]
        public void LegacyNumbersAsABodyAreNotLegacy()
        {
            CombatBody same = Ml3World.Body(M, M / 2, 0);
            var w = new Ml3World(Ml3World.Pack(same), Pair());
            var old = new Ml3World(Ml3World.Pack(null), Pair());
            Assert.That(w.UsedPack.PolicyDigest, Is.Not.EqualTo(old.UsedPack.PolicyDigest));
            Assert.That(w.Sim.FootAnchoredLayers, Is.EqualTo(1u));
            // A ray from x = 5 m along y = -0.25 m passes under the target in new mode (its body is 0..1 m) and hits it in legacy (-0.5..0.5 m).
            SpatialRequest low = SpatialQueries.Ray(Ml3World.Raw(5 * M, -M / 4, 0), Ml3World.Raw(M, 0, 0), SimFix.FromInt(20), Ml3World.UnitLayer);
            Assert.That((w.Sim.SpatialQuery.Nearest(low, out SpatialHit a), a.Entity), Is.EqualTo((false, default(EntityId))));
            Assert.That(old.Sim.SpatialQuery.Nearest(low, out SpatialHit b) && b.Entity.Equals(old.U(1)), Is.True);
        }

        // Acceptance 7: saves of the two modes refuse each other; a new-mode save restores into a new-mode world.
        [Test]
        public void SavesOfTheTwoModesRefuseEachOther()
        {
            var a = new Ml3World(Ml3World.Pack(Ml3World.Rwr), Pair());
            var b = new Ml3World(Ml3World.Pack(null), Pair());
            a.Step(); b.Step();
            byte[] save = a.Sim.SaveRetainedSnapshot(), legacySave = b.Sim.SaveRetainedSnapshot();
            // A refused restore leaves the target unusable, so each refusal gets a fresh world.
            Assert.Throws<InvalidOperationException>(() => new Ml3World(Ml3World.Pack(null), Pair()).Sim.RestoreRetainedSnapshot(save));
            Assert.Throws<InvalidOperationException>(() => new Ml3World(Ml3World.Pack(Ml3World.Rwr), Pair()).Sim.RestoreRetainedSnapshot(legacySave));
            var c = new Ml3World(Ml3World.Pack(Ml3World.Rwr), Pair());
            c.Sim.RestoreRetainedSnapshot(save);
            Assert.That(c.Sim.SaveRetainedSnapshot(), Is.EqualTo(save));
            for (int t = 0; t < 40; t++) { a.Step(); c.Step(); Assert.That(c.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick " + t); }
        }

        // D2a-1: WithSelector passes the body on unchanged (and an absent body stays absent).
        [Test]
        public void WithSelectorKeepsTheBody()
        {
            CombatPack pack = Ml3World.Pack(Ml3World.Rwr);
            CombatPack copy = pack.WithSelector(new SightLimited(new NearestVisibleEnemy(), SimFix.FromInt(20)));
            Assert.That(copy.Body, Is.EqualTo(pack.Body));
            Assert.That(Ml3World.Pack(null).WithSelector(new NearestVisibleEnemy()).Body, Is.Null);
        }

        // Acceptance 8: the body pack on a terrain map with grass (cover 500): the combat system runs the ConcealedSight copy,
        // which keeps the body; its digest is the recipe pack's WithSelector of that selector, not the same pack without a
        // body; and shots start at the muzzle.
        [Test]
        public void TerrainCoverMapKeepsTheBody()
        {
            Content.ContentPack content = Ml3Content.Load("ml3-a2", Ml3Content.Layer("ml3-a2", "rwr",
                "\"surfaces\": [ { \"id\": \"dirt\" }, { \"id\": \"grass\", \"speedMilli\": 800, \"concealMilli\": 500 } ], "
                + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }, \"combat\": { " + Ml3Content.BodyJson + " }"));
            CombatPack recipePack = content.BuildCombat();
            Assert.That(recipePack.Body, Is.EqualTo(Ml3World.Rwr));
            var units = A2.DUnits().Select(u => new UnitSpawn(u.Key, 0, u.Squad, u.XMm, u.ZMm, u.SpeedMmPerSec, u.Team, 0)).ToArray();
            var w = new M6World(A2.Recipe(units, recipePack, content), WcMapWriter.Write(A2.Map(A2.DWalls)));
            CombatPack used = w.Combat.Pack;
            Assert.That(used.Selector, Is.InstanceOf<ConcealedSight>());
            Assert.That(used.Body, Is.EqualTo(Ml3World.Rwr));
            Assert.That(used.PolicyDigest, Is.EqualTo(w.Recipe.Combat.WithSelector(used.Selector).PolicyDigest));
            CombatPack bare = new CombatPack(used.Weapons.ToArray(), used.Resolver, used.Selector, used.MaxHealthRaw,
                used.FriendlyFire, used.FriendlyFireLocked, used.AllyCheckMissRaw, used.AllyHoldMilli);
            Assert.That(used.PolicyDigest, Is.Not.EqualTo(bare.PolicyDigest));
            int fires = 0;
            for (int t = 0; t < 300 && fires == 0; t++)
            {
                w.Sim.Step(default);
                foreach (FireRecord f in w.Combat.Fires)
                {
                    fires++;
                    EntityId shooter = w.Sim.Entities.Resolve(new EntityLink(f.Shooter));
                    SimVec3 foot = w.Sim.Entities.Read(w.Sim.Entities.Resolve<Position>(), shooter).Value;
                    Assert.That(f.Origin.Y.Raw, Is.EqualTo(foot.Y.Raw + 98304));
                }
            }
            Assert.That(fires, Is.GreaterThan(0));
            Assert.That(w.Sim.FootAnchoredLayers, Is.EqualTo(1u));
            Assert.That(w.Sim.Entities.Read(w.Sim.Entities.Resolve<Collider>(), w.Unit(1)).Height.Raw, Is.EqualTo(114688));
        }

        // Acceptance 9 (a) and (b): the pack and the spatial index disagree about the body; also a wall layer anchored.
        [TestCase(true, 0u, 0u)]
        [TestCase(false, 1u, 0u)]
        [TestCase(true, 3u, 2u)]
        public void MaskDisagreementThrowsAtConstruction(bool bodied, uint anchored, uint wallMask)
        {
            var units = Pair();
            Ml3World.Wall[] walls = wallMask == 0 ? null : new[] { new Ml3World.Wall(4 * M, -M, 5 * M, M, 0, 3 * M) };
            Assert.Throws<InvalidOperationException>(() => new Ml3World(Ml3World.Pack(bodied ? Ml3World.Rwr : (CombatBody?)null), units, walls, anchored: anchored));
            Assert.DoesNotThrow(() => new Ml3World(Ml3World.Pack(bodied ? Ml3World.Rwr : (CombatBody?)null), units, walls));
        }

        // A combat system built before the spatial index sees no anchored layer: legacy passes, a body pack is refused.
        [Test]
        public void CombatBeforeTheIndexCountsAsUnanchored()
        {
            Assert.That(() => new M5TestWorld(M5TestWorld.Pair(), Ml3World.Pack(Ml3World.Rwr)), Throws.InvalidOperationException);
            Assert.DoesNotThrow(() => new M5TestWorld(M5TestWorld.Pair(), Ml3World.Pack(null)));
        }

        // Acceptance 9 (c): a unit whose collider is not the body height throws the first time it shoots, even though no ray
        // ever reaches it as a target. A legacy pack never checks.
        [Test]
        public void WrongColliderHeightThrowsWhenTheUnitShoots()
        {
            // Unit 2 (team 1) is the wrong one: it stands far behind and only shoots.
            var units = new[] { U(0, 1), U(10 * M, 2, -1), new Ml3World.Unit(-30 * M, 0, 0, 1) };
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr), units, heights: new[] { 114688L, 114688L, 65536L });
            Assert.Throws<InvalidOperationException>(() => w.Step());
            var right = new Ml3World(Ml3World.Pack(Ml3World.Rwr), units);
            Assert.DoesNotThrow(() => right.Step());
            var old = new Ml3World(Ml3World.Pack(null), units, heights: new[] { M, M, 114688L });
            Assert.DoesNotThrow(() => old.Step());
        }
    }
}
