using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A3-A6, A9: flight, worked by hand. Shooter foot at the origin, muzzle 1.5 m; targets stand on z = 0 facing it.
    public sealed class Proj1FlightTests
    {
        private const long M = Proj1World.M, Eye = 98304;
        // A shot every 100 s: one projectile per test.
        private static WeaponDef Slow(int falloffEnd = 60) => Proj1World.Weapon(falloffEnd, intervalMilli: 3_000_000);

        private static Proj1World World(int speed, Proj1World.Unit[] units, Ml3World.Wall[] walls = null, HeightField ground = null,
            PlateField plates = null, int falloffEnd = 60) =>
            new Proj1World(Proj1World.Pack(speed, Slow(falloffEnd)), units, walls, ground, plates);

        private static void AimAt(Proj1World w, int target) => w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(target)));
        private static void AimAt(Proj1World w, SimVec3 point) => w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), point));

        // A3: fired on tick 1 at a target 10 m away (front of its 0.25 m body at 9.75 m): 1 m a tick lands on tick 11, 0.5 m a
        // tick (step 32768) on tick 21 (9.75 / 0.5 = 19.5 -> segment 20), 100 000 mm/s (step 218 453, 3 x 218 453 = 655 359 raw
        // > 638 976) on tick 4. Full health every tick before.
        [TestCase(30000, 11L)]
        [TestCase(15000, 21L)]
        [TestCase(100000, 4L)]
        public void LandsOnTheHandWorkedTick(int speed, long tick)
        {
            Proj1World w = World(speed, new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1) });
            long full = w.Health(1);
            for (long t = 1; t < tick; t++)
            {
                w.Step();
                Assert.That(w.Projectiles.Impacts.Length, Is.Zero, "tick " + t);
                Assert.That(w.Health(1), Is.EqualTo(full), "tick " + t);
                Assert.That(w.Projectiles.Count, Is.EqualTo(1), "tick " + t);
            }
            ProjectileImpact hit = w.StepImpacts().Single();
            Assert.That(w.Tick, Is.EqualTo(tick));
            Assert.That((hit.Outcome, hit.Target, hit.Launcher, hit.LaunchTick), Is.EqualTo((FireOutcome.Hit, w.Id(1), w.Id(0), 1L)));
            Assert.That(hit.End.X.Raw, Is.InRange(638976 - 4, 638976 + 4), "front of the body");
            Assert.That(w.Health(1), Is.LessThan(full));
            Assert.That(w.Projectiles.Count, Is.Zero, "a landed projectile leaves the pool");
            long after = w.Health(1);
            for (int t = 0; t < 5; t++) { w.Step(); Assert.That(w.Projectiles.Impacts.Length, Is.Zero); }
            Assert.That(w.Health(1), Is.EqualTo(after), "no second landing");
        }

        // Nothing moves on the firing tick: an enemy 0.6 m behind the muzzle is never struck (a segment on the firing tick would
        // run from 1 m behind the muzzle to it); the projectile lands on the target ahead on tick 11.
        [Test]
        public void NothingFliesOnTheFiringTick()
        {
            Proj1World w = World(30000, new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1), Proj1World.U(-3 * M / 5, 0, 2, -1) });
            long full = w.Health(2);
            AimAt(w, 1);
            (long tick, ProjectileImpact hit) = w.StepUntilImpact();
            Assert.That((tick, hit.Outcome, hit.Target), Is.EqualTo((11L, FireOutcome.Hit, w.Id(1))));
            Assert.That(w.Health(2), Is.EqualTo(full));
        }

        // The position after k ticks is origin + k * step, never accumulated: at 100 000 mm/s after 2 ticks 2 x 218 453 raw.
        [Test]
        public void PositionIsAMultipleOfTheStep()
        {
            Proj1World w = World(100000, new[] { Proj1World.U(0, 0, 1), Proj1World.U(50 * M, 0, 2, -1) });
            w.Step(); w.Step(); w.Step();
            ProjectileView p = w.Projectiles.InFlight(0);
            Assert.That(p.Step, Is.EqualTo(Proj1World.Raw(218453, 0, 0)));
            Assert.That(p.PositionAt(w.Projectiles.LastTick), Is.EqualTo(Proj1World.Raw(2 * 218453, Eye, 0)));
        }

        // A4: the target steps off the line before the projectile arrives: it flies its whole 60 m and ends Open; nobody is hurt.
        [Test]
        public void TargetThatStepsAsideIsMissed()
        {
            Proj1World w = World(30000, new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1) });
            long full = w.Health(1);
            w.Step(); w.Step();
            w.Move(1, 10 * M, 0, 5 * M);
            (long tick, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That(tick, Is.EqualTo(61L));
            Assert.That((end.Outcome, end.Target), Is.EqualTo((FireOutcome.Open, default(StableId))));
            Assert.That(end.End, Is.EqualTo(Proj1World.Raw(60 * M, Eye, 0)));
            Assert.That(w.Health(1), Is.EqualTo(full));
        }

        // A4: another enemy walks onto the line half way: it is struck (front at 4.75 m: segment 5, tick 6).
        [Test]
        public void UnitThatStepsIntoTheLineIsStruck()
        {
            Proj1World w = World(30000, new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1), Proj1World.U(5 * M, 10 * M, 2, -1) });
            w.Step(); w.Step();
            w.Move(2, 5 * M, 0, 0);
            (long tick, ProjectileImpact hit) = w.StepUntilImpact();
            Assert.That((tick, hit.Outcome, hit.Target), Is.EqualTo((6L, FireOutcome.Hit, w.Id(2))));
        }

        // Review advice 3: a unit standing on the projectile's current point is struck on the next tick, at distance 0.
        [Test]
        public void UnitOnTheProjectilesPointIsStruckNextTick()
        {
            Proj1World w = World(30000, new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1), Proj1World.U(3 * M, 10 * M, 2, -1) });
            for (int t = 0; t < 4; t++) w.Step();
            Assert.That(w.Projectiles.InFlight(0).PositionAt(w.Tick), Is.EqualTo(Proj1World.Raw(3 * M, Eye, 0)));
            w.Move(2, 3 * M, 0, 0);
            ProjectileImpact hit = w.StepImpacts().Single();
            Assert.That((hit.Outcome, hit.Target, hit.End), Is.EqualTo((FireOutcome.Hit, w.Id(2), Proj1World.Raw(3 * M, Eye, 0))));
        }

        // A5: a 0.2 m wall at 5.5 m stops even the fastest projectile (33.3 m a tick) on its near face; the target behind it is
        // not hurt. Aimed at the target's foot (the line ends at its eye height, so it runs level).
        [TestCase(1_000_000, 2L)]
        [TestCase(30000, 7L)]
        public void WallStopsTheProjectile(int speed, long tick)
        {
            var wall = new[] { new Ml3World.Wall(11 * M / 2, -2 * M, 11 * M / 2 + M / 5, 2 * M, 0, 3 * M) };
            Proj1World w = World(speed, new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1) }, wall);
            long full = w.Health(1);
            AimAt(w, 1);
            (long at, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That((at, end.Outcome), Is.EqualTo((tick, FireOutcome.WallBlocked)));
            Assert.That(end.End.X.Raw, Is.InRange(11 * M / 2 - 4, 11 * M / 2 + 4));
            w.Step();
            Assert.That(w.Health(1), Is.EqualTo(full));
        }

        // A unit whose body reaches into the wall from behind, its front exactly on the wall's near face: the tie goes to the wall.
        [Test]
        public void TieBetweenWallAndUnitGoesToTheWall()
        {
            var wall = new[] { new Ml3World.Wall(11 * M / 2, -2 * M, 11 * M / 2 + M / 5, 2 * M, 0, 3 * M) };
            Proj1World w = World(30000, new[] { Proj1World.U(0, 0, 1), Proj1World.U(23 * M / 4, 0, 2, -1) }, wall);
            long full = w.Health(1);
            AimAt(w, 1);
            (long _, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That(end.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            w.Step();
            Assert.That(w.Health(1), Is.EqualTo(full));
        }

        // A6: a 1.75 m ridge on the grid line x = 10 m stops the level 1.5 m line where it goes under (first blocked sample x = 10).
        [Test]
        public void RidgeStopsTheProjectile()
        {
            var points = new int[21 * 6];
            for (int j = 0; j <= 5; j++) points[j * 21 + 10] = (int)(7 * M / 4);
            var ground = new HeightField(20, 5, default, default, points);
            Proj1World w = World(30000, new[] { Proj1World.U(5 * M / 2, 5 * M / 2, 1), Proj1World.U(35 * M / 2, 5 * M / 2, 2, -1) }, ground: ground);
            long full = w.Health(1);
            AimAt(w, 1);
            (long tick, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That((tick, end.Outcome, end.End), Is.EqualTo((9L, FireOutcome.WallBlocked, Proj1World.Raw(10 * M, Eye, 5 * M / 2))));
            w.Step();
            Assert.That(w.Health(1), Is.EqualTo(full));
        }

        // A6: a floor plate (cells x 2..6, top 3 m, bottom 2.5 m) stops a projectile rising toward a point above it, from below.
        [Test]
        public void PlateStopsTheProjectile()
        {
            Proj1World w = World(30000, new[] { Proj1World.U(M / 2, 2 * M, 1), Proj1World.U(7 * M + M / 2, 2 * M, 2, -1) }, plates: Ml5Plates.M());
            AimAt(w, Proj1World.Raw(7 * M + M / 2, 3 * M, 2 * M));
            (long _, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That(end.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            Assert.That(end.End.Y.Raw, Is.InRange(Ml5Plates.Bottom - 8, Ml5Plates.Bottom + 8), "the plate's underside");
            Assert.That(end.End.X.Raw, Is.InRange(2 * M, 6 * M));
        }

        // A9: falloffEnd 20 m. 1 m a tick: a target 19.5 m away is struck on the last segment, one 20.5 m away is not and the
        // projectile ends Open exactly 20 m out. 70 000 mm/s (step 152 917, 9 ticks, 9 x 2.33 m = 21 m): the last segment is cut
        // at 20 m, so the target at 20.5 m (front 20.25 m) is still missed.
        [TestCase(30000, 39, 21L, true)]
        [TestCase(30000, 41, 21L, false)]
        [TestCase(70000, 41, 10L, false)]
        [TestCase(70000, 39, 10L, true)]
        public void RangeEndsAtFalloffEnd(int speed, int halfMetres, long tick, bool struck)
        {
            Proj1World w = World(speed, new[] { Proj1World.U(0, 0, 1), Proj1World.U(halfMetres * M / 2, 0, 2, -1) }, falloffEnd: 20);
            long full = w.Health(1);
            AimAt(w, 1);
            (long at, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That(at, Is.EqualTo(tick));
            Assert.That(end.Outcome, Is.EqualTo(struck ? FireOutcome.Hit : FireOutcome.Open));
            if (!struck) Assert.That(end.End, Is.EqualTo(Proj1World.Raw(20 * M, Eye, 0)));
            w.Step();
            Assert.That(w.Health(1) < full, Is.EqualTo(struck));
        }
    }
}
