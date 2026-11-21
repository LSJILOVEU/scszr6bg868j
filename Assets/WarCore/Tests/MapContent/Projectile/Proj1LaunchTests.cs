using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A2 and A17: firing a projectile spends the shot exactly as an instant shot does, and lands nothing that tick.
    public sealed class Proj1LaunchTests
    {
        private const long M = Proj1World.M;

        // A weapon with spread, so the spread state after the shot is a real check.
        private static WeaponDef Spread() => new WeaponDef(30000, 30, 60000, default, SimFix.FromInt(60), 10 * M, new SpreadSpec(1000, 340, 1400, 3000));

        private static Proj1World Pair(int speed, WeaponDef weapon, long targetX = 10 * M, Ml3World.Wall[] walls = null) =>
            new Proj1World(Proj1World.Pack(speed, weapon), new[] { Proj1World.U(0, 0, 1), Proj1World.U(targetX, 0, 2, -1) }, walls);

        // A2: ammo, cooldown and spread state equal the instant shot's; no fire record, no damage on the firing tick.
        [Test]
        public void LaunchSpendsTheShotAndLandsNothing()
        {
            Proj1World flying = Pair(30000, Spread()), instant = Pair(0, Spread());
            long full = flying.Health(1);
            flying.Step(); instant.Step();
            Assert.That(instant.Combat.Fires.Length, Is.EqualTo(1), "the instant twin fires on tick 1");
            Combatant a = flying.State(0), b = instant.State(0);
            Assert.That((a.Ammo, a.DueMilli, a.SpreadStored, a.SpreadLastMilli), Is.EqualTo((b.Ammo, b.DueMilli, b.SpreadStored, b.SpreadLastMilli)));
            Assert.That(a.Ammo, Is.EqualTo(29));
            Assert.That(flying.Combat.Fires.Length, Is.Zero);
            Assert.That(flying.Projectiles.Impacts.Length, Is.Zero);
            Assert.That(flying.Projectiles.Count, Is.EqualTo(1));
            flying.Step();
            Assert.That(flying.Health(1), Is.EqualTo(full));
        }

        // A17: an explicit target intent with a projectile weapon fires at the target's viewpoint, without spread.
        [Test]
        public void TargetIntentFiresAtTheViewpoint()
        {
            Proj1World w = Pair(30000, Spread());
            w.Produce = c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1)));
            w.Step();
            Assert.That(w.Projectiles.Count, Is.EqualTo(1));
            ProjectileView p = w.Projectiles.InFlight(0);
            Assert.That(p.Origin, Is.EqualTo(Proj1World.Raw(0, 98304, 0)), "the muzzle");
            Assert.That(p.Step, Is.EqualTo(Proj1World.Raw(M, 0, 0)), "straight at the 1.5 m viewpoint, 1 m a tick");
            Assert.That((p.Launcher, p.Team, p.Weapon, p.LaunchTick, p.Flights), Is.EqualTo((w.Id(0), 1, 0, 1L, 60)));
            Assert.That(w.State(0).Ammo, Is.EqualTo(29));
            Assert.That((w.State(0).SpreadStored, w.State(0).SpreadLastMilli), Is.EqualTo((0, 0L)), "this path keeps the spread state");
            Assert.That(w.Combat.Fires.Length, Is.Zero);
        }

        // A17: out of range or out of sight, nothing is fired and nothing is spent.
        [Test]
        public void TargetIntentOutOfRangeOrSightFiresNothing()
        {
            Proj1World far = Pair(30000, Spread(), targetX: 61 * M);
            far.Produce = c => far.Combat.Submit(c, FireIntent.AtTarget(far.Id(0), far.Id(1)));
            far.Step();
            var walls = new[] { new Ml3World.Wall(5 * M, -2 * M, 5 * M + M / 5, 2 * M, 0, 3 * M) };
            Proj1World hidden = Pair(30000, Spread(), walls: walls);
            hidden.Produce = c => hidden.Combat.Submit(c, FireIntent.AtTarget(hidden.Id(0), hidden.Id(1)));
            hidden.Step();
            foreach (Proj1World w in new[] { far, hidden })
            {
                Assert.That(w.Projectiles.Count, Is.Zero);
                Assert.That(w.State(0).Ammo, Is.EqualTo(30));
                Assert.That(w.State(0).DueMilli, Is.Zero);
            }
        }

        // A mixed pack: the instant weapon still fires instantly beside a projectile weapon.
        [Test]
        public void InstantWeaponInAMixedPackStaysInstant()
        {
            var pack = Proj1World.Pack(new[] { Proj1World.Weapon(), Proj1World.Weapon() }, new[] { 30000, 0 });
            var w = new Proj1World(pack, new[] { Proj1World.U(0, 0, 1, 1), Proj1World.U(10 * M, 0, 2, -1) });
            w.Step();
            Assert.That(w.Combat.Fires.ToArray().Single().Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(w.Projectiles.Count, Is.Zero);
        }
    }
}
