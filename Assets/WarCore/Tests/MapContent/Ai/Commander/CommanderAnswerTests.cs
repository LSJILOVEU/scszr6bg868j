using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // RC2-3 card appendix 7: the standard answers jia, yi, bing and geng on the boards of CommanderScene (2400 ticks each),
    // with the checker of every tick attached.
    public sealed class CommanderAnswerTests
    {
        private const int Ticks = 2400;

        // Jia (bing's early half with squads): on every tick A is team 1's, no team-1 row names A and the commander neither
        // attacks A nor wants anybody there, however many enemies stand near it; no row leaves a point that is still a target.
        [TestCase(false)]
        [TestCase(true)]
        public void JiaNobodyIsSentToTheOwnPointHoweverManyEnemiesComeNear(bool squads)
        {
            var s = new CommanderScene(CommanderScene.Kind.Early, squads);
            int blueTicks = 0, mostNear = 0;
            long bTaken = -1;
            var rows = new Dictionary<ulong, int>();
            for (int t = 1; t <= Ticks; t++)
            {
                s.Step();
                if (bTaken < 0 && s.Owner(1) == 1) bTaken = t;
                LeftOnlyForAReason(s, rows, t);
                if (s.Owner(0) != 1) continue;
                blueTicks++;
                mostNear = System.Math.Max(mostNear, s.Commander.EnemiesNear(1, 0));
                Assert.That(s.RowsAt(0), Is.EqualTo(0), "tick " + t + ": team-1 rows naming its own point A");
                Assert.That(s.Commander.Desired(1, 0), Is.EqualTo(0), "tick " + t + ": the commander's view of A");
                Assert.That(s.Commander.Attacks(1, 0), Is.False, "tick " + t + ": the commander's view of A");
            }
            Assert.That(blueTicks, Is.EqualTo(Ticks), "A stays team 1's");
            Assert.That(mostNear, Is.GreaterThanOrEqualTo(8), "enemies did come near A");
            Assert.That(bTaken, Is.GreaterThan(0), "team 1 takes B");
            Assert.That(s.Checker.Clean, Is.True, s.Checker.First);
            Assert.That(s.Checker.Checks, Is.EqualTo(Ticks));
        }

        // Yi (bing's idle half with squads): A stays team 1's and no row names it; on the first tick after the batch is let go
        // (the commander's first look at them) every unit of the batch remembers B, the one target.
        [TestCase(false)]
        [TestCase(true)]
        public void YiALetGoBatchGoesToTheTargetAtOnce(bool squads)
        {
            var s = new CommanderScene(CommanderScene.Kind.Idle, squads);
            var rows = new Dictionary<ulong, int>();
            bool lookedAtBatch = false;
            for (int t = 1; t <= Ticks; t++)
            {
                s.Step();
                LeftOnlyForAReason(s, rows, t);
                Assert.That(s.Owner(0), Is.EqualTo(1), "tick " + t + ": A stays team 1's");
                Assert.That(s.RowsAt(0), Is.EqualTo(0), "tick " + t + ": team-1 rows naming its own point A");
                if (s.ReleasedAt < 0 || t != s.ReleasedAt + 1) continue;
                lookedAtBatch = true;
                for (int i = 0; i < s.BatchCount; i++)
                    Assert.That(s.Row((ulong)(s.BatchFirst + i)), Is.EqualTo(1), "tick " + t + ": batch unit " + (s.BatchFirst + i));
                Assert.That(s.Commander.Attacks(1, 0), Is.False, "B is the one target");
                Assert.That(s.Commander.Attacks(1, 1), Is.True, "B is the one target");
            }
            Assert.That(lookedAtBatch, Is.True, "the batch was let go");
            Assert.That(s.Checker.Clean, Is.True, s.Checker.First);
        }

        // Geng ("lost and retaken", no squads): from the tick A first belongs to team 2 on, A is a target; no row goes from B
        // to A; on the first tick after the batch is let go the commander wants 7 at A and 7 at B and sends the batch as the
        // hand count of R5 does: keys 11..17 to A, 18..20 to B; the four units going to B keep their rows.
        [Test]
        public void GengALostOwnPointBecomesATargetForTheIdleGroupsOnly()
        {
            var s = new CommanderScene(CommanderScene.Kind.Lose, false);
            var rows = new Dictionary<ulong, int>();
            bool counted = false;
            int[] before = new int[4];
            for (int t = 1; t <= Ticks; t++)
            {
                s.Step();
                foreach (UnitSpawn spawn in s.W.Recipe.Units)
                {
                    if (spawn.Team != 1) continue;
                    ulong key = spawn.Key;
                    int now = s.Row(key);
                    if (s.LostAt > 0 && rows.TryGetValue(key, out int was))
                        Assert.That(was == 1 && now == 0, Is.False, "tick " + t + ": unit " + key + " turned from B to A");
                    rows[key] = now;
                }
                if (s.LostAt > 0) Assert.That(s.Commander.Attacks(1, 0), Is.True, "tick " + t + ": A is a target from the tick it was lost");
                if (s.ReleasedAt > 0 && t == s.ReleasedAt) for (int i = 0; i < 4; i++) before[i] = s.Row((ulong)i + 1);
                if (s.ReleasedAt < 0 || t != s.ReleasedAt + 1) continue;
                counted = true;
                Assert.That(s.Commander.Desired(1, 0), Is.EqualTo(7), "desired at A and B");
                Assert.That(s.Commander.Desired(1, 1), Is.EqualTo(7), "desired at A and B");
                var batch = new int[10];
                for (int i = 0; i < 10; i++) batch[i] = s.Row((ulong)(11 + i));
                Assert.That(batch, Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0, 0, 1, 1, 1 }), "rows of keys 11..20");
                Assert.That(before, Is.EqualTo(new[] { 1, 1, 1, 1 }));
                for (int i = 0; i < 4; i++) Assert.That(s.Row((ulong)i + 1), Is.EqualTo(1), "unit " + (i + 1) + " keeps B");
                Assert.That(s.RowsAt(0), Is.EqualTo(7));
            }
            Assert.That(s.LostAt, Is.GreaterThan(0), "team 2 takes A");
            Assert.That(s.ReleasedAt, Is.EqualTo(s.LostAt + 30));
            Assert.That(counted, Is.True, "the batch was let go");
            Assert.That(s.Checker.Clean, Is.True, s.Checker.First);
        }

        // Jia's third point on every tick: no living team-1 unit's row leaves a point that its team still attacks on that tick
        // (open ground: every group reaches every point; on these boards no unit changes group while its row would change).
        private static void LeftOnlyForAReason(CommanderScene s, Dictionary<ulong, int> rows, int t)
        {
            foreach (UnitSpawn spawn in s.W.Recipe.Units)
            {
                if (spawn.Team != 1 || !s.Alive(spawn.Key)) continue;
                int now = s.Row(spawn.Key);
                if (rows.TryGetValue(spawn.Key, out int was) && was >= 0 && now != was)
                    Assert.That(s.Commander.Attacks(1, was), Is.False, "tick " + t + ": unit " + spawn.Key + " left point " + was + " that is still a target");
                rows[spawn.Key] = now;
            }
        }
    }
}
