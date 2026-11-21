using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Blast1
{
    // BLAST-1 B7, B9 and B10: the queue, saves and the world's limits.
    public sealed class Blast1QueueTests
    {
        private const long M = Blast1World.M, C = Blast1World.Centre, X = 20 * M, Z = 20 * M;

        private static long[] Serials(Blast1World w) => w.Blasts.Records.ToArray().Select(r => r.Serial).ToArray();

        // B7: six queued at once with four a tick: the first four go off now, the last two on the next tick, in order.
        [Test]
        public void FourATickInOrder()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + 4 * M, Z, 2) });
            for (int i = 0; i < 6; i++) Assert.That(w.Blast(X, C, Z), Is.EqualTo(i + 1L));
            Assert.DoesNotThrow(() => w.Step());
            Assert.That(Serials(w), Is.EqualTo(new[] { 1L, 2L, 3L, 4L }));
            Assert.That(w.Blasts.Count, Is.EqualTo(2));
            Assert.That(w.Blasts.Waiting(0).Serial, Is.EqualTo(5L));
            w.Step();
            Assert.That(Serials(w), Is.EqualTo(new[] { 5L, 6L }));
            Assert.That(w.Blasts.Count, Is.Zero);
            w.Step();
            Assert.That(w.Blasts.Records.Length, Is.Zero);
            Assert.That(w.Blasts.Serial, Is.EqualTo(6L));
        }

        // B7: a blast queued after Order 28 goes off on the next tick; one queued before it on the same tick.
        [Test]
        public void QueuedAfterTheSystemWaitsATick()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + 4 * M, Z, 2) });
            long late = 0, early = 0;
            w.Late = c => { if (c.Clock.Tick == 3) late = w.Blast(X, C, Z); };
            w.Early = c => { if (c.Clock.Tick == 5) early = w.Blast(X, C, Z); };
            for (int t = 1; t <= 5; t++)
            {
                w.Step();
                long[] went = Serials(w);
                Assert.That(went, Is.EqualTo(w.Tick == 4 ? new[] { late } : w.Tick == 5 ? new[] { early } : new long[0]), "tick " + w.Tick);
            }
            Assert.That((late, early), Is.EqualTo((1L, 2L)));
        }

        // A full queue throws; the queued blasts stay as they were.
        [Test]
        public void AFullQueueThrows()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + 4 * M, Z, 2) });
            for (int i = 0; i < 64; i++) w.Blast(X, C, Z);
            var ex = Assert.Throws<InvalidOperationException>(() => w.Blast(X, C, Z));
            StringAssert.Contains("Blast queue capacity exceeded", ex.Message);
            Assert.That((w.Blasts.Count, w.Blasts.Serial), Is.EqualTo((64, 64L)));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Blast(X, C, Z, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Blast(X, C, Z, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Blast(X, C, Z, 0, default, -1));
        }

        // A queue of 3, one a tick, filled over and over: the ring keeps the order.
        [Test]
        public void TheRingKeepsTheOrder()
        {
            var w = new Blast1World(Blast1World.Pack(new BlastRules(new[] { Blast1World.Frag() }, 1, 3)), new[] { Blast1World.U(X + 4 * M, Z, 2) });
            long next = 1;
            w.Blast(X, C, Z); w.Blast(X, C, Z);
            for (int t = 0; t < 7; t++)
            {
                w.Blast(X, C, Z);
                w.Step();
                Assert.That(Serials(w), Is.EqualTo(new[] { next++ }));
                Assert.That(Enumerable.Range(0, w.Blasts.Count).Select(i => w.Blasts.Waiting(i).Serial), Is.EqualTo(new[] { next, next + 1 }));
            }
        }

        // The busy world without blasts queued.
        private static Blast1World Quiet(ulong seed = 41, bool reverse = false) =>
            new Blast1World(Blast1World.Pack(new BlastRules(new[] { Blast1World.Frag(), Blast1World.Frag(false, id: "t.mine") }, 1, 8)), new[]
            {
                Blast1World.U(X + M, Z, 1), Blast1World.U(X + 3 * M, Z, 2), Blast1World.U(X - 5 * M, Z, 2), Blast1World.U(X, Z + 4 * M, 1),
                Blast1World.U(X + 10 * M, Z, 2),
            }, seed: seed, reverse: reverse);

        private static Blast1World Busy(ulong seed = 41, bool reverse = false)
        {
            Blast1World w = Quiet(seed, reverse);
            w.Blast(X + 6 * M, C, Z, 1, w.Id(3), 1);
            w.Blast(X, C, Z + M, 0, w.Id(1), 2);
            w.Blast(X - M, C, Z - M);
            return w;
        }

        // B9: saved with two blasts waiting, restored into a fresh world: every later tick hashes as the run that never stopped,
        // and the serial goes on.
        [Test]
        public void RestoreWithBlastsWaitingContinuesTheSameRun()
        {
            Blast1World a = Busy();
            a.Step();
            Assert.That(a.Blasts.Count, Is.EqualTo(2));
            byte[] save = a.Sim.SaveRetainedSnapshot();
            Blast1World b = new Blast1World(Blast1World.Pack(new BlastRules(new[] { Blast1World.Frag(), Blast1World.Frag(false, id: "t.mine") }, 1, 8)), new[]
            {
                Blast1World.U(X + M, Z, 1), Blast1World.U(X + 3 * M, Z, 2), Blast1World.U(X - 5 * M, Z, 2), Blast1World.U(X, Z + 4 * M, 1),
                Blast1World.U(X + 10 * M, Z, 2),
            });
            b.Sim.RestoreRetainedSnapshot(save);
            Assert.That((b.Blasts.Count, b.Blasts.Serial), Is.EqualTo((2, 3L)));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(save));
            for (int t = 0; t < 4; t++)
            {
                a.Step(); b.Step();
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick " + a.Tick);
                Assert.That(b.Hits(), Is.EqualTo(a.Hits()), "tick " + a.Tick);
            }
            Assert.That(b.Blast(X, C, Z), Is.EqualTo(4L));
            Assert.That(a.Blast(X, C, Z), Is.EqualTo(4L));
            a.Step(); b.Step();
            Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()));
        }

        // B9: saved with nothing waiting after three blasts, restored into a fresh world: the serial goes on from 3.
        [Test]
        public void AnEmptyQueueKeepsTheSerial()
        {
            Blast1World a = Busy();
            for (int t = 0; t < 3; t++) a.Step();
            Assert.That((a.Blasts.Count, a.Blasts.Serial), Is.EqualTo((0, 3L)));
            Blast1World b = Quiet();
            b.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot());
            Assert.That((b.Blasts.Count, b.Blasts.Serial), Is.EqualTo((0, 3L)));
            Assert.That(b.Blast(X, C, Z), Is.EqualTo(4L));
        }

        // B9: the queue is in the hash: one more blast, or the same count with a point 1 raw apart, hashes differently, in the
        // world's hash (the saved bytes) and in the system's own hash.
        [Test]
        public void TheQueueIsInTheHash()
        {
            Blast1World a = Busy(), b = Busy();
            b.Blast(X, C, Z);
            Assert.That(b.Sim.StateHashRetained(), Is.Not.EqualTo(a.Sim.StateHashRetained()));
            Assert.That(Own(b), Is.Not.EqualTo(Own(a)));
            a.Blast(X + 1, C, Z);
            Assert.That(b.Sim.StateHashRetained(), Is.Not.EqualTo(a.Sim.StateHashRetained()));
            Assert.That(Own(b), Is.Not.EqualTo(Own(a)));
            Blast1World c = Busy();
            c.Blast(X, C, Z);
            Assert.That(Own(c), Is.EqualTo(Own(b)));
        }

        private static ulong Own(Blast1World w)
        {
            var hash = new StateHash();
            w.Blasts.WriteState(hash);
            return hash.Value;
        }

        // B9: two runs from one seed match; a world whose units were created in reverse hits every unit the same.
        [Test]
        public void SameSeedSameRunAndCreationOrderDoesNotMatter()
        {
            Blast1World a = Busy(), b = Busy(), r = Busy(reverse: true);
            int hits = 0;
            for (int t = 0; t < 4; t++)
            {
                a.Step(); b.Step(); r.Step();
                hits += a.Blasts.HitCount;
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()));
                Assert.That(r.Hits(), Is.EqualTo(a.Hits()), "tick " + a.Tick);
                for (int i = 0; i < a.Count; i++) Assert.That(r.Health(i), Is.EqualTo(a.Health(i)));
            }
            Assert.That(hits, Is.EqualTo(8), "2 + 4 + 2 hits over the three blasts (two units die in the second)");
        }

        // The system's bytes: serial, count, then per blast point (raw), kind, source, team, serial.
        private static byte[] Bytes(long serial, params (long X, long Y, long Z, int Kind, ulong Source, int Team, long Serial)[] items)
        {
            var writer = new SimStateWriter();
            writer.Int64(serial); writer.Int32(items.Length);
            foreach (var i in items) { writer.Int64(i.X); writer.Int64(i.Y); writer.Int64(i.Z); writer.Int32(i.Kind); writer.UInt64(i.Source); writer.Int32(i.Team); writer.Int64(i.Serial); }
            return writer.ToArray();
        }

        [Test]
        public void TheSavedBytesAreTheQueueInOrder()
        {
            Blast1World a = Busy();
            var writer = new SimStateWriter();
            a.Blasts.SaveState(writer);
            Assert.That(writer.ToArray(), Is.EqualTo(Bytes(3, (X + 6 * M, C, Z, 1, a.Id(3).Value, 1, 1), (X, C, Z + M, 0, a.Id(1).Value, 2, 2), (X - M, C, Z - M, 0, 0, 0, 3))));
            Blast1World b = Busy();
            b.Blasts.LoadState(new SimStateReader(Bytes(9, (1, 2, 3, 1, 7, 2, 5), (4, 5, 6, 0, 0, 0, 9))));
            Assert.That((b.Blasts.Count, b.Blasts.Serial), Is.EqualTo((2, 9L)));
            Assert.That(b.Blasts.Waiting(0), Is.EqualTo((Blast1World.Raw(1, 2, 3), 1, new StableId(7), 2, 5L)));
            Assert.That(b.Blasts.Waiting(1), Is.EqualTo((Blast1World.Raw(4, 5, 6), 0, default(StableId), 0, 9L)));
        }

        // Bad saves are refused: too many, a kind the pack lacks, a negative team, serials not rising or above the saved one,
        // a point out of range.
        [Test]
        public void BadSavesAreRefused()
        {
            Blast1World w = Busy();
            void Refused(byte[] bytes, string because) => Assert.Throws<SimSnapshotException>(() => w.Blasts.LoadState(new SimStateReader(bytes)), because);
            var nine = Enumerable.Range(1, 9).Select(s => (0L, 0L, 0L, 0, 0UL, 0, (long)s)).ToArray();
            Refused(Bytes(9, nine), "more than the queue holds");
            Refused(Bytes(1, (0, 0, 0, 2, 0, 0, 1)), "kind 2 of two");
            Refused(Bytes(1, (0, 0, 0, -1, 0, 0, 1)), "kind -1");
            Refused(Bytes(1, (0, 0, 0, 0, 0, -1, 1)), "team -1");
            Refused(Bytes(2, (0, 0, 0, 0, 0, 0, 2), (0, 0, 0, 0, 0, 0, 2)), "serials repeat");
            Refused(Bytes(3, (0, 0, 0, 0, 0, 0, 2), (0, 0, 0, 0, 0, 0, 1)), "serials fall");
            Refused(Bytes(1, (0, 0, 0, 0, 0, 0, 2)), "a serial above the saved one");
            Refused(Bytes(1, (0, 0, 0, 0, 0, 0, 0)), "serial 0");
            Refused(Bytes(-1), "a negative serial");
            Refused(Bytes(1, ((1L << 32) + 1, 0, 0, 0, 0, 0, 1)), "x out of range");
            Refused(Bytes(1, (0, -(1L << 32) - 1, 0, 0, 0, 0, 1)), "y out of range");
            Refused(Bytes(1, (0, 0, (1L << 32) + 1, 0, 0, 0, 1)), "z out of range");
            Assert.DoesNotThrow(() => w.Blasts.LoadState(new SimStateReader(Bytes(8, nine.Take(8).ToArray()))));
            Assert.DoesNotThrow(() => w.Blasts.LoadState(new SimStateReader(Bytes(1L << 40, ((1L << 32), -(1L << 32), 0, 1, 0, 0, 1L << 40)))));
        }

        // A save of a world with blasts does not load into the same world without them (system table and limits differ).
        [Test]
        public void ABlastSaveDoesNotLoadIntoAWorldWithout()
        {
            Blast1World a = Busy();
            byte[] save = a.Sim.SaveRetainedSnapshot();
            var plain = new Blast1World(new CombatPack(new[] { Projectile.Proj1World.Weapon() }, new Projectile.FalloffDamage(), new NearestVisibleEnemy(), 100 * M,
                body: Ml3World.Rwr), new[]
            {
                Blast1World.U(X + M, Z, 1), Blast1World.U(X + 3 * M, Z, 2), Blast1World.U(X - 5 * M, Z, 2), Blast1World.U(X, Z + 4 * M, 1),
                Blast1World.U(X + 10 * M, Z, 2),
            });
            Assert.That(plain.Blasts, Is.Null);
            Assert.That(() => plain.Sim.RestoreRetainedSnapshot(save), Throws.Exception);
        }

        // B10: the world's limits grow by B and are unchanged at 0.
        [Test]
        public void LimitsGrowByB()
        {
            CombatPack pack = Blast1World.Pack(Blast1World.Rules());
            Assert.That(pack.BuildSchema(7, 0, 0).Capacities.Commands, Is.EqualTo(7));
            Assert.That(pack.BuildSchema(7, 5, 28).Capacities.Commands, Is.EqualTo(40));
            Assert.That(pack.BuildSchema(7, 5, 28).Capacities.Notices, Is.EqualTo(47));
            Assert.That(pack.BuildConfig(9, 7, 5, 28).EventHardLimit, Is.EqualTo(54));
            Assert.That(pack.BuildConfig(9, 7, 5).EventHardLimit, Is.EqualTo(26));
            Assert.That(pack.BlastCommands(7), Is.EqualTo(28));
            Assert.That(pack.BlastCommands(7, 5), Is.EqualTo(28));
            Assert.That(pack.BlastCommands(0), Is.Zero);
            Assert.That(RwrPack.Create().BlastCommands(500, 9), Is.Zero, "no blasts, no commands");
            Assert.That(Blast1World.Pack(new BlastRules(new[] { Blast1World.Frag() }, 64)).BlastCommands(3), Is.EqualTo(192));
        }

        // B10: B = 4, k = 1, P = 0: staging 12 N + 1; N = 87381 fits, 87382 does not, and the message names B and N.
        [Test]
        public void TheSettlementBoundIsChecked()
        {
            CombatPack pack = Blast1World.Pack(Blast1World.Rules());
            Assert.That(pack.BlastCommands(87381), Is.EqualTo(4 * 87381));
            var ex = Assert.Throws<ArgumentException>(() => pack.BlastCommands(87382));
            StringAssert.Contains("B = 4, N = 87382", ex.Message);
            // Projectiles count twice: 87380 units with 7 projectiles fit (12 N + 1 + 2 P = 1048575), 8 do not.
            Assert.That(pack.BlastCommands(87380, 7), Is.EqualTo(4 * 87380));
            Assert.Throws<ArgumentException>(() => pack.BlastCommands(87380, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => pack.BlastCommands(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => pack.BlastCommands(1, -1));
        }

        // B10: four blasts on one tick each hit all 50 units: 200 commands, nothing throws, every unit takes all four.
        [Test]
        public void FourBlastsOnFiftyUnitsAllSettle()
        {
            var units = new Blast1World.Unit[50];
            for (int i = 0; i < units.Length; i++) units[i] = Blast1World.U(X + (i % 10) * M / 8, Z + (i / 10) * M / 8, 2);
            var w = new Blast1World(Blast1World.Pack(new BlastRules(new[] { Blast1World.Frag(damage: 20) })), units);
            for (int b = 0; b < 4; b++) w.Blast(X, C, Z);
            Assert.DoesNotThrow(() => w.Step());
            Assert.That(w.Blasts.HitCount, Is.EqualTo(200));
            w.Step();
            for (int i = 0; i < units.Length; i++) Assert.That(w.Health(i), Is.EqualTo(20 * M), "unit " + i);
        }

        private static T Registered<T>(Simulation sim) where T : class
        {
            foreach (object entry in (IEnumerable)typeof(Simulation).GetField("systems", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sim))
            {
                object system = entry.GetType().GetField("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (system is T value) return value;
            }
            return null;
        }

        // Only a pack with blasts gets the system, from the world builder too.
        [Test]
        public void OnlyAPackWithBlastsGetsTheSystem()
        {
            var plain = new M6World(WarCore.Presentation.DemoRecipes.M6(RwrPack.Load("rwr-m6")));
            Assert.That(Registered<BlastSystem>(plain.Sim), Is.Null);
            Assert.That(plain.Combat.Blasts, Is.Null);
            var session = WarCore.Presentation.SimSession.Create(WarCore.Presentation.BlastDemo.Recipe(), WarCore.Presentation.BlastDemo.MapBytes());
            Simulation sim = (Simulation)typeof(WarCore.Presentation.SimSession).GetField("sim", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(session);
            BlastSystem system = Registered<BlastSystem>(sim);
            Assert.That(system, Is.Not.Null);
            Assert.That(system.Order, Is.EqualTo(28));
            Assert.That(system.StateTypeId, Is.EqualTo("combat.blasts"));
        }
    }
}
