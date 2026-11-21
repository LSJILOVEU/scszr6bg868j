using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A13 and A14: the pool bound, the world's damage limits, and the system existing only with a projectile weapon.
    public sealed class Proj1CapacityTests
    {
        private const long M = Proj1World.M;

        // A13 (review 1): per unit ((F + 1) * 1000 - 1) / min(interval, reload) + 1, worked by hand. 1 m a tick, so F = metres.
        [TestCase(1001, 1, 600000, 2, 3L)]
        [TestCase(1000, 30, 600000, 10, 11L)]
        [TestCase(3000, 1, 1000, 20, 21L)]
        [TestCase(30000, 30, 60000, 60, 3L)]
        [TestCase(30000, 30, 60000, 900, 31L)]
        public void PerUnitBound(long interval, int magazine, long reload, int flights, long bound)
        {
            WeaponDef weapon = Proj1World.Weapon(flights, interval, magazine: magazine, reloadMilli: reload);
            var spec = new ProjectileSpec(30000, weapon.FalloffEnd);
            Assert.That(spec.Flights, Is.EqualTo(flights));
            Assert.That(spec.PerUnitCapacity(weapon), Is.EqualTo(bound));
        }

        // The pack's P sums the units carrying a projectile weapon (reserves are units too); instant and unarmed units add 0.
        [Test]
        public void PackCapacitySumsTheUnits()
        {
            var pack = Proj1World.Pack(new[] { Proj1World.Weapon(10, 1000), Proj1World.Weapon() }, new[] { 30000, 0 });
            Assert.That(pack.ProjectileCapacity(new[] { 0, 0, 1, -1 }), Is.EqualTo(22));
            Assert.That(Proj1World.Pack(0).ProjectileCapacity(new[] { 0, 0 }), Is.Zero);
        }

        // A13: the bound is met in play. Interval 1001, F = 2: the unit fires on ticks 1, 3, 4, 5, ... so three projectiles are in
        // the pool when the third fires; a pool of 3 runs, a pool of 2 throws on the firing that would overflow it.
        [Test]
        public void TheBoundIsReachedAndAPoolOneSmallerThrows()
        {
            Proj1World World(int? pool)
            {
                var w = new Proj1World(Proj1World.Pack(30000, Proj1World.Weapon(2, 1001, magazine: 100000, reloadMilli: 600000)),
                    new[] { Proj1World.U(0, 0, 1), Proj1World.U(50 * M, 0, 2, -1) }, pool: pool);
                w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
                return w;
            }
            Proj1World exact = World(null);
            Assert.That(exact.Pool, Is.EqualTo(3));
            int most = 0;
            for (int t = 0; t < 400; t++) { exact.Step(); most = Math.Max(most, exact.Projectiles.Count + exact.Projectiles.Impacts.Length); }
            Assert.That(most, Is.EqualTo(3), "three in the pool at a firing");
            Proj1World small = World(2);
            small.Step(); small.Step(); small.Step(); small.Step();
            int ammo = small.State(0).Ammo;
            var ex = Assert.Throws<InvalidOperationException>(() => small.Step());
            StringAssert.Contains("Projectile capacity", ex.Message);
            Assert.That(small.State(0).Ammo, Is.EqualTo(ammo), "the overflowing shot spends nothing");
        }

        // A13: more landings on one tick than there are units (two launchers, two landings each, on one target) all settle.
        // Without P added to the damage limits the fourth command would not fit (3 units x 1).
        [Test]
        public void MoreLandingsThanUnitsAllSettle()
        {
            var gun = Proj1World.Weapon(3, 1000, magazine: 1000);
            var w = new Proj1World(Proj1World.Pack(30000, gun), new[] { Proj1World.U(0, 0, 1), Proj1World.U(4 * M, 0, 1), Proj1World.U(2 * M, 20 * M, 2, -1) });
            w.Produce = c =>
            {
                w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), Proj1World.Raw(10 * M, 0, 0)));
                w.Combat.Submit(c, FireIntent.Aimed(w.Id(1), Proj1World.Raw(-6 * M, 0, 0)));
            };
            for (int t = 0; t < 3; t++) w.Step();
            w.Move(2, 2 * M, 0, 0);
            long full = w.Health(2);
            ProjectileImpact[] hits = w.StepImpacts();
            Assert.That(hits.Count(h => h.Outcome == FireOutcome.Hit && h.Target.Equals(w.Id(2))), Is.EqualTo(4));
            Assert.That(full - w.Health(2), Is.EqualTo(hits.Sum(h => h.DamageRaw)), "every landing's damage was applied");
        }

        // A13 (review 1, must 3): P is refused when the damage staging would exceed GameplayMath.MaxPackets. Interval 1000 and
        // F = 900 give 901 per unit; staging = 2 (N + 901 N) + 2 N + 1 = 1806 N + 1: 580 units fit (1 047 481), 581 do not.
        [Test]
        public void CapacityAboveTheSettlementLimitIsRefused()
        {
            var pack = Proj1World.Pack(30000, Proj1World.Weapon(900, 1000));
            Assert.That(pack.ProjectileCapacity(new int[580]), Is.EqualTo(580 * 901));
            var ex = Assert.Throws<ArgumentException>(() => pack.ProjectileCapacity(new int[581]));
            StringAssert.Contains("Projectile capacity too large", ex.Message);
            Assert.DoesNotThrow(() => Simulation.CreateRetainedWorld(1, new PlacedCatalog(new StableContentManifest(1), new[] { new PlacedCatalogEntry(1, 1) }),
                pack.BuildConfig(1, 580, 580 * 901), content: new StableContentManifest(1), gameplaySchema: pack.BuildSchema(580, 580 * 901)),
                "the largest accepted P builds its settlement");
            Assert.Throws<SimBoundaryException>(() => Simulation.CreateRetainedWorld(1, new PlacedCatalog(new StableContentManifest(1), new[] { new PlacedCatalogEntry(1, 1) }),
                pack.BuildConfig(1, 581, 581 * 901), content: new StableContentManifest(1), gameplaySchema: pack.BuildSchema(581, 581 * 901)),
                "one more unit is what the settlement itself refuses");
        }

        // BuildSchema and BuildConfig grow by P and are unchanged at 0.
        [Test]
        public void LimitsGrowByP()
        {
            CombatPack pack = Proj1World.Pack(30000);
            Assert.That(pack.BuildSchema(7, 0).Capacities.Commands, Is.EqualTo(7));
            Assert.That(pack.BuildSchema(7, 5).Capacities.Commands, Is.EqualTo(12));
            Assert.That(pack.BuildSchema(7, 5).Capacities.Notices, Is.EqualTo(19));
            Assert.That(pack.BuildConfig(9, 7, 5).EventHardLimit, Is.EqualTo(26));
            Assert.That(pack.BuildConfig(9, 7).EventHardLimit, Is.EqualTo(21));
        }

        private static T Registered<T>(Simulation sim, out SimSystemPhase phase) where T : class
        {
            phase = default;
            foreach (object entry in (IEnumerable)typeof(Simulation).GetField("systems", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sim))
            {
                object system = entry.GetType().GetField("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (system is T value) { phase = (SimSystemPhase)entry.GetType().GetField("Phase").GetValue(entry); return value; }
            }
            return null;
        }

        private static GameplaySchema Schema(Simulation sim)
        {
            object state = typeof(Simulation).GetField("gameplay", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sim);
            return (GameplaySchema)state.GetType().GetField("schema", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(state);
        }

        // A14: a world built from a pack without projectile weapons has no projectile system.
        [Test]
        public void WorldWithoutProjectilesHasNoProjectileSystem()
        {
            var w = new M6World(DemoRecipes.M6(RwrPack.Load("rwr-m6")));
            Assert.That(Registered<ProjectileSystem>(w.Sim, out _), Is.Null);
            Assert.That(w.Combat.Projectiles, Is.Null);
        }

        // A14 and review must 1: a built world with a projectile weapon registers the system in Queries, Order 31, with the
        // pack's pool for the recipe's units, and sizes its damage limits by the same P.
        [Test]
        public void WorldWithProjectilesRegistersTheSystem()
        {
            CombatPack pack = Proj1World.Pack(new[] { Proj1World.Weapon(10, 1000), Proj1World.Weapon() }, new[] { 30000, 0 });
            var units = new[] { MeleeWorld.Unit(1, 1, 10500, 10500, weapon: 0), MeleeWorld.Unit(2, 2, 15500, 10500, weapon: 1), MeleeWorld.Unit(3, 1, 10500, 20500) };
            var w = new M6World(new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(pack));
            ProjectileSystem system = Registered<ProjectileSystem>(w.Sim, out SimSystemPhase phase);
            Assert.That(system, Is.Not.Null);
            Assert.That(w.Combat.Projectiles, Is.SameAs(system));
            Assert.That((phase, system.Order, system.Capacity), Is.EqualTo((SimSystemPhase.Queries, 31, 11)));
            Assert.That(Schema(w.Sim).Capacities.Commands, Is.EqualTo(3 + 11), "the world's limits take the same P");
            for (int t = 0; t < 40; t++) w.Sim.Step(default);
            Assert.That(w.Sim.GameplayResource(w.Sim.Entities.StableOf(w.Unit(2)), 1), Is.LessThan(100 * M), "the projectile gun fires and lands in a built world");
        }
    }
}
