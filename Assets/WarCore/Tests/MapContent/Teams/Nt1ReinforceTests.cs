using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // NT-1 A4, A5: reinforcement for every team 1..TeamCount, in team order; more than two teams save TeamCount triples (version 2).
    public sealed class Nt1ReinforceTests
    {
        private const int Interval = 5;

        // One base and one reserve per team; a 1 x 1 grid, so a reserve lands on its base exactly.
        private static M6World World(int teams)
        {
            var bases = new SpawnAnchor[teams];
            var units = new UnitSpawn[teams];
            for (int t = 1; t <= teams; t++)
            {
                bases[t - 1] = new SpawnAnchor(t, M6World.P(8 * t, 10));
                units[t - 1] = M6World.U((ulong)t, t, reserve: true);
            }
            var pack = M6World.Pack(interval: Interval, columns: 1, rows: 1, bases: bases);
            return new M6World(M6World.Endless(M6World.RecipeOf(units, pack, M6World.Guns(new ZeroResolver()))));
        }

        private static byte[] Saved(ReinforceSystem system)
        {
            var writer = new SimStateWriter();
            system.SaveState(writer);
            return writer.ToArray();
        }

        [Test]
        public void Nt1Reinforce_ThreeTeams_EachDeploysOnTheDueTickInTeamOrder()
        {
            var w = World(3);
            Assert.That(w.Recipe.Match.TeamCount, Is.EqualTo(3));
            Assert.That((w.Reinforce.NextTick(1), w.Reinforce.NextTick(2), w.Reinforce.NextTick(3)), Is.EqualTo((5L, 5L, 5L)));
            w.Step(Interval - 1);
            Assert.That(w.Reinforce.Deployed.Length, Is.Zero);
            Assert.That(w.Reinforce.Attempts(3), Is.Zero);
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(Interval));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(3));
            for (int t = 1; t <= 3; t++)
            {
                DeployRecord record = w.Reinforce.Deployed[t - 1];
                Assert.That((record.Tick, record.Team, record.Point), Is.EqualTo(((long)Interval, t, M6World.P(8 * t, 10))), "team " + t);
                Assert.That(record.Unit, Is.EqualTo(w.Sim.Entities.StableOf(w.Unit((ulong)t))));
            }
            Assert.That((w.Reinforce.Attempts(3), w.Reinforce.Successes(3), w.Reinforce.NextTick(3)), Is.EqualTo((1L, 1L, 10L)));
            Assert.That((w.Reinforce.Attempts(1), w.Reinforce.Successes(1), w.Reinforce.NextTick(1)), Is.EqualTo((1L, 1L, 10L)));
            w.Step(Interval);
            // No reserve left: the attempt counts, nothing deploys.
            Assert.That(w.Reinforce.Deployed.Length, Is.Zero);
            Assert.That((w.Reinforce.Attempts(3), w.Reinforce.Successes(3), w.Reinforce.NextTick(3)), Is.EqualTo((2L, 1L, 15L)));
        }

        [Test]
        public void Nt1Reinforce_VersionAndBytes_TwoTeamsKeepVersionOne()
        {
            var two = World(2); var three = World(3); var eight = World(8);
            Assert.That((two.Reinforce.StateVersion, three.Reinforce.StateVersion, eight.Reinforce.StateVersion), Is.EqualTo((1, 2, 2)));
            Assert.That((Saved(two.Reinforce).Length, Saved(three.Reinforce).Length, Saved(eight.Reinforce).Length), Is.EqualTo((64, 88, 208)));
        }

        [Test]
        public void Nt1Reinforce_EightTeams_EveryTeamDeploys()
        {
            var w = World(8);
            w.Step(Interval);
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(8));
            for (int t = 1; t <= 8; t++) Assert.That(w.Reinforce.Deployed[t - 1].Team, Is.EqualTo(t));
            Assert.That((w.Reinforce.Attempts(8), w.Reinforce.Successes(8), w.Reinforce.NextTick(8)), Is.EqualTo((1L, 1L, 10L)));
        }

        private static ulong HashOf(ReinforceSystem system)
        {
            var hash = new StateHash();
            system.WriteState(hash);
            return hash.Value;
        }

        // Every team's three counters are saved, loaded back and hashed: change only team 3's attempts in the bytes and the load
        // shows it, the hash moves, and saving again gives the changed bytes.
        [Test]
        public void Nt1Reinforce_ThreeTeams_TeamThreeCountersReachLoadAndHash()
        {
            var w = World(3);
            w.Step(Interval);
            byte[] bytes = Saved(w.Reinforce);
            ulong before = HashOf(w.Reinforce);
            const int attemptsOfTeamThree = 16 + 2 * 24 + 8;
            Assert.That(BitConverterLittle(bytes, attemptsOfTeamThree), Is.EqualTo(1L));
            bytes[attemptsOfTeamThree] = 7;
            var reader = new SimStateReader(bytes);
            w.Reinforce.LoadState(reader);
            Assert.That(reader.Remaining, Is.Zero);
            Assert.That((w.Reinforce.Attempts(3), w.Reinforce.Attempts(2), w.Reinforce.NextTick(3)), Is.EqualTo((7L, 1L, 10L)));
            Assert.That(HashOf(w.Reinforce), Is.Not.EqualTo(before));
            Assert.That(Saved(w.Reinforce), Is.EqualTo(bytes));
        }

        private static long BitConverterLittle(byte[] bytes, int at)
        {
            long value = 0;
            for (int i = 7; i >= 0; i--) value = (value << 8) | bytes[at + i];
            return value;
        }

        // A5: saved at 40, restored into a fresh world and run to 200, every tick hash equals the run that never stopped.
        [Test]
        public void Nt1Reinforce_ThreeTeams_SaveAt40RestoreRunTo200()
        {
            WorldRecipe recipe = Nt1Worlds.ThreeTeams(alive: 4, reserves: 4, interval: 30, endless: true);
            var straight = new M6World(recipe);
            var hashes = new ulong[201];
            byte[] snapshot = null;
            int deploys = 0;
            for (int tick = 1; tick <= 200; tick++)
            {
                straight.Step(); hashes[tick] = straight.Hash; deploys += straight.Reinforce.Deployed.Length;
                if (tick == 40) snapshot = straight.Sim.SaveRetainedSnapshot();
            }
            Assert.That(straight.Reinforce.StateVersion, Is.EqualTo(2));
            Assert.That(straight.Reinforce.Successes(3), Is.GreaterThan(1));
            var restored = new M6World(recipe);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(hashes[40]));
            Assert.That((restored.Reinforce.Attempts(3), restored.Reinforce.NextTick(3)), Is.EqualTo((1L, 60L)));
            for (int tick = 41; tick <= 200; tick++)
            {
                restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(hashes[tick]), "tick " + tick);
            }
            Assert.That(restored.Reinforce.Successes(3), Is.EqualTo(straight.Reinforce.Successes(3)));
            TestContext.Progress.WriteLine("NT1 A5 deploys=" + deploys + " team3=" + straight.Reinforce.Successes(3));
        }
    }
}
