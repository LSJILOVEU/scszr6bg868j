using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp16FallbackTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DeathChoosesNearestFriend_TiesByStableId_ClearsPath_UsesSameTickInput(bool reverse)
        {
            var w = new M6World(Recipe(new[] {
                U(1), U(2, 8000), U(3, 12000), U(4, 20000),
                U(5, 10000, 11000, team: 2, squad: 2), U(6, reserve: true)
            }), reverse: reverse);
            ulong chosen = StableEntityKey.Placed(1, 2).Id.Value < StableEntityKey.Placed(1, 3).Id.Value ? 2UL : 3UL;
            KillAt(w, 1, 1);
            w.Step();
            Assert.That(w.Dead(1), Is.True); Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL));
            Assert.That(Switches(w), Is.Zero); Assert.That(w.Has<Position>(6), Is.False);
            // Give the successor a real AI-style path immediately before the takeover.
            M3TestWorld.Locomotion(w.Session).Order(w.Unit(chosen), SimVec3.Metres(30, 0, 20));
            Assert.That(w.Read<MoveIntent>(chosen).Active, Is.True);
            Assert.That(w.Read<MoveWaypoint>(chosen).Has, Is.True);
            SimVec3 before = Point(w, chosen);
            SimFix speed = w.Read<MoveIntent>(chosen).SpeedPerTick;
            w.Sim.Step(Input(0, 1));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(chosen));
            Assert.That(w.System<PlayerControl>().Current, Is.EqualTo(w.Unit(chosen)));
            Assert.That(Point(w, chosen), Is.EqualTo(before + new SimVec3(default, default, speed)));
            Idle(w, chosen); Assert.That(Switches(w), Is.EqualTo(1));
            var change = SwitchEvent(w);
            Assert.That(change.Source.Id, Is.EqualTo(StableEntityKey.Placed(1, 1).Id));
            Assert.That(change.Target.Id, Is.EqualTo(StableEntityKey.Placed(1, chosen).Id));
            w.Step(); Assert.That(Switches(w), Is.Zero);
        }

        // The result is not final before the match system judges, so on the end tick itself the fallback still takes over.
        [Test]
        public void FallbackStillSwitchesOnTheEndTickItself()
        {
            var w = new M6World(Recipe(new[] { U(1), U(2, 14000), U(3, 70000, 35000, team: 2) }));
            KillAt(w, 1, 1, 3);
            w.Step();
            Assert.That(w.Match.Winner, Is.Zero);
            w.Sim.Step(Input(0, 1));
            Assert.That(w.Match.Winner, Is.EqualTo(1)); Assert.That(w.Match.EndTick, Is.EqualTo(2));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(2UL)); Assert.That(Switches(w), Is.EqualTo(1));
        }

        [Test]
        public void NearestUsesHorizontalDistance_NotStableOrderOrFull3D()
        {
            ulong near = 2, far = 3;
            if (StableEntityKey.Placed(1, near).Id.Value < StableEntityKey.Placed(1, far).Id.Value)
            { near = 3; far = 2; }
            var w = new M6World(Recipe(new[] {
                U(1), U(near, 12000), U(far, 18000), U(4, 70000, 35000, team: 2)
            }));
            // Horizontal distance is 2 m, even though vertical separation is much larger than the other candidate.
            w.Set(near, new Position(SimVec3.Metres(12, 100, 10)));
            KillAt(w, 1, 1); w.Step(2);
            Assert.That(w.Session.ControlledKey, Is.EqualTo(near)); Assert.That(Switches(w), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NoCandidate_DeadInputIgnored_MatchStillEnds(bool killEnemy)
        {
            var w = new M6World(Recipe());
            KillAt(w, 1, killEnemy ? new ulong[] { 1, 2 } : new ulong[] { 1 });
            w.Step(); SimVec3 corpse = Point(w);
            // Tick 2 is the last tick: the dead input is ignored and the match ends on it.
            Assert.DoesNotThrow(() => w.Sim.Step(Input(1, 1)));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL));
            Assert.That(Point(w), Is.EqualTo(corpse)); Assert.That(Switches(w), Is.Zero);
            Assert.That(w.Match.Winner, Is.EqualTo(killEnemy ? -1 : 2));
            Assert.That(w.Match.EndTick, Is.EqualTo(2));
            Assert.That(w.Match.EndReason, Is.EqualTo(killEnemy ? MatchEndReason.Draw : MatchEndReason.Exhausted));
            // From the first step after the end Sim refuses, and nothing moves or switches.
            Assert.Throws<InvalidOperationException>(() => w.Sim.Step(Input(1, 1)));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL));
            Assert.That(Point(w), Is.EqualTo(corpse)); Assert.That(Switches(w), Is.Zero);
            Assert.That(w.Match.EndTick, Is.EqualTo(2));
        }

        // The original ten ticks of dead input with no candidate, kept in a world whose match never ends.
        [TestCase(false)]
        [TestCase(true)]
        public void NoCandidate_DeadInputIgnoredForTenTicks(bool killEnemy)
        {
            var w = new M6World(M6World.Endless(Recipe()));
            KillAt(w, 1, killEnemy ? new ulong[] { 1, 2 } : new ulong[] { 1 });
            w.Step(); SimVec3 corpse = Point(w);
            for (int tick = 0; tick < 10; tick++)
            {
                Assert.DoesNotThrow(() => w.Sim.Step(Input(1, 1)));
                Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL));
                Assert.That(Point(w), Is.EqualTo(corpse)); Assert.That(Switches(w), Is.Zero);
            }
            Assert.That(w.Match.Winner, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReserveCannotBeChosenUntilTickAfterRealDeployment(bool reverse)
        {
            var w = new M6World(Recipe(new[] {
                U(1), U(2, reserve: true), U(3, 70000, 35000, team: 2)
            }, match: Match(interval: 3)), reverse: reverse);
            var idsBefore = new[] { w.Sim.Entities.StableOf(w.Unit(1)), w.Sim.Entities.StableOf(w.Unit(2)), w.Sim.Entities.StableOf(w.Unit(3)) };
            KillAt(w, 1, 1); w.Step();
            w.Sim.Step(Input(1, 0));
            Assert.That(w.Has<Position>(2), Is.False); Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL));
            Assert.That(Switches(w), Is.Zero); Assert.That(w.Match.Winner, Is.Zero, "reserve still counts as living");
            w.Sim.Step(Input(1, 0)); // Fallback order 6 runs before Reinforce order 8.
            Assert.That(w.Has<Position>(2), Is.True); Assert.That(w.Reinforce.Successes(1), Is.EqualTo(1));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(1UL)); Assert.That(Switches(w), Is.Zero);
            SimVec3 deployed = Point(w, 2);
            w.Sim.Step(Input(1, 0));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(2UL));
            Assert.That(Point(w, 2).X.Raw, Is.GreaterThan(deployed.X.Raw));
            Idle(w, 2); Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(new[] { w.Sim.Entities.StableOf(w.Unit(1)), w.Sim.Entities.StableOf(w.Unit(2)), w.Sim.Entities.StableOf(w.Unit(3)) }, Is.EqualTo(idsBefore));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FinishedMatchPreventsFallback_AllPointsAndTimeoutRemainUnchanged(bool allPoints)
        {
            var pack = allPoints
                ? M6World.Pack(new[] { new ControlPointDef(1, M6World.P(50, 30), SimFix.FromInt(1), initialOwner: 1) })
                : Match(max: 1);
            var units = new[] { U(1), U(2, 14000), U(3, 70000, 35000, team: 2) };
            var baseRecipe = M6World.RecipeOf(units, pack);
            var player = new M6World(baseRecipe.WithPlayer(1)); var plain = new M6World(baseRecipe);
            KillAt(player, 1, 1); KillAt(plain, 1, 1);
            player.Step(); plain.Step();
            Assert.That(player.Match.Winner, Is.Not.Zero); Assert.That(player.Dead(1), Is.True);
            for (int tick = 0; tick < 5; tick++)
            {
                Assert.Throws<InvalidOperationException>(() => player.Sim.Step(Input(1, 0)));
                Assert.Throws<InvalidOperationException>(() => plain.Step());
                Assert.That(player.Session.ControlledKey, Is.EqualTo(1UL)); Assert.That(Switches(player), Is.Zero);
                Assert.That(player.Match.Winner, Is.EqualTo(plain.Match.Winner));
                Assert.That(player.Match.EndTick, Is.EqualTo(plain.Match.EndTick));
                Assert.That(player.Match.EndReason, Is.EqualTo(plain.Match.EndReason));
            }
        }

        [Test]
        public void SwitchingToCurrentTargetDoesNotEmitOrConsumeSequence()
        {
            var a = new M6World(Recipe()); var b = new M6World(Recipe(), reverse: true);
            a.Sim.RegisterGameplay("test.wp16-repeat", new RepeatSwitch(a), SimSystemPhase.Requests);
            b.Sim.RegisterGameplay("test.wp16-repeat", new RepeatSwitch(b), SimSystemPhase.Requests);
            a.Step(); b.Step();
            Assert.That(Switches(a), Is.Zero); Assert.That(Switches(b), Is.Zero);
            Assert.That(a.Sim.Events.NextSequence, Is.EqualTo(1)); SameEvents(a, b);
        }

        private sealed class RepeatSwitch : IRetainedGameplaySystem
        {
            private readonly PlayerControl control;
            internal RepeatSwitch(M6World w) { control = w.System<PlayerControl>(); }
            public int Order => 9;
            public string StateTypeId => "test.wp16-repeat";
            public int StateVersion => 1;
            public void Step(SimContext context) { control.Switch(context, control.Current); control.Switch(context, control.Current); }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
    }
}
