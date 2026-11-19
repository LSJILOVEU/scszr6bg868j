using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-3 D2 (the body grows up from the foot), acceptance 4 (pressure source on the ground) and acceptance 5 (the same
    // candidates, walk checks and swing hits as legacy on flat ground).
    public sealed class Ml3BodyTests
    {
        private const long M = Ml3World.M;
        private static Ml3World.Unit U(long x, long y, long z, int team, int weapon = 0) => new Ml3World.Unit(x, y, z, team, weapon);

        private static bool RayHits(Ml3World w, long y, uint mask = Ml3World.UnitLayer) =>
            w.Sim.SpatialQuery.Any(SpatialQueries.Ray(Ml3World.Raw(-5 * M, y, 0), Ml3World.Raw(M, 0, 0), SimFix.FromInt(10), mask));

        // D2: the body's bottom is the foot and its top the foot plus height, for an even and an odd height raw (odd: the top
        // is 1 raw lower). A level ray touching the bottom or the top counts (tangent is a hit); 1 raw outside misses.
        [TestCase(114688L, 114688L)]
        [TestCase(114689L, 114688L)]
        [TestCase(65536L, 65536L)]
        public void BodyStandsOnTheFoot(long height, long top)
        {
            const long Foot = 3 * M;
            var w = new Ml3World(Ml3World.Pack(Ml3World.Body(height, 32768, 0)), new[] { U(0, Foot, 0, 1, -1) });
            Assert.That(new[] { RayHits(w, Foot - 1), RayHits(w, Foot), RayHits(w, Foot + top), RayHits(w, Foot + top + 1) },
                Is.EqualTo(new[] { false, true, true, false }));
        }

        // Legacy (mask 0): the 1 m capsule is centred on the foot, half of it under the ground.
        [Test]
        public void LegacyBodyIsCentredOnTheFoot()
        {
            var w = new Ml3World(Ml3World.Pack(null), new[] { U(0, 0, 0, 1, -1) });
            Assert.That(new[] { RayHits(w, -M / 2 - 1), RayHits(w, -M / 2), RayHits(w, M / 2), RayHits(w, M / 2 + 1) },
                Is.EqualTo(new[] { false, true, true, false }));
        }

        // D2: only foot-anchored layers move. A wall box 0..2 m on the wall layer stays there in a new-mode world.
        [Test]
        public void WallLayerIsNotLifted()
        {
            var wall = new[] { new Ml3World.Wall(-M, -M, M, M, 0, 2 * M) };
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr), new[] { U(10 * M, 0, 0, 1, -1) }, wall);
            Assert.That(new[] { RayHits(w, M / 10, Ml3World.WallLayer), RayHits(w, 2 * M, Ml3World.WallLayer), RayHits(w, 2 * M + 1, Ml3World.WallLayer) },
                Is.EqualTo(new[] { true, true, false }));
        }

        // Acceptance 4 (P15): a pressed unit's source is the shooter's ground point, not the muzzle (muzzle 1.25 m, eye
        // 1.5 m, the shooter standing 3 m up). All three shot paths record the muzzle; GroundOfFireOrigin takes it off.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void PressureSourceIsTheShootersGround(int path)
        {
            CombatBody body = Ml3World.Body(114688, 98304, 81920);
            var w = new Ml3World(Ml3World.Pack(body, new RwrHitResolver()), new[] { U(0, 3 * M, 0, 1), U(10 * M, 0, 0, 2, -1) });
            var pressure = new Pressure(w.Combat, new PressureRules(3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 300), sources: true);
            w.Sim.RegisterGameplay("battle.pressure", pressure, SimSystemPhase.Queries);
            if (path == 1) w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            if (path == 2) w.Produce = c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1)));
            FireRecord f = w.StepFires().Single();
            Assert.That(f.Origin, Is.EqualTo(Ml3World.Raw(0, 3 * M + 81920, 0)));
            Assert.That(w.Combat.GroundOfFireOrigin(in f), Is.EqualTo(Ml3World.Raw(0, 3 * M, 0)));
            Assert.That(pressure.TryPressedFrom(w.Id(1), out SimVec3 from), Is.True);
            Assert.That(from, Is.EqualTo(Ml3World.Raw(0, 3 * M, 0)));
        }

        // Without a body GroundOfFireOrigin returns the origin as recorded, the explicit target shot's 0.5 m included.
        [Test]
        public void LegacyGroundOfFireOriginIsTheOrigin()
        {
            var w = new Ml3World(Ml3World.Pack(null), new[] { U(0, 0, 0, 1), U(10 * M, 0, 0, 2, -1) });
            w.Produce = c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1)));
            FireRecord f = w.StepFires().Single();
            Assert.That(w.Combat.GroundOfFireOrigin(in f), Is.EqualTo(Ml3World.Raw(0, M / 2, 0)));
        }

        // Acceptance 5: one flat layout, legacy and new mode. Units around the shooter (unit 0) at 3, 9.9, 10.2 and 10.3 m
        // and two teammates; target candidates within 10 m, the walk check and the swing hits are the same sets.
        private static Ml3World.Unit[] Layout() => new[]
        {
            U(0, 0, 0, 1, -1),
            U(3 * M, 0, 0, 2, -1), U(0, 0, 99 * M / 10, 2, -1), U(-102 * M / 10, 0, 0, 2, -1), U(0, 0, -103 * M / 10, 2, -1),
            U(5 * M, 0, 5 * M, 1, -1), U(-5 * M, 0, 5 * M + 6 * M / 10, 1, -1),
        };

        private static ulong[] Ids(Ml3World w, System.ReadOnlySpan<SpatialHit> hits)
        {
            var ids = new ulong[hits.Length];
            for (int i = 0; i < hits.Length; i++) ids[i] = w.Sim.Entities.StableOf(hits[i].Entity).Value;
            System.Array.Sort(ids);
            return ids;
        }

        [Test]
        public void CandidatesAndWalkChecksAreTheLegacySets()
        {
            var old = new Ml3World(Ml3World.Pack(null), Layout());
            var now = new Ml3World(Ml3World.Pack(Ml3World.Rwr), Layout());
            ulong[] a = Ids(old, old.Combat.Candidates(old.U(0), SimFix.FromInt(10)));
            Assert.That(Ids(now, now.Combat.Candidates(now.U(0), SimFix.FromInt(10))), Is.EqualTo(a));
            Assert.That(a.Length, Is.EqualTo(6), "self, 3 m, 9.9 m, 10.2 m and the teammates at 7.07 m and 7.52 m");
            // Walks of unit 0: through the teammate at (5, 5); along z = 5 m, 0.6 m beside the one at (-5, 5.6) (radii
            // 0.25 + 0.25 = 0.5 m); along z = 5.2 m, 0.4 m beside it; along z = 5.15 m, 0.45 m beside it (a line at the foot
            // would pass 0.51 m from the new body's axis, a line 0.5 m up 0.51 m from the legacy one's); ending on it.
            foreach (Ml3World w in new[] { old, now })
                Assert.That(new[] {
                    w.Combat.FriendOnWalk(w.U(0), w.Point(0), Ml3World.Raw(10 * M, 0, 10 * M)),
                    w.Combat.FriendOnWalk(w.U(0), Ml3World.Raw(-10 * M, 0, 5 * M), Ml3World.Raw(0, 0, 5 * M)),
                    w.Combat.FriendOnWalk(w.U(0), Ml3World.Raw(-10 * M, 0, 52 * M / 10), Ml3World.Raw(0, 0, 52 * M / 10)),
                    w.Combat.FriendOnWalk(w.U(0), Ml3World.Raw(-10 * M, 0, 515 * M / 100), Ml3World.Raw(0, 0, 515 * M / 100)),
                    w.Combat.FriendOnWalk(w.U(0), Ml3World.Raw(-8 * M, 0, 5 * M + 6 * M / 10), w.Point(6)) },
                    Is.EqualTo(new[] { true, false, true, true, true }));
        }

        // P9: the candidate sphere starts at the body centre (foot + 0.875 m). A target 9.85 m away and 3 m up: its axis
        // (3.25..4.5 m) is 10.13 m from the centre, inside 10 + 0.25; from the foot it would be 10.37 m. The other way round
        // (shooter 3 m up) it is inside too; from the shooter's foot + 1.75 m it would be 10.37 m again.
        [TestCase(0L, 3 * M)]
        [TestCase(3 * M, 0L)]
        public void CandidateSphereStartsAtTheBodyCentre(long shooterY, long targetY)
        {
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr), new[] { U(0, shooterY, 0, 1, -1), U(985 * M / 100, targetY, 0, 2, -1) });
            Assert.That(Ids(w, w.Combat.Candidates(w.U(0), SimFix.FromInt(10))).Length, Is.EqualTo(2));
        }

        // P13: a swing's sector (half height 1 m) is centred on the body centre: an enemy 1 m ahead standing 1.6 m up (body
        // 1.6..3.35 m) overlaps -0.125..1.875 m and is struck; a sector at the foot (-1..1 m) would miss it.
        [Test]
        public void SwingReachesFromTheBodyCentre()
        {
            var spec = new MeleeSpec(2, 1, 3, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(1), 4, SimFix.FromInt(2));
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr, melee: new[] { spec }), new[] { U(0, 0, 0, 1, -1), U(M, 16 * M / 10, 0, 2, -1) });
            w.Combat.SetMelee(w.U(0), 0);
            w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); };
            int landed = 0;
            for (int t = 0; t < 4; t++) { w.Step(); foreach (var s in w.Combat.Strikes) if (s.Kind == StrikeKind.Landed) landed++; }
            Assert.That(landed, Is.EqualTo(1));
        }

        // Acceptance 5, swings: a 2 m sector (half-angle 60 degrees, half height 1 m) toward +X lands on the enemies inside
        // it in both modes: (1.5, 0) and (1.2, 1.2) in, (0, 1.8) at 90 degrees and (2.4, 0) beyond reach out.
        [Test]
        public void SwingHitsAreTheLegacySet()
        {
            var spec = new MeleeSpec(2, 1, 3, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(1), 4, SimFix.FromInt(2));
            Ml3World.Unit[] units = { U(0, 0, 0, 1, -1), U(3 * M / 2, 0, 0, 2, -1), U(6 * M / 5, 0, 6 * M / 5, 2, -1), U(0, 0, 9 * M / 5, 2, -1), U(12 * M / 5, 0, 0, 2, -1) };
            ulong[] Landed(CombatBody? body)
            {
                var w = new Ml3World(Ml3World.Pack(body, melee: new[] { spec }), units);
                w.Combat.SetMelee(w.U(0), 0);
                w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); };
                var landed = new System.Collections.Generic.List<ulong>();
                for (int t = 0; t < 4; t++) { w.Step(); foreach (var s in w.Combat.Strikes) if (s.Kind == StrikeKind.Landed) landed.Add(s.Target.Value); }
                landed.Sort();
                return landed.ToArray();
            }
            ulong[] legacy = Landed(null);
            Assert.That(Landed(Ml3World.Rwr), Is.EqualTo(legacy));
            Assert.That(legacy.Length, Is.EqualTo(2));
        }
    }
}
