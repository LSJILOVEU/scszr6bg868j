using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.LedGame;

namespace WarCore.Tests
{
    // RC2-1: the keys of the squad the player leads (ShowcaseActions), the tick an order from a key is queued for
    // (SimSession.FreeTick) and what the HUD tells about the squad (ShowcaseHud).
    public sealed class Rc21LedKeysTests
    {
        private const string None = "SQUAD  none (T: follow me, C: go where you aim)\n";

        private static ShowcaseActions Actions(ShowcaseStage stage) => new ShowcaseActions(stage, stage.StanceScales().Length);

        private static BridgeCommand Last(SimSession s) => s.CommandLog[s.CommandLog.Count - 1];

        [Test]
        public void TheThreeKeysOrderTheSquadAndTheStatusLineTellsWhatItDoes()
        {
            ShowcaseStage stage = Stage("battle");
            SimSession s = stage.Start();
            ShowcaseActions actions = Actions(stage);
            Assert.That(ShowcaseHud.Squad(s), Is.EqualTo(None));
            Assert.That(actions.Dismiss(s), Is.False, "there is no squad to dismiss");
            Assert.That(s.FreeTick(1), Is.EqualTo(1L), "and nothing was queued");

            Assert.That(actions.FollowOrHold(s), Is.True);
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(1));
            Assert.That(s.LedCount, Is.EqualTo(8));
            Assert.That(ShowcaseHud.Squad(s), Is.EqualTo("SQUAD  8 follow you (T: hold here, C: go where you aim, X: dismiss)\n"));

            Assert.That(actions.FollowOrHold(s), Is.True);
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(2), "the same key while they follow: hold");
            Assert.That(ShowcaseHud.Squad(s), Is.EqualTo("SQUAD  8 hold their place (T: follow me, C: go where you aim, X: dismiss)\n"));

            Assert.That(actions.FollowOrHold(s), Is.True);
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(1), "and again: follow");

            Assert.That(s.Current.TryGet(s.ControlledKey.Value, out ViewEntity me), Is.True);
            long x = (long)Math.Round(me.X * 1000.0) + 20000, z = (long)Math.Round(me.Z * 1000.0);
            Assert.That(actions.GoTo(s, x, z), Is.True);
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(2), "sent to a point, the squad holds there");
            BridgeCommand sent = Last(s);
            Assert.That(sent.Kind, Is.EqualTo(BridgeCommandKind.MoveTo));
            Assert.That(sent.Squad, Is.EqualTo(BridgeCommand.Led));
            Assert.That(sent.XMm, Is.EqualTo(x));
            Assert.That(sent.ZMm, Is.EqualTo(z));
            Assert.That(sent.AtTick, Is.EqualTo(s.Tick));

            Assert.That(actions.Dismiss(s), Is.True);
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(0));
            Assert.That(s.LedCount, Is.EqualTo(0));
            Assert.That(ShowcaseHud.Squad(s), Is.EqualTo(None));

            var kinds = new List<BridgeCommandKind>();
            foreach (BridgeCommand command in s.CommandLog)
            {
                kinds.Add(command.Kind);
                Assert.That(command.Squad, Is.EqualTo(BridgeCommand.Led));
            }
            Assert.That(kinds, Is.EqualTo(new[] { BridgeCommandKind.Follow, BridgeCommandKind.Hold, BridgeCommandKind.Follow, BridgeCommandKind.MoveTo, BridgeCommandKind.Dismiss }));
            Assert.That(() => actions.FollowOrHold(null), Throws.ArgumentNullException);
            Assert.That(() => actions.GoTo(null, 0, 0), Throws.ArgumentNullException);
            Assert.That(() => actions.Dismiss(null), Throws.ArgumentNullException);
            Assert.That(() => ShowcaseHud.Squad((SimSession)null), Throws.ArgumentNullException);
        }

        [Test]
        public void GoThereIsTheFirstOrderToo()
        {
            ShowcaseStage stage = Stage("battle");
            SimSession s = stage.Start();
            Assert.That(s.Current.TryGet(s.ControlledKey.Value, out ViewEntity me), Is.True);
            Assert.That(Actions(stage).GoTo(s, (long)Math.Round(me.X * 1000.0) + 10000, (long)Math.Round(me.Z * 1000.0)), Is.True);
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(2));
            Assert.That(s.LedCount, Is.EqualTo(8));
        }

        [Test]
        public void APointTheSessionRefusesQueuesNothingAndAPointNobodyCanStandOnMakesNoSquad()
        {
            ShowcaseStage stage = Stage("battle");
            SimSession s = stage.Start();
            ShowcaseActions actions = Actions(stage);
            Assert.That(actions.GoTo(s, 10_000_000_000L, 61000), Is.False, "beyond the world's bounds");
            Assert.That(actions.GoTo(s, 26000, 61000, 2), Is.False, "a floor this map does not have");
            Assert.That(s.FreeTick(1), Is.EqualTo(1L));
            // Off the map: the session takes the point, the simulation finds nobody can stand there and makes no squad.
            Assert.That(actions.GoTo(s, -5000, 61000), Is.True);
            s.StepOnce();
            Assert.That(s.CommandLog.Count, Is.EqualTo(1));
            Assert.That(s.LedOrder, Is.EqualTo(0));
            Assert.That(s.LedCount, Is.EqualTo(0));
            Assert.That(ShowcaseHud.Squad(s), Is.EqualTo(None));
        }

        [Test]
        public void AnOrderWaitsForTheNextTickThatIsFree()
        {
            ShowcaseStage stage = Stage("battle");
            SimSession s = stage.Start();
            ShowcaseActions actions = Actions(stage);
            Assert.That(s.FreeTick(1), Is.EqualTo(1L));
            Assert.That(s.FreeTick(7), Is.EqualTo(7L));
            // Ticks 1, 2 and 4 are taken: the first free tick from 1 on is 3, from 4 on it is 5.
            s.Enqueue(BridgeCommand.Reload(1, 1));
            s.Enqueue(BridgeCommand.Reload(2, 1));
            s.Enqueue(BridgeCommand.Reload(4, 1));
            Assert.That(s.FreeTick(1), Is.EqualTo(3L));
            Assert.That(s.FreeTick(2), Is.EqualTo(3L));
            Assert.That(s.FreeTick(3), Is.EqualTo(3L));
            Assert.That(s.FreeTick(4), Is.EqualTo(5L));
            Assert.That(s.FreeTick(5), Is.EqualTo(5L));
            Assert.That(() => s.FreeTick(0), Throws.TypeOf<ArgumentOutOfRangeException>(), "the tick the session stands on is not a future tick");

            Assert.That(actions.FollowOrHold(s), Is.True);
            Assert.That(s.FreeTick(1), Is.EqualTo(5L), "the order took tick 3");
            s.StepOnce();
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(0), "not yet");
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(1));
            Assert.That(Last(s).Kind, Is.EqualTo(BridgeCommandKind.Follow));
            Assert.That(Last(s).AtTick, Is.EqualTo(3L));
            s.StepOnce();
            Assert.That(() => s.FreeTick(s.Tick), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void APlayerWhoIsDownGivesNoOrders()
        {
            ShowcaseStage stage = Stage("battle");
            var world = new M6World(stage.Recipe(), stage.MapBytes());
            SimSession s = world.Session;
            ulong me = s.ControlledKey.Value;
            world.Sim.RegisterGameplay("test.death-3", new M6Damage(world, 3, me), SimSystemPhase.Queries);
            ShowcaseActions actions = Actions(stage);
            Assert.That(actions.FollowOrHold(s), Is.True);
            s.StepOnce();
            Assert.That(s.LedCount, Is.EqualTo(8));
            while (s.Current.TryGet(me, out ViewEntity unit) && unit.Alive && s.Tick < 10) s.StepOnce();
            Assert.That(s.Current.TryGet(me, out ViewEntity fallen) && !fallen.Alive, Is.True, "the player's unit fell");
            Assert.That(s.LedCount, Is.EqualTo(8), "on the tick of the fall the squad is still there");
            Assert.That(fallen.Band, Is.EqualTo(0UL), "but a fallen leader shows no band");
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(0), "the squad broke up with its leader");
            long free = s.FreeTick(s.Tick + 1);
            if (s.ControlledKey == me)
            {
                Assert.That(actions.FollowOrHold(s), Is.False);
                Assert.That(actions.GoTo(s, 26000, 61000), Is.False);
                Assert.That(actions.Dismiss(s), Is.False);
                Assert.That(ShowcaseHud.Squad(s), Is.EqualTo(""));
                Assert.That(s.FreeTick(s.Tick + 1), Is.EqualTo(free), "nothing was queued");
            }
            else Assert.Fail("the player's key changed on the tick after the fall; write the takeover case here");
        }

        [Test]
        public void AStageWithoutTheRulesRefusesTheOrdersAndKeepsItsOldSquadLine()
        {
            ShowcaseStage stage = Showcase.LegacyHouse;
            Assert.That(stage.LedSize(), Is.EqualTo(0));
            SimSession s = stage.Start();
            ShowcaseActions actions = Actions(stage);
            Assert.That(s.LedOrders, Is.False);
            Assert.That(actions.FollowOrHold(s), Is.False);
            Assert.That(actions.GoTo(s, 5000, 7000), Is.False);
            Assert.That(actions.Dismiss(s), Is.False);
            Assert.That(s.FreeTick(1), Is.EqualTo(1L));
            for (int t = 0; t < 60; t++)
            {
                Assert.That(ShowcaseHud.Squad(s), Is.EqualTo(ShowcaseHud.Squad(s.Current, s.ControlledKey)));
                s.StepOnce();
            }
            Assert.That(s.CommandLog.Count, Is.EqualTo(0));
            Assert.That(ShowcaseHud.Shows(stage), Does.Not.Contain("follow you as a squad"));
        }

        [Test]
        public void EveryStageTellsTheSquadKeysWithTheSizeItsLayerGives()
        {
            var sizes = new Dictionary<string, int>();
            foreach (ShowcaseStage stage in Showcase.Stages)
            {
                int size = stage.LedSize();
                sizes[stage.Id] = size;
                Assert.That(size, Is.GreaterThan(0), stage.Id);
                Assert.That(stage.Start().LedOrders, Is.True, stage.Id);
                Assert.That(stage.Recipe().Pack.Section<AiRules>().Led.Value.RecruitRadiusMm, Is.EqualTo(30000), stage.Id);
                Assert.That(ShowcaseHud.Shows(stage), Does.EndWith("- T: up to " + size
                    + " soldiers near you follow you as a squad (T again: they hold); C sends them where you aim; X dismisses them\n"), stage.Id);
            }
            Assert.That(sizes["battle"], Is.EqualTo(8));
            Assert.That(sizes["melee"], Is.EqualTo(12));
            Assert.That(ShowcaseHud.Keys, Does.Contain("SQUAD     T follow me or hold here, C go where you aim, X dismiss\n"));
        }
    }
}
