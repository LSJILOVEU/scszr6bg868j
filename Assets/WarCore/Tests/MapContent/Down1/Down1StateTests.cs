using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Down1.Down1World;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D10 (snapshots and determinism) and D11 (the down-crawl replay setup).
    public sealed class Down1StateTests
    {
        private static SimSession Demo() => SimSession.Create(DownDemo.Recipe(), DownDemo.MapBytes());

        private static List<ulong> Hashes(SimSession s, int n)
        {
            var list = new List<ulong>();
            for (int i = 0; i < n; i++) { s.StepOnce(); list.Add(s.StateHash); }
            return list;
        }

        // ---- D11 ----

        // A is downed and B stunned on tick 30, C untouched; B wakes on tick 90; A crawls at Scale(S, [250, 150]) (to 1 raw, the
        // crowd mover's slanted steps) and bleeds out on tick 1830.
        [Test]
        public void D11_TheDownCrawlSetup()
        {
            var w = new Down1Session(Demo());
            w.StepTo(DownDemo.BlastTick - 1);
            for (ulong k = 1; k <= 3; k++) Assert.That(w.Downed(k) || w.Stunned(k), Is.False);
            w.Step();
            Assert.That(w.Downed(DownDemo.KeyA), Is.True);
            Assert.That(w.Stunned(DownDemo.KeyB), Is.True);
            Assert.That(w.Downed(DownDemo.KeyC) || w.Stunned(DownDemo.KeyC), Is.False);
            Assert.That(w.Sim.GameplayResource(w.Sim.Entities.StableOf(w.Unit(DownDemo.KeyB)), 1), Is.LessThan(w.Combat.Pack.MaxHealthRaw));
            for (int i = 0; i < 59; i++) { w.Step(); Assert.That(w.Stunned(DownDemo.KeyB), Is.True, "tick " + w.Tick); }
            w.Step();
            Assert.That(w.Stunned(DownDemo.KeyB), Is.False, "awake on tick 90");
            long crawl = StatLine.Scale(M3TestWorld.Component<WarCore.Interim.MoveIntent>(w.S, DownDemo.KeyA).SpeedPerTick.Raw, new[] { 250, 150 });
            List<long> steps = w.Steps(DownDemo.KeyA, 60);
            Assert.That(steps, Is.All.InRange(crawl - 1, crawl + 1));
            w.StepTo(1829);
            Assert.That(w.Downed(DownDemo.KeyA), Is.True);
            Assert.That(w.Dead(DownDemo.KeyA), Is.False);
            w.Step();
            Assert.That(w.Dead(DownDemo.KeyA), Is.True);
            Assert.That(w.Dead(DownDemo.KeyB) || w.Dead(DownDemo.KeyC), Is.False);
            Assert.That(w.Pos(DownDemo.KeyC).Z.Raw, Is.EqualTo(SimFix.Ratio(DownDemo.BlastZMm + DownDemo.MoveMm, 1000).Raw));
        }

        // ---- D10 ----

        // Saved on tick 35 (A downed within its grace, B stunned, A crawling), 60 (past the grace) and 100 (B awake): a fresh world
        // loading the save steps the same hashes as the uninterrupted run.
        [TestCase(35)]
        [TestCase(60)]
        [TestCase(100)]
        public void D10_ASaveResumesTheSameRun(int tick)
        {
            SimSession a = Demo();
            while (a.Tick < tick) a.StepOnce();
            byte[] save = Stance.Stance1Worlds.SimOf(a).SaveRetainedSnapshot();
            List<ulong> expected = Hashes(a, 60);
            SimSession b = Demo();
            Stance.Stance1Worlds.SimOf(b).RestoreRetainedSnapshot(save);
            b.RefreshView();
            Assert.That(Hashes(b, 60), Is.EqualTo(expected));
        }

        [Test]
        public void D10_TwoRunsAreTheSame() => Assert.That(Hashes(Demo(), 200), Is.EqualTo(Hashes(Demo(), 200)));

        // A save of a casualty world does not load into the same setup on rwr-blast (no casualties), nor the other way round.
        [Test]
        public void D10_SavesDoNotCrossTheCasualtySwitch()
        {
            SimSession Plain() => SimSession.Create(DownDemo.Recipe(RwrPack.Load("rwr-blast")), DownDemo.MapBytes());
            SimSession down = Demo(), plain = Plain();
            for (int i = 0; i < 40; i++) { down.StepOnce(); plain.StepOnce(); }
            byte[] downSave = Stance.Stance1Worlds.SimOf(down).SaveRetainedSnapshot(), plainSave = Stance.Stance1Worlds.SimOf(plain).SaveRetainedSnapshot();
            Assert.That(() => Stance.Stance1Worlds.SimOf(Plain()).RestoreRetainedSnapshot(downSave), Throws.Exception);
            Assert.That(() => Stance.Stance1Worlds.SimOf(Demo()).RestoreRetainedSnapshot(plainSave), Throws.Exception);
        }

        // On tick 3 units 0 and 2 (team 1) each shoot unit 1 (team 2, 100 health) for 60: the second shot downs it (the first
        // went into this tick's ledger); units 1 and 3 shoot unit 0. One Sim runs tick 3, loads its own save of tick 2 and runs
        // on: every hash matches the uninterrupted run. A load puts the units in other entity slots, so the stale ledger of
        // the undone tick 3 would land on whichever unit now holds unit 1's old slot: unit 0, also hit on tick 3.
        [Test]
        public void D10_RewindingOverALedgerTick()
        {
            Down1World Make()
            {
                var w = new Down1World(Pack(Rules(), new ScriptedDamage { Fallback = 60 }),
                    new[] { U(0, 0, 1), U(2 * M, 0, 2), U(0, 2 * M, 1), U(2 * M, 2 * M, 2) });
                w.Shoot(3, 0, 1);
                w.Shoot(3, 2, 1);
                w.Shoot(3, 1, 0);
                w.Shoot(3, 3, 0);
                return w;
            }
            Down1World a = Make(), b = Make();
            var expected = new List<ulong>();
            for (int i = 0; i < 8; i++) { a.Step(); expected.Add(a.Sim.StateHashRetained()); }
            Assert.That(a.Downed(1), Is.True);
            Assert.That(a.Health(1), Is.EqualTo(1L));
            var actual = new List<ulong>();
            b.StepTo(2);
            actual.Add(expected[0]); actual.Add(expected[1]);
            byte[] save = b.Sim.SaveRetainedSnapshot();
            b.Step();
            Assert.That(b.Downed(1), Is.True);
            b.Sim.RestoreRetainedSnapshot(save);
            for (int i = 2; i < 8; i++) { b.Step(); actual.Add(b.Sim.StateHashRetained()); }
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(b.Health(1), Is.EqualTo(1L));
        }

        [TestCase(0, 1L, 2L)]
        [TestCase(3, 1L, 2L)]
        [TestCase(2, 5L, 4L)]
        [TestCase(1, -1L, 4L)]
        public void D10_BadCasualtyBytesAreRefused(int kind, long start, long due)
        {
            var writer = new SimStateWriter();
            writer.Byte((byte)kind); writer.Int64(start); writer.Int64(due);
            var reader = new SimStateReader(writer.ToArray());
            Assert.Throws<SimSnapshotException>(() => new CasualtySerializer().Read(reader));
        }

        [TestCase(1, 0L, 0L)]
        [TestCase(2, 7L, 1807L)]
        public void D10_GoodCasualtyBytesRoundTrip(int kind, long start, long due)
        {
            var writer = new SimStateWriter();
            new CasualtySerializer().Write(writer, new Casualty((byte)kind, start, due));
            Casualty back = new CasualtySerializer().Read(new SimStateReader(writer.ToArray()));
            Assert.That(back.Kind, Is.EqualTo((byte)kind));
            Assert.That(back.Start, Is.EqualTo(start));
            Assert.That(back.Due, Is.EqualTo(due));
        }
    }
}
