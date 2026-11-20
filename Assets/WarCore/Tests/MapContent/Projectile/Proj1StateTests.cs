using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A11 and A12: saves, restores and the order of landings.
    public sealed class Proj1StateTests
    {
        private const long M = Proj1World.M;
        private static readonly WeaponDef Gun = Proj1World.Weapon(intervalMilli: 3_000_000);

        // Two shooters, two targets: shooter 0 fires on tick 1 at 20 m, shooter 1 on tick 3 (due 3000) at 15 m.
        private static Proj1World Busy(ulong seed = 41)
        {
            var w = new Proj1World(Proj1World.Pack(new[] { Gun, Gun }, new[] { 30000, 0 }, new RollDamage()), new[]
            {
                Proj1World.U(0, 0, 1), Proj1World.U(20 * M, 0, 2, -1), Proj1World.U(0, 20 * M, 1), Proj1World.U(15 * M, 20 * M, 2, -1),
            }, seed: seed);
            w.SetDue(2, 3000);
            return w;
        }

        // A11: saved with two projectiles in flight, restored into a fresh world, 30 more ticks hash as the run never stopped.
        [Test]
        public void RestoreMidFlightContinuesTheSameRun()
        {
            Proj1World a = Busy();
            for (int t = 0; t < 5; t++) a.Step();
            Assert.That(a.Projectiles.Count, Is.EqualTo(2));
            byte[] save = a.Sim.SaveRetainedSnapshot();
            Proj1World b = Busy();
            b.Sim.RestoreRetainedSnapshot(save);
            Assert.That(b.Projectiles.Count, Is.EqualTo(2));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(save));
            int landed = 0;
            for (int t = 0; t < 30; t++)
            {
                a.Step(); b.Step();
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick " + a.Tick);
                Assert.That(b.Projectiles.Impacts.ToArray(), Is.EqualTo(a.Projectiles.Impacts.ToArray()), "tick " + a.Tick);
                landed += a.Projectiles.Impacts.Length;
            }
            Assert.That(landed, Is.EqualTo(2));
        }

        // The projectile state is in the world hash: the same world with one projectile fired a tick later hashes differently.
        [Test]
        public void FlightIsInTheHash()
        {
            Proj1World a = Busy(), b = Busy();
            b.SetDue(0, 2000);
            a.Step(); a.Step(); b.Step(); b.Step();
            Assert.That(a.Projectiles.Count, Is.EqualTo(b.Projectiles.Count));
            Assert.That(a.Sim.StateHashRetained(), Is.Not.EqualTo(b.Sim.StateHashRetained()));
        }

        // The system's own bytes: digest, count, then per projectile launcher, team, weapon, launch tick, origin, step, flights.
        private static byte[] Bytes(Proj1World w, Action<SimStateWriter> body, ulong? digest = null)
        {
            var writer = new SimStateWriter();
            writer.UInt64(digest ?? w.UsedPack.PolicyDigest);
            body(writer);
            return writer.ToArray();
        }

        private static void One(SimStateWriter writer, StableId launcher, int weapon, long launch, int flights, long stepX = M)
        {
            writer.UInt64(launcher.Value); writer.Int32(1); writer.Int32(weapon); writer.Int64(launch);
            writer.Int64(0); writer.Int64(98304); writer.Int64(0);
            writer.Int64(stepX); writer.Int64(0); writer.Int64(0);
            writer.Int32(flights);
        }

        private static void Load(Proj1World w, byte[] bytes) => w.Projectiles.LoadState(new SimStateReader(bytes));

        // A11: bad saves are refused. The world stands on tick 70 (after 70 steps); weapon 0 flies 60 ticks, weapon 1 is instant.
        [Test]
        public void BadSavesAreRefused()
        {
            Proj1World w = Busy();
            for (int t = 0; t < 70; t++) w.Step();
            StableId id = w.Id(0);
            Assert.DoesNotThrow(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 70 - 59, 60); })), "k = F - 1 is a legal save");
            Assert.That(w.Projectiles.Count, Is.EqualTo(1));
            Assert.DoesNotThrow(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 70, 60); })), "k = 0 is a legal save");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 70 - 60, 60); })), "k = F");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 71, 60); })), "fired in the future");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 1, 70, 60); })), "instant weapon");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 2, 70, 60); })), "no such weapon");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 70, 59); })), "flights differ");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 70, 60, 0); })), "zero step");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 70, 60, 1L << 40); })), "step too long");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x =>
            {
                x.Int32(w.Pool + 1);
                for (int i = 0; i <= w.Pool; i++) One(x, id, 0, 70, 60);
            })), "count above the pool");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(1); One(x, id, 0, 70, 60); }, digest: 1)), "other pack");
            Assert.Throws<SimSnapshotException>(() => Load(w, Bytes(w, x => { x.Int32(2); One(x, id, 0, 70, 60); One(x, id, 0, 69, 60); })),
                "out of firing order");
        }

        // A12: two projectiles landing on one tick draw their rolls in firing order: swapping who fires first swaps the damage.
        [Test]
        public void RollsFollowTheFiringOrder()
        {
            long[] Run(bool swap)
            {
                var w = new Proj1World(Proj1World.Pack(30000, Gun, new RollDamage()), new[]
                {
                    Proj1World.U(0, 0, 1), Proj1World.U((swap ? 9 : 10) * M, 0, 2, -1),
                    Proj1World.U(0, 20 * M, 1), Proj1World.U((swap ? 10 : 9) * M, 20 * M, 2, -1),
                });
                w.SetDue(swap ? 0 : 2, 2000);
                long full = w.Health(1);
                (long tick, ProjectileImpact first) = w.StepUntilImpact();
                Assert.That(tick, Is.EqualTo(11L));
                Assert.That(w.Projectiles.Impacts.Length, Is.EqualTo(2), "both land on tick 11");
                Assert.That(first.Launcher, Is.EqualTo(w.Id(swap ? 2 : 0)), "the first fired lands first");
                w.Step();
                return new[] { full - w.Health(1), full - w.Health(3) };
            }
            long[] a = Run(false), b = Run(true);
            Assert.That(a[0], Is.Not.EqualTo(a[1]));
            Assert.That((b[0], b[1]), Is.EqualTo((a[1], a[0])));
        }

        // A12: the projectile rolls have their own stream: an instant gun's later hit takes the same damage whether or not a
        // projectile landed before it.
        [Test]
        public void ProjectileRollsDoNotShiftInstantRolls()
        {
            long Instant(bool projectile)
            {
                var w = new Proj1World(Proj1World.Pack(new[] { Proj1World.Weapon(), Gun }, new[] { 0, 30000 }, new RollDamage()), new[]
                {
                    Proj1World.U(0, 0, 1, 0), Proj1World.U(10 * M, 0, 2, -1),
                    Proj1World.U(0, 20 * M, 1, projectile ? 1 : -1), Proj1World.U(10 * M, 20 * M, 2, -1),
                });
                for (int t = 0; t < 30; t++) w.Step();
                long before = w.Health(1);
                w.Step();
                Assert.That(w.Combat.Fires.Length, Is.EqualTo(1), "the instant gun fires again on tick 31");
                w.Step();
                return before - w.Health(1);
            }
            Assert.That(Instant(true), Is.EqualTo(Instant(false)));
        }

        // A12: one unit fires every tick; two of its projectiles land on the same target on one tick (keys differ by launch
        // tick) while it fires again: nothing throws and both hurt.
        [Test]
        public void TwoProjectilesOfOneUnitLandTogether()
        {
            var w = new Proj1World(Proj1World.Pack(30000, Proj1World.Weapon(intervalMilli: 1000, magazine: 1000), new RollDamage()),
                new[] { Proj1World.U(0, 0, 1), Proj1World.U(50 * M, 0, 2, -1) });
            for (int t = 0; t < 3; t++) w.Step();
            w.Move(1, 2 * M, 0, 0);
            long full = w.Health(1);
            ProjectileImpact[] hits = null;
            Assert.DoesNotThrow(() => hits = w.StepImpacts());
            Assert.That(hits.Select(h => (h.Outcome, h.LaunchTick)), Is.EqualTo(new[] { (FireOutcome.Hit, 1L), (FireOutcome.Hit, 2L) }));
            Assert.That(w.Projectiles.Count, Is.EqualTo(2), "tick 3's and tick 4's are still flying");
            w.Step();
            Assert.That(full - w.Health(1), Is.GreaterThanOrEqualTo(hits[0].DamageRaw + hits[1].DamageRaw));
        }
    }
}
