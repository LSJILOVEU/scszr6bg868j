using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using static WarCore.Tests.RespawnAi2World;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 A12: whole matches on the three-lane map (100 on the field and 50 reserves a team), seeds 41-45, with
    // match.reentryTicks 90 (the replay setup "lanes-reentry") and without. No line is set on who wins. With the switch,
    // after every step: no team has had more reserves enter than it has dead, and no team has more men on the field than at
    // the start. The numbers of each match are written to the test output (the table in the pull request is made from them).
    public sealed class RespawnAi2LanesTests
    {
        private const string LanesPath = "Assets/WarCore/Maps/lanes-playtest.map.json", Layer = "replay-lanes-reentry";
        private const int ReentryTicks = 90, Limit = 9000;

        private static M6World Lanes(int seed, bool reentry)
        {
            string text = File.ReadAllText(LanesPath);
            Assert.That(text, Does.Contain("\"seed\": 41").And.Contain("\"pack\": \"rwr-m6\""));
            text = text.Replace("\"seed\": 41", "\"seed\": " + seed);
            if (reentry) text = text.Replace("\"pack\": \"rwr-m6\"", "\"pack\": \"" + Layer + "\"");
            MapBakeResult bake = E1aSamples.Bake(Encoding.UTF8.GetBytes(text));
            ContentPack pack = reentry
                ? ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
                    { [Layer] = "{ \"id\": \"" + Layer + "\", \"extends\": \"rwr-m6\", \"match\": { \"reentryTicks\": " + ReentryTicks + " } }" }, FileContentSource.Rwr()), Layer)
                : RwrPack.Load(bake.PackId);
            M6World w = null;
            Assert.DoesNotThrow(() => w = new M6World(MapRecipe.From(bake, pack), bake.WcMapBytes));
            return w;
        }

        // One match to its end: the entries of each team, the entries from each place (the bases, then each point by index),
        // the men on the field every 300 ticks, the end.
        private static string Play(int seed, bool reentry, out long[] entries)
        {
            M6World w = Lanes(seed, reentry);
            int points = w.Recipe.Match.Points.Length;
            int[] start = { Fielded(w, 1), Fielded(w, 2) };
            Assert.That((start[0], start[1], Reserves(w, 1), Reserves(w, 2), w.Reinforce.ByDebt), Is.EqualTo((100, 100, 50, 50, reentry)));
            entries = new long[2];
            var places = new long[2, points + 1];
            var field = new StringBuilder();
            while (!w.Sim.IsFinished && w.Sim.Clock.Tick < Limit)
            {
                Assert.DoesNotThrow(() => w.Step());
                foreach (DeployRecord r in w.Reinforce.Deployed) { entries[r.Team - 1]++; places[r.Team - 1, r.PointIndex + 1]++; }
                if (w.Sim.Clock.Tick % 300 == 0) field.Append(' ').Append(Fielded(w, 1)).Append('/').Append(Fielded(w, 2));
                if (!reentry) continue;
                for (int team = 1; team <= 2; team++)
                {
                    Assert.That(w.Reinforce.Entered(team), Is.LessThanOrEqualTo(DeadOf(w, team)), "entered <= dead, team " + team + ", seed " + seed + ", tick " + w.Sim.Clock.Tick);
                    Assert.That(Fielded(w, team), Is.LessThanOrEqualTo(start[team - 1]), "on the field <= at the start, team " + team + ", seed " + seed + ", tick " + w.Sim.Clock.Tick);
                    Assert.That(w.Reinforce.Entered(team), Is.EqualTo(entries[team - 1]), "every entry is recorded, team " + team);
                }
            }
            for (int team = 1; team <= 2; team++)
                Assert.That(Reserves(w, team) + entries[team - 1], Is.EqualTo(50), "a reserve is used once, team " + team + ", seed " + seed);
            var line = new StringBuilder();
            line.Append("A12 seed ").Append(seed).Append(reentry ? " reentry" : " clock").Append(" | entries ").Append(entries[0]).Append('/').Append(entries[1]).Append(" | by place (base, points)");
            for (int team = 0; team < 2; team++)
            {
                line.Append(team == 0 ? " t1" : " t2");
                for (int p = 0; p <= points; p++) line.Append(p == 0 ? ' ' : ',').Append(places[team, p]);
            }
            line.Append(" | field every 300").Append(field).Append(" | end ").Append(w.Sim.Clock.Tick).Append(" winner ").Append(w.Match.Winner).Append(' ').Append(w.Match.EndReason);
            line.Append(" | reserves left ").Append(Reserves(w, 1)).Append('/').Append(Reserves(w, 2)).Append(" | hash ").Append(w.Hash.ToString("X16"));
            return line.ToString();
        }

        [TestCase(41)]
        [TestCase(42)]
        [TestCase(43)]
        [TestCase(44)]
        [TestCase(45)]
        public void A12_WholeMatches(int seed)
        {
            string on = Play(seed, true, out long[] entered);
            TestContext.Out.WriteLine(on);
            TestContext.Out.WriteLine(Play(seed, false, out _));
            Assert.That(entered[0] + entered[1], Is.GreaterThan(0), "somebody died and was replaced");
            Assert.That(Play(seed, true, out _), Is.EqualTo(on), "the same seed twice");
        }
    }
}
