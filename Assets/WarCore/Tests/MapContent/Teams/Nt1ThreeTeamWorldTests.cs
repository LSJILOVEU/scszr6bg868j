using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // NT-1 A9, A10 and the match system's own count: a whole three-team world runs, repeats, restores and allocates nothing.
    public sealed class Nt1ThreeTeamWorldTests
    {
        // A9: three teams of 10 plus 2 reserves, one point, 300 ticks (or to the end of the match); the same seed twice gives the same hash every tick, and a
        // restore at 150 continues the same way. Only equality is asserted, never a hash value.
        [Test]
        public void Nt1ThreeTeams_RunsRepeatsAndRestoresAt150()
        {
            WorldRecipe recipe = Nt1Worlds.ThreeTeams(alive: 10, reserves: 2, interval: 60, endless: false);
            var first = new M6World(recipe); var repeat = new M6World(recipe);
            Assert.That(first.Recipe.Match.TeamCount, Is.EqualTo(3));
            var hashes = new ulong[301];
            byte[] snapshot = null;
            int fires = 0, last = 300;
            for (int tick = 1; tick <= 300; tick++)
            {
                if (first.Sim.IsFinished) { last = tick - 1; break; }
                first.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + tick);
                hashes[tick] = first.Hash; fires += first.Combat.Fires.Length;
                if (tick == 150) snapshot = first.Sim.SaveRetainedSnapshot();
            }
            Assert.That(fires, Is.GreaterThan(0));
            Assert.That(last, Is.GreaterThan(150), "the match runs past the restore tick");
            var restored = new M6World(recipe);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            for (int tick = 151; tick <= last; tick++)
            {
                restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(hashes[tick]), "tick " + tick);
            }
            Assert.That((restored.Match.Winner, restored.Match.EndTick), Is.EqualTo((first.Match.Winner, first.Match.EndTick)));
            Assert.That(restored.Sim.SaveRetainedSnapshot(), Is.EqualTo(first.Sim.SaveRetainedSnapshot()));
            TestContext.Progress.WriteLine($"NT1 A9 fires={fires} winner={first.Match.Winner} end={first.Match.EndTick} reason={first.Match.EndReason}");
        }

        // A10: after a warm-up of 100 ticks, 100 more allocate nothing.
        [Test]
        public void Nt1ThreeTeams_HundredStepsAfterWarmUp_AllocateNothing()
        {
            var w = new M6World(Nt1Worlds.ThreeTeams(alive: 10, reserves: 2, interval: 60, endless: true));
            w.Step(100);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) w.Sim.Step(default);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        // The match system counts living units per team 1..TeamCount; team 9 counts nowhere.
        [Test]
        public void Nt1MatchSystem_CountsEachTeamAndSkipsTeamNine()
        {
            var w = new M6World(Nt1Worlds.ThreeTeams(alive: 3, reserves: 2, interval: 100000, endless: true));
            Assert.That(w.Match.TeamCount, Is.EqualTo(3));
            var counted = new int[8];
            Assert.DoesNotThrow(() => { Span<int> slots = counted; slots.Fill(7); w.Match.CountAlive(slots); }, "team 9 is skipped");
            Assert.That(counted, Is.EqualTo(new[] { 5, 5, 5, 0, 0, 0, 0, 0 }));
            w.Match.CountAlive(out int one, out int two);
            Assert.That((one, two), Is.EqualTo((5, 5)));
        }

        // A world where team 2 has a base but no units: with three teams the match goes on while teams 1 and 3 stand, and ends for
        // team 1 once the only team 3 unit dies. Hits deal no damage; the kill comes from the test.
        [Test]
        public void Nt1MatchSystem_ThirdTeamFalls_TeamOneWinsByExhaustion()
        {
            var units = new[] { M6World.U(1, 1, 10, 10), M6World.U(2, 3, 70, 30), M6World.U(Nt1Worlds.TeamNine, 9, 40, 38) };
            var bases = new[] { new SpawnAnchor(1, M6World.P(5)), new SpawnAnchor(2, M6World.P(40, 2)), new SpawnAnchor(3, M6World.P(75)) };
            var pack = M6World.Pack(new[] { new ControlPointDef(7, M6World.P(40), SimFix.FromInt(2)) }, bases: bases);
            var w = new M6World(M6World.RecipeOf(units, pack, M6World.Guns(new ZeroResolver())));
            w.Sim.RegisterGameplay("test.death", new M6Damage(w, 5, 2), SimSystemPhase.Queries);
            for (int tick = 1; tick <= 4; tick++)
            {
                w.Step();
                Assert.That(w.Match.Winner, Is.Zero, "teams 1 and 3 stand, tick " + tick);
            }
            for (int i = 0; i < 3 && w.Match.Winner == 0; i++) w.Step();
            Assert.That(w.Dead(2), Is.True);
            Assert.That((w.Match.Winner, w.Match.EndReason), Is.EqualTo((1, MatchEndReason.Exhausted)));
            Assert.That(w.Match.EndTick, Is.InRange(5L, 6L));
        }
    }
}
