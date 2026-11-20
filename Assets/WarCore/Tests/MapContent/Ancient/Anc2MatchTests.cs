using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class Anc2MatchTests
    {
        // Baseline recorded by this package (PLACEHOLDER fixture: formation, seed 41, spear numbers in ancient-field.json).
        private const int Winner = 2, EndTick = 458, DeadTeam1 = 60, DeadTeam2 = 51;
        private const int SwordHits = 241, SpearHits = 100, SwordBlocked = 68, SpearBlocked = 40;
        private const ulong EndHash = 0x72A6C627BB3E21C1;

        internal sealed class Result
        {
            internal M6World World;
            internal Anc2.Tally Tally = new Anc2.Tally();
            internal List<ulong> Hashes = new List<ulong>();
        }

        // Runs the field to its end; the hash is recorded every 50 ticks and at the end.
        internal static Result Run(ContentPack pack, ulong seed = 41)
        {
            var r = new Result { World = Anc2.World(pack, seed) };
            M6World w = r.World;
            while (!w.Mode.IsFinished && w.Sim.Clock.Tick < w.Recipe.Match.MaxTicks)
            {
                w.Step();
                r.Tally.Add(w.Combat);
                if (w.Sim.Clock.Tick % 50 == 0) r.Hashes.Add(w.Hash);
            }
            r.Hashes.Add(w.Hash);
            return r;
        }

        [Test]
        public void TheFieldRunsToTheEnd()
        {
            Result r = Run(Anc2.Load());
            M6World w = r.World;
            Assert.That(w.Mode.IsFinished, Is.True);
            Assert.That(w.Mode.EndReason, Is.EqualTo(MatchEndReason.Exhausted));
            Assert.That(new[] { w.Mode.Winner, (int)w.Mode.EndTick, Anc2.Deaths(w, 1), Anc2.Deaths(w, 2) },
                Is.EqualTo(new[] { Winner, EndTick, DeadTeam1, DeadTeam2 }));
            Assert.That(w.Hash, Is.EqualTo(EndHash));
        }

        [Test]
        public void SwordSpearAndShieldAllCountInTheRun()
        {
            Anc2.Tally t = Run(Anc2.Load()).Tally;
            Assert.That(new[] { t.SwordHits, t.SpearHits, t.SwordBlocked, t.SpearBlocked },
                Is.EqualTo(new[] { SwordHits, SpearHits, SwordBlocked, SpearBlocked }));
            Assert.That(t.SwordHits, Is.GreaterThan(0));
            Assert.That(t.SpearHits, Is.GreaterThan(0));
            Assert.That(t.Blocked, Is.GreaterThan(0));
        }

        [Test]
        public void TheSameSeedGivesTheSameHashEvery50TicksAndAtTheEnd()
        {
            Result first = Run(Anc2.Load()), second = Run(Anc2.Load());
            Assert.That(first.Hashes.Count, Is.GreaterThan(2));
            Assert.That(second.Hashes, Is.EqualTo(first.Hashes));
        }

        [Test]
        public void ASnapshotTakenMidRunRestoresToTheSameEnd()
        {
            ContentPack pack = Anc2.Load();
            M6World whole = Anc2.World(pack), early = Anc2.World(pack), resumed = Anc2.World(pack);
            whole.Step(150); early.Step(150);
            resumed.Sim.RestoreRetainedSnapshot(early.Sim.SaveRetainedSnapshot());
            Assert.That(resumed.Hash, Is.EqualTo(whole.Hash));
            while (!whole.Mode.IsFinished && whole.Sim.Clock.Tick < whole.Recipe.Match.MaxTicks)
            {
                whole.Step(); resumed.Step();
                if (whole.Sim.Clock.Tick % 50 == 0) Assert.That(resumed.Hash, Is.EqualTo(whole.Hash), "tick " + whole.Sim.Clock.Tick);
            }
            Assert.That(resumed.Mode.IsFinished, Is.True);
            Assert.That(resumed.Hash, Is.EqualTo(EndHash));
            Assert.That(resumed.Mode.EndTick, Is.EqualTo(EndTick));
        }

        // Data sensitivity (this package has no product code to mutate): change one number in an in-memory copy of a layer and
        // the run has to change.
        [Test]
        public void WithoutTheShieldNothingIsBlocked()
        {
            Result r = Run(Anc2.Load(Anc2.Replace("ancient", "\"block\": \"ancient.shield\", ", "")));
            Assert.That(r.Tally.Blocked, Is.Zero);
            Assert.That(r.World.Hash, Is.Not.EqualTo(EndHash));
        }

        [Test]
        public void ASpearThatDoesOneDamageChangesTheEnd()
        {
            Result r = Run(Anc2.Load(Anc2.Replace("ancient-field", "\"damage\": 40", "\"damage\": 1")));
            Assert.That(r.Tally.SpearHits + r.Tally.SpearBlocked, Is.Zero);
            Assert.That(r.World.Mode.IsFinished && r.World.Mode.EndTick == EndTick, Is.False);
        }
    }
}
