using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal static class AiPlaytest
    {
        internal const int Ticks = 3000;

        // PLACEHOLDER: all nine AI values in rwr-ai-playtest await PF1 calibration from recordings.
        internal static ContentPack Pack() => RwrPack.Load("rwr-ai-playtest");

        internal static WorldRecipe Recipe(bool endless = false)
        {
            WorldRecipe recipe = DemoRecipes.TerrainMatch(RwrPack.Load(RwrPack.M6Layer), Pack());
            return endless ? recipe.WithRuleSet(MatchMode.TestNeverEndsId) : recipe;
        }

        internal static M6World World(bool endless = false) => new M6World(Recipe(endless), TerrainDemo.MapBytes());

        internal static object Field(object system, string name)
        {
            Assert.That(system, Is.Not.Null, name + " requires an installed system");
            return system.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(system);
        }
    }

    public sealed class AiPlaytestMatchTests
    {
        [Test]
        public void AllNineValuesReachTheInstalledSystems()
        {
            M6World w = AiPlaytest.World();
            AiRules rules = w.Recipe.TerrainPack.Section<AiRules>();
            Assert.That((rules.FailedSlotTicks, rules.TargetMemoryTicks, rules.SearchTicks, rules.ReactionTicks,
                rules.SenseTicks, rules.BurstShots, rules.BurstPauseTicks, rules.SuppressTicks, rules.MeleeChaseRange),
                Is.EqualTo((90, 300, 90, 15, 6, 3, 30, 90, 12)));
            Assert.That(AiPlaytest.Field(w.System<FailedSlotMemory>(), "ticks"), Is.EqualTo(rules.FailedSlotTicks));
            Assert.That(AiPlaytest.Field(w.System<TargetMemory>(), "ticks"), Is.EqualTo(rules.TargetMemoryTicks));
            Assert.That(AiPlaytest.Field(w.System<TargetSearch>(), "ticks"), Is.EqualTo(rules.SearchTicks));
            Assert.That(AiPlaytest.Field(w.System<TargetSuppress>(), "ticks"), Is.EqualTo(rules.SuppressTicks));
            ReactionFire fire = w.System<ReactionFire>();
            Assert.That(AiPlaytest.Field(fire, "ticks"), Is.EqualTo(rules.ReactionTicks));
            Assert.That(AiPlaytest.Field(fire, "senseTicks"), Is.EqualTo(rules.SenseTicks));
            Assert.That(AiPlaytest.Field(fire, "burstShots"), Is.EqualTo(rules.BurstShots));
            Assert.That(AiPlaytest.Field(fire, "burstPauseTicks"), Is.EqualTo(rules.BurstPauseTicks));
            Assert.That(AiPlaytest.Field(w.Combat, "autoFire"), Is.SameAs(fire));
            SimpleAiSystem ai = w.System<SimpleAiSystem>();
            Assert.That(AiPlaytest.Field(ai, "meleeChaseRange"), Is.EqualTo(SimFix.FromInt(rules.MeleeChaseRange)));
            Assert.That(AiPlaytest.Field(ai, "memory"), Is.SameAs(w.System<FailedSlotMemory>()));
            Assert.That(AiPlaytest.Field(ai, "contacts"), Is.SameAs(w.System<TargetMemory>()));
            Assert.That(AiPlaytest.Field(ai, "search"), Is.SameAs(w.System<TargetSearch>()));
            Assert.That(AiPlaytest.Field(ai, "suppress"), Is.SameAs(w.System<TargetSuppress>()));
        }

        [Test]
        public void ProductionRulesFinishTheTerrainMatch()
        {
            M6World w = AiPlaytest.World();
            while (!w.Session.IsFinished && w.Session.Tick < L6.StepLimit) w.Session.StepOnce();
            Assert.That(w.Session.IsFinished, Is.True);
            Assert.That(w.Match.Winner, Is.EqualTo(1).Or.EqualTo(2));
            Assert.That(w.Match.EndTick, Is.LessThanOrEqualTo(9000));
            TestContext.Progress.WriteLine("AI playtest result: tick=" + w.Match.EndTick + ", winner=" + w.Match.Winner);
        }

        [Test]
        public void ThreeThousandTicksFightCaptureAndRepeatEveryHash()
        {
            M6World a = AiPlaytest.World(endless: true), b = AiPlaytest.World(endless: true);
            var firstFire = new long[3];
            var hits = new int[3];
            int captures = 0, transfers = 0;
            long firstCapture = 0, firstTransfer = 0;
            for (int tick = 1; tick <= AiPlaytest.Ticks; tick++)
            {
                a.Step(); b.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
                foreach (FireRecord fire in a.Combat.Fires)
                {
                    if (firstFire[fire.ShooterTeam] == 0) firstFire[fire.ShooterTeam] = tick;
                    if (fire.Outcome == FireOutcome.Hit) hits[fire.ShooterTeam]++;
                }
                foreach (CaptureRecord change in a.Capture.Changes)
                {
                    captures++;
                    if (firstCapture == 0) firstCapture = tick;
                    if (change.OldOwner == 0) continue;
                    transfers++;
                    if (firstTransfer == 0) firstTransfer = tick;
                }
            }
            Assert.That(a.Sim.Clock.Tick, Is.EqualTo(AiPlaytest.Ticks));
            Assert.That(b.Sim.Clock.Tick, Is.EqualTo(AiPlaytest.Ticks));
            for (int team = 1; team <= 2; team++)
            {
                Assert.That(firstFire[team], Is.GreaterThan(0), "team " + team + " fired");
                Assert.That(hits[team], Is.GreaterThan(0), "team " + team + " hit an enemy");
                Assert.That(A1.Deaths(a, team), Is.GreaterThan(0), "team " + team + " deaths");
            }
            Assert.That(transfers, Is.GreaterThan(0), "an enemy-held point changed owner");
            TestContext.Progress.WriteLine("AI playtest: firstFire=" + firstFire[1] + "," + firstFire[2]
                + "; hits=" + hits[1] + "," + hits[2] + "; deaths=" + A1.Deaths(a, 1) + "," + A1.Deaths(a, 2)
                + "; captures=" + captures + "; firstCapture=" + firstCapture + "; transfers=" + transfers + "; firstTransfer=" + firstTransfer);
        }

        [Test]
        public void ThreeRestoresMatchEveryFollowingTick()
        {
            M6World original = AiPlaytest.World(endless: true);
            var restored = new List<M6World>();
            for (int tick = 1; tick <= AiPlaytest.Ticks; tick++)
            {
                original.Step();
                foreach (M6World copy in restored)
                {
                    copy.Step();
                    Assert.That(copy.Hash, Is.EqualTo(original.Hash), "tick " + tick);
                }
                if (tick != 90 && tick != 300 && tick != 600) continue;
                byte[] snapshot = original.Sim.SaveRetainedSnapshot();
                M6World resumed = AiPlaytest.World(endless: true);
                resumed.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true);
                Assert.That(resumed.Hash, Is.EqualTo(original.Hash), "restore at " + tick);
                Assert.That(resumed.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
                restored.Add(resumed);
            }
            Assert.That(restored.Count, Is.EqualTo(3));
            Assert.That(original.Sim.Clock.Tick, Is.EqualTo(AiPlaytest.Ticks));
            foreach (M6World copy in restored)
                Assert.That(copy.Sim.SaveRetainedSnapshot(), Is.EqualTo(original.Sim.SaveRetainedSnapshot()));
        }
    }
}
