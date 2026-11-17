using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // V2: the ancient pack picks its own end rules (data only): last side standing wins; holding every point does not.
    public sealed class V2AncientRulesTests
    {
        private const string DataDirectory = "Assets/WarCore/Content/Ancient/Data";

        private static ContentPack Ancient() => ContentLoader.Load(new FileContentSource(DataDirectory), "ancient");

        private static readonly ControlPointDef[] Points = { new ControlPointDef(1, SimVec3.Metres(16, 0, 8), SimFix.FromInt(4)) };
        private static readonly SpawnAnchor[] Anchors = { new SpawnAnchor(1, SimVec3.Metres(2, 0, 8)), new SpawnAnchor(2, SimVec3.Metres(30, 0, 8)) };

        private sealed class Reader : IMatchReader
        {
            internal int OwnedOne, AliveOne = 5, AliveTwo = 5;
            public int PointCount => 1;
            public int PointsOwnedBy(int team) => team == 1 ? OwnedOne : 0;
            public void CountAlive(out int one, out int two) { one = AliveOne; two = AliveTwo; }
        }

        // PLACEHOLDER: same fixture values as V1 (seed, formation, terrain bounds, point, anchors).
        private static M6World World()
        {
            ContentPack pack = Ancient();
            var units = new UnitSpawn[40];
            for (int i = 0; i < 20; i++)
            {
                string type = i % 2 == 0 ? "ancient.swordsman" : "ancient.guard";
                units[i] = UnitSpawn.OfType((ulong)i + 1, pack, type, 1, 1, 4000 + 1500 * (i % 4), 4000 + 2000 * (i / 4));
                units[i + 20] = UnitSpawn.OfType((ulong)i + 21, pack, type, 2, 2, 28000 - 1500 * (i % 4), 4000 + 2000 * (i / 4));
            }
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat()).WithMatch(pack.BuildMatch(Points, Anchors))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(pack).WithFacing().WithBlockAi();
            byte[] map = WcMapWriter.Write(Wp23.Make(32, 16, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(),
                2 * SimFix.Scale, (x, z) => 0, new[] { "ancient.ground" }, (x, z) => 0));
            return new M6World(recipe, map);
        }

        [Test]
        public void V2_AncientRulesDifferFromRwr()
        {
            string[] ancient = Ancient().BuildMatch(Points, Anchors).Rules.ToArray();
            string[] rwr = DemoRecipes.M6().Match.Rules.ToArray();
            Assert.That(ancient, Is.EqualTo(new[] { "exhausted", "time-limit" }));
            Assert.That(rwr, Is.EqualTo(new[] { "all-points", "exhausted", "time-limit" }));
            Assert.That(ancient, Is.Not.EqualTo(rwr));
        }

        [Test]
        public void V2_HoldingEveryPointWinsOnlyUnderRwrRules()
        {
            MatchPack ancientPack = Ancient().BuildMatch(Points, Anchors);
            var ancient = new MatchMode(ancientPack);
            var rwr = new MatchMode(DemoRecipes.M6().Match);
            var reader = new Reader { OwnedOne = 1 };
            ancient.Bind(reader);
            rwr.Bind(new Reader { OwnedOne = 1 });
            ancient.Evaluate(1);
            rwr.Evaluate(1);
            Assert.That((rwr.Winner, rwr.EndReason), Is.EqualTo((1, MatchEndReason.AllPoints)));
            Assert.That((ancient.Winner, ancient.EndReason), Is.EqualTo((0, MatchEndReason.None)));
            reader.AliveOne = 0;
            ancient.Evaluate(2);
            Assert.That((ancient.Winner, ancient.EndReason, ancient.EndTick), Is.EqualTo((2, MatchEndReason.Exhausted, 2L)));
        }

        [Test]
        public void V2_AncientTimeLimitStillDecides()
        {
            MatchPack pack = Ancient().BuildMatch(Points, Anchors);
            var mode = new MatchMode(pack);
            mode.Bind(new Reader { AliveOne = 7, AliveTwo = 3 });
            mode.Evaluate(pack.MaxTicks - 1);
            Assert.That(mode.Winner, Is.EqualTo(0));
            mode.Evaluate(pack.MaxTicks);
            Assert.That((mode.Winner, mode.EndReason, mode.EndTick), Is.EqualTo((1, MatchEndReason.Time, (long)pack.MaxTicks)));
        }

        [Test]
        public void V2_AncientMatchEndsAtFixedTickSameEachRun()
        {
            M6World first = World(), second = World();
            while (!first.Mode.IsFinished && first.Sim.Clock.Tick < first.Recipe.Match.MaxTicks)
            {
                first.Step(); second.Step();
                Assert.That(second.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            Assert.That((first.Mode.Winner, first.Mode.EndReason, first.Mode.EndTick), Is.EqualTo((2, MatchEndReason.Exhausted, 177L)));
            Assert.That((second.Mode.Winner, second.Mode.EndReason, second.Mode.EndTick), Is.EqualTo((2, MatchEndReason.Exhausted, 177L)));
        }
    }
}
