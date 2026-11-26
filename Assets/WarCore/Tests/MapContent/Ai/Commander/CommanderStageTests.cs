using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RC2-3 acceptance 4, 5 and 6 on the four stages that take the commander (battle, bullets, hamlet: showcase stages; mp:
    // the playtest recipe on the M7 map), each played to its end or 3600 ticks with nobody at the controls. Only the test of
    // acceptance 4 registers the checker: it changes its world's hash, so the others play worlds without it.
    public sealed class CommanderStageTests
    {
        internal const int Limit = 3600;
        internal static readonly string[] Stages = { "battle", "bullets", "hamlet", "mp" };

        internal static M6World World(string id)
        {
            foreach (ShowcaseStage stage in Showcase.Stages)
                if (stage.Id == id) return new M6World(stage.Recipe(), stage.MapBytes());
            if (id == "mp") return new M6World(DemoRecipes.MpPlaytest(), DemoRecipes.M7MapBytes());
            throw new ArgumentException("no stage " + id, nameof(id));
        }

        internal static bool Playing(M6World w) => !w.Session.IsFinished && w.Session.Tick < Limit;

        // The commander's own save section (its five numbers, attack flags and rows), as a snapshot writes it.
        internal static byte[] Section(Commander commander)
        {
            var writer = new SimStateWriter();
            commander.SaveState(writer);
            return writer.ToArray();
        }

        // Acceptance 4: the checker on every tick of the whole match finds nothing, and no row ever names an own point.
        // Card errata 1 (2): the checker only reads, so the same stage without it keeps the same commander section on every
        // tick and ends the same way.
        [TestCaseSource(nameof(Stages))]
        public void TheWholeMatchKeepsEveryRule(string id)
        {
            M6World plain = World(id), watched = World(id);
            CommanderChecker checker = CommanderChecker.Attach(watched);
            Commander a = plain.System<Commander>(), b = watched.System<Commander>();
            Assert.That(a, Is.Not.Null, "the stage takes the commander");
            int rows = 0;
            while (Playing(plain))
            {
                plain.Session.StepOnce(); watched.Session.StepOnce();
                Assert.That(Section(b), Is.EqualTo(Section(a)), "tick " + plain.Session.Tick + ": the commander with the checker beside it");
                rows += a.OrderCount;
            }
            Assert.That(watched.Session.Tick, Is.EqualTo(plain.Session.Tick));
            Assert.That(watched.Session.IsFinished, Is.EqualTo(plain.Session.IsFinished));
            Assert.That(watched.Session.Winner, Is.EqualTo(plain.Session.Winner));
            Assert.That(checker.Checks, Is.EqualTo(plain.Session.Tick), "the checker ran on every tick");
            Assert.That(rows, Is.GreaterThan(0), "the commander gave orders");
            Assert.That(checker.Clean, Is.True, checker.First);
            Assert.That(checker.OwnPointRows, Is.EqualTo(0));
        }

        private static readonly object[] Saves =
        {
            new object[] { "battle", new long[] { 300, 900, 1500 } },
            new object[] { "bullets", new long[] { 300, 900, 1500, 2100 } },
            new object[] { "hamlet", new long[] { 300, 900, 1500, 2100, 2700 } },
            new object[] { "mp", new long[] { 300, 900, 1500, 2100 } },
        };

        // Acceptance 5: each save, loaded into a world that has played another number of ticks, has the hash of the
        // unsaved run at that tick and keeps it on each of the next 300 ticks (to the end when the match ends sooner).
        [TestCaseSource(nameof(Saves))]
        public void ASaveLoadedIntoAWorldAtAnotherTickPlaysOnAsTheUnsavedRun(string id, long[] at)
        {
            M6World run = World(id);
            var hashes = new List<ulong> { run.Session.StateHash };
            var saved = new Dictionary<long, byte[]>();
            while (Playing(run))
            {
                run.Session.StepOnce();
                hashes.Add(run.Session.StateHash);
                if (Array.IndexOf(at, run.Session.Tick) >= 0) saved[run.Session.Tick] = run.Sim.SaveRetainedSnapshot();
            }
            long last = run.Session.Tick;
            M6World other = World(id);
            for (int i = 0; i < 100; i++) other.Session.StepOnce();
            foreach (long tick in at)
            {
                Assert.That(saved.ContainsKey(tick), Is.True, "the match reaches tick " + tick);
                Assert.That(other.Session.Tick, Is.Not.EqualTo(tick), "the loading world stands at another tick");
                other.Sim.RestoreRetainedSnapshot(saved[tick]);
                Assert.That(other.Session.StateHash, Is.EqualTo(hashes[(int)tick]), "loaded at " + tick);
                for (long t = tick + 1; t <= Math.Min(tick + 300, last); t++)
                {
                    other.Session.StepOnce();
                    Assert.That(other.Session.StateHash, Is.EqualTo(hashes[(int)t]), "saved at " + tick + ", tick " + t);
                }
            }
        }

        // Acceptance 6: the same stage played twice has the same hash on every tick.
        [TestCaseSource(nameof(Stages))]
        public void TheSameSeedPlaysTheSameMatch(string id)
        {
            M6World a = World(id), b = World(id);
            Assert.That(b.Session.StateHash, Is.EqualTo(a.Session.StateHash));
            while (Playing(a))
            {
                a.Session.StepOnce(); b.Session.StepOnce();
                Assert.That(b.Session.StateHash, Is.EqualTo(a.Session.StateHash), "tick " + a.Session.Tick);
            }
            Assert.That(b.Session.Tick, Is.EqualTo(a.Session.Tick));
            Assert.That(b.Session.IsFinished, Is.EqualTo(a.Session.IsFinished));
            Assert.That(b.Session.Winner, Is.EqualTo(a.Session.Winner));
        }
    }
}
