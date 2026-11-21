using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A7, A8, A10, A16: what a landing does. 1 m a tick, one shot per 100 s, falloff 0..60 m, damage = effect x falloff.
    public sealed class Proj1LandingTests
    {
        private const long M = Proj1World.M;
        private static readonly WeaponDef Gun = Proj1World.Weapon(intervalMilli: 3_000_000);

        private static Proj1World World(Proj1World.Unit[] units, bool friendlyFire = false, IDamageGuard guard = null) =>
            new Proj1World(Proj1World.Pack(30000, Gun, friendlyFire: friendlyFire), units, guard: guard);

        private static void AimAt(Proj1World w, int target) => w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(target)));

        // An outside blow that kills unit i on the tick it is struck (a damage command of its own key).
        private static void KillOn(Proj1World w, int i, long tick, System.Action<SimContext> also = null) => w.Produce = c =>
        {
            also?.Invoke(c);
            if (c.Clock.Tick == tick)
                c.Damage(w.Sim.GameplayTarget(w.Id(i)), new GameplayApplyKey(w.Id(i), (ulong)tick, 0, w.Id(i), (ulong)tick, 9), 1, 1000 * M);
        };

        private static long Expected(int metres) => Gun.EffectRaw * Gun.FalloffRaw(SimFix.FromInt(metres)) / SimFix.Scale;

        // A7: a teammate first on the line stops the projectile unhurt (friendly fire off) or takes the hit (on).
        [TestCase(false, FireOutcome.FriendBlocked)]
        [TestCase(true, FireOutcome.FriendHit)]
        public void TeammateOnTheLine(bool friendlyFire, FireOutcome outcome)
        {
            Proj1World w = World(new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1), Proj1World.U(5 * M, 0, 1, -1) }, friendlyFire);
            long full = w.Health(2);
            AimAt(w, 1);
            (long tick, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That((tick, end.Outcome, end.Target), Is.EqualTo((6L, outcome, w.Id(2))));
            w.Step();
            Assert.That(w.Health(2), friendlyFire ? Is.EqualTo(full - Expected(5)) : Is.EqualTo(full));
            Assert.That(end.DamageRaw, Is.EqualTo(friendlyFire ? Expected(5) : 0));
            Assert.That(w.Health(1), Is.EqualTo(full), "the enemy behind is untouched");
        }

        // A8: the launcher dies in flight; its projectile still lands and hurts.
        [Test]
        public void LauncherDeadInFlightStillLands()
        {
            Proj1World w = World(new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1) });
            long full = w.Health(1);
            KillOn(w, 0, 3);
            (long tick, ProjectileImpact hit) = w.StepUntilImpact();
            Assert.That(w.Dead(0), Is.True);
            Assert.That((tick, hit.Outcome, hit.Target, hit.Launcher), Is.EqualTo((11L, FireOutcome.Hit, w.Id(1), w.Id(0))));
            w.Step();
            Assert.That(w.Health(1), Is.EqualTo(full - Expected(10)));
        }

        // A8: the team is the one recorded at launch, not the launcher's team now.
        [Test]
        public void TeamIsTheOneRecordedAtLaunch()
        {
            Proj1World w = World(new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1) });
            w.Step();
            Combatant u = w.State(0);
            w.Sim.Entities.Set(w.Combatants, w.Entity(0), new Combatant(2, u.Weapon, u.Ammo, u.DueMilli, u.SpreadStored, u.SpreadLastMilli));
            (long _, ProjectileImpact hit) = w.StepUntilImpact();
            Assert.That((hit.Outcome, hit.Team), Is.EqualTo((FireOutcome.Hit, 1)));
        }

        // A8: the target dies in flight; the projectile passes the body and strikes the enemy behind (front 14.75 m, tick 16).
        [Test]
        public void DeadTargetDoesNotStopTheProjectile()
        {
            Proj1World w = World(new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1), Proj1World.U(15 * M, 0, 2, -1) });
            KillOn(w, 1, 3);
            (long tick, ProjectileImpact hit) = w.StepUntilImpact();
            Assert.That((tick, hit.Outcome, hit.Target), Is.EqualTo((16L, FireOutcome.Hit, w.Id(2))));
        }

        // A10: the falloff distance runs from the launch foot to the struck unit's foot when struck: the target fired on at 20 m
        // walks to 15 m (or 25 m) and takes the damage of that distance.
        [TestCase(15)]
        [TestCase(25)]
        public void FalloffUsesTheDistanceAtTheLanding(int metres)
        {
            Proj1World w = World(new[] { Proj1World.U(0, 0, 1), Proj1World.U(20 * M, 0, 2, -1) });
            long full = w.Health(1);
            w.Step(); w.Step();
            w.Move(1, metres * M, 0, 0);
            (long _, ProjectileImpact hit) = w.StepUntilImpact();
            Assert.That(hit.Distance, Is.EqualTo(SimFix.FromInt(metres)));
            Assert.That(hit.DamageRaw, Is.EqualTo(Expected(metres)));
            w.Step();
            Assert.That(w.Health(1), Is.EqualTo(full - Expected(metres)));
        }

        // A16: a shielded victim takes 0; the landing is still a Hit.
        [Test]
        public void ShieldedVictimTakesNothing()
        {
            var guard = new ShieldAll();
            Proj1World w = World(new[] { Proj1World.U(0, 0, 1), Proj1World.U(10 * M, 0, 2, -1) }, guard: guard);
            guard.Add(w.Id(1));
            long full = w.Health(1);
            (long _, ProjectileImpact hit) = w.StepUntilImpact();
            Assert.That((hit.Outcome, hit.DamageRaw), Is.EqualTo((FireOutcome.Hit, 0L)));
            w.Step();
            Assert.That(w.Health(1), Is.EqualTo(full));
        }

        // A team-0 launcher's projectile passes every unit (as its instant shot would) and ends on its range.
        [Test]
        public void TeamZeroProjectileStrikesNobody()
        {
            Proj1World w = World(new[] { Proj1World.U(0, 0, 0), Proj1World.U(10 * M, 0, 2, -1) });
            long full = w.Health(1);
            AimAt(w, 1);
            (long tick, ProjectileImpact end) = w.StepUntilImpact();
            Assert.That((tick, end.Outcome, end.Team), Is.EqualTo((61L, FireOutcome.Open, 0)));
            Assert.That(w.Health(1), Is.EqualTo(full));
        }
    }
}
