using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // C2: the respawn state in the frame and its HUD line. Worlds come from the C1 respawn fixtures (all literals) and are
    // advanced through the session, so Current is the frame a host would show. The player dies at tick K in Queries.
    public sealed class C2RespawnViewTests
    {
        private const ulong Reserve = 3;
        private static readonly SpawnAnchor HomeBase = new SpawnAnchor(1, P(5, 38));

        internal static M6World Respawning(int delay, long[] kills, params UnitSpawn[] reserves) =>
            World(Units(reserves), Pack(Points(Spawn(1, 30, 20)), Bases(HomeBase), delay), kills: kills);

        private static readonly HudText T = HudText.English;
        private static string In(long seconds) => string.Format(System.Globalization.CultureInfo.InvariantCulture, T.RespawnIn, seconds);
        private static string Now(ulong key) => string.Format(System.Globalization.CultureInfo.InvariantCulture, T.NowControlling, key);

        internal static ViewRespawn Step(M6World w) { w.Session.StepOnce(); return w.Session.Current.Respawn; }

        private static void Expect(ViewRespawn r, RespawnPhase state, long left, int reserves, string at)
        {
            Assert.That(r.State, Is.EqualTo(state), at);
            Assert.That(r.TicksLeft, Is.EqualTo(left), at);
            Assert.That(r.Reserves, Is.EqualTo(reserves), at);
        }

        // (2) D = 45, death at tick 1: Waiting with 46 - tick left on every tick, whole seconds rounded up, switch at tick 46.
        [Test]
        public void C2Countdown_EveryTick_ThenTakeoverOnTheReserve()
        {
            var w = Respawning(45, new long[] { 1 }, U(Reserve, 0, 0, reserve: true));
            var tracker = new PlayerTracker(w.Session.ControlledKey);
            Assert.That(w.Session.Current.Respawn.State, Is.EqualTo(RespawnPhase.None));
            for (long tick = 1; tick <= 45; tick++)
            {
                ViewRespawn r = Step(w);
                Assert.That(w.Session.Tick, Is.EqualTo(tick));
                Expect(r, RespawnPhase.Waiting, 46 - tick, 1, "tick " + tick);
                Assert.That(w.Session.ControlledKey, Is.EqualTo(Player), "tick " + tick);
                tracker.Update(w.Session.Current, w.Session.ControlledKey);
                long seconds = tick <= 15 ? 2 : 1;
                Assert.That(HudLines.Respawn(w.Session.Current), Is.EqualTo(In(seconds)), "tick " + tick);
                Assert.That(HudLines.Takeover(tracker.LostPlayer, tracker.Alive, w.Session.ControlledKey), Is.EqualTo(T.Down), "tick " + tick);
                Assert.That(HudLines.PlayerLine(w.Session.Current, tracker.LostPlayer, tracker.Alive, w.Session.ControlledKey),
                    Is.EqualTo(In(seconds)), "the countdown replaces Down, tick " + tick);
            }
            Expect(Step(w), RespawnPhase.None, 0, 0, "tick 46");
            Assert.That(w.Session.Tick, Is.EqualTo(46));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(Reserve));
            Assert.That(HudLines.Respawn(w.Session.Current), Is.Null);
            Assert.That(tracker.Update(w.Session.Current, w.Session.ControlledKey), Is.True);
            Assert.That(HudLines.Takeover(tracker.LostPlayer, tracker.Alive, w.Session.ControlledKey), Is.EqualTo(Now(3)));
            Assert.That(HudLines.PlayerLine(w.Session.Current, tracker.LostPlayer, tracker.Alive, w.Session.ControlledKey), Is.EqualTo(Now(3)));
        }

        // Another table changes every line without touching HudLines.
        [Test]
        public void C2AnotherTextTable_IsUsedForEveryLine()
        {
            var other = new HudText("R{0}", "W", "N", "D", "C{0}");
            FrameView Frame(RespawnPhase state, long left) =>
                new FrameView(0, Array.Empty<ViewEntity>(), respawn: new ViewRespawn(state, left, 1));
            Assert.That(HudLines.Respawn(Frame(RespawnPhase.Waiting, 31), other), Is.EqualTo("R2"));
            Assert.That(HudLines.Respawn(Frame(RespawnPhase.Overdue, 0), other), Is.EqualTo("W"));
            Assert.That(HudLines.Respawn(Frame(RespawnPhase.NoReserve, 0), other), Is.EqualTo("N"));
            Assert.That(HudLines.Takeover(true, false, 1, other), Is.EqualTo("D"));
            Assert.That(HudLines.Takeover(true, true, 7, other), Is.EqualTo("C7"));
            Assert.That(HudLines.PlayerLine(Frame(RespawnPhase.Waiting, 31), true, false, 1, other), Is.EqualTo("R2"));
            Assert.That(HudLines.PlayerLine(Frame(RespawnPhase.None, 0), true, false, 1, other), Is.EqualTo("D"));
            Assert.That(HudLines.PlayerLine(Frame(RespawnPhase.None, 0), false, true, 1, other), Is.Null);
            Assert.Throws<ArgumentNullException>(() => new HudText(null, "W", "N", "D", "C{0}"));
        }

        [TestCase(1L, 1L)]
        [TestCase(30L, 1L)]
        [TestCase(31L, 2L)]
        [TestCase(300L, 10L)]
        public void C2WaitingSeconds_RoundUp(long left, long seconds)
        {
            var frame = new FrameView(0, Array.Empty<ViewEntity>(), respawn: new ViewRespawn(RespawnPhase.Waiting, left, 1));
            Assert.That(HudLines.Respawn(frame), Is.EqualTo(In(seconds)));
        }

        // (3) D = 1, death at tick 3 and switch at tick 4 inside one Advance (66667 us at 30 Hz is exactly 2 steps).
        [Test]
        public void C2DeathAndTakeoverInOneFrame_AreSeenAsATakeover()
        {
            var w = Respawning(1, new long[] { 3 }, U(Reserve, 0, 0, reserve: true));
            var tracker = new PlayerTracker(w.Session.ControlledKey);
            w.Session.StepOnce(); w.Session.StepOnce();
            Assert.That(tracker.Update(w.Session.Current, w.Session.ControlledKey), Is.False);
            Assert.That(w.Session.Advance(66667), Is.EqualTo(2));
            Assert.That(w.Session.Current.Tick, Is.EqualTo(4));
            Assert.That(w.Session.ControlledKey, Is.EqualTo(Reserve));
            Assert.That(tracker.Update(w.Session.Current, w.Session.ControlledKey), Is.True);
            Assert.That(tracker.Alive, Is.True);
            Assert.That(HudLines.Takeover(tracker.LostPlayer, tracker.Alive, w.Session.ControlledKey), Is.EqualTo(Now(3)));

            var stepped = Respawning(1, new long[] { 3 }, U(Reserve, 0, 0, reserve: true));
            var each = new PlayerTracker(stepped.Session.ControlledKey);
            for (int i = 0; i < 4; i++) { stepped.Session.StepOnce(); each.Update(stepped.Session.Current, stepped.Session.ControlledKey); }
            Assert.That(each.LostPlayer, Is.EqualTo(tracker.LostPlayer));
            Assert.That(HudLines.Takeover(each.LostPlayer, each.Alive, stepped.Session.ControlledKey),
                Is.EqualTo(HudLines.Takeover(tracker.LostPlayer, tracker.Alive, w.Session.ControlledKey)));
        }

        // (4a) K = 1, D = 5, 1 x 1 grid, a living ally on the anchor until tick 8 (moved after respawn tried there): 6-8 Overdue, 9 switches.
        [Test]
        public void C2BlockedAnchor_IsOverdueUntilTheSwitch()
        {
            const ulong blocker = 4;
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(blocker, 30, 20)),
                Pack(Points(Spawn(1, 30, 20), Spawn(2, 50, 20)), Bases(HomeBase), 5, columns: 1, rows: 1), kills: new long[] { 1 });
            w.Sim.RegisterGameplay("test.respawn-move", new MoveAt(w, 8, blocker, P(30, 30)), WarCore.Sim.SimSystemPhase.Requests);
            for (long tick = 1; tick <= 5; tick++) Expect(Step(w), RespawnPhase.Waiting, 6 - tick, 1, "tick " + tick);
            for (long tick = 6; tick <= 8; tick++)
            {
                Expect(Step(w), RespawnPhase.Overdue, 0, 1, "tick " + tick);
                Assert.That(HudLines.Respawn(w.Session.Current), Is.EqualTo(T.RespawnWaiting));
                Assert.That(w.Session.ControlledKey, Is.EqualTo(Player));
            }
            Expect(Step(w), RespawnPhase.None, 0, 0, "tick 9");
            Assert.That(w.Session.ControlledKey, Is.EqualTo(Reserve));
        }

        // (4b) A reserve but no owned spawn point and no own base: Overdue from tick 6 on, never a switch.
        [Test]
        public void C2NoAnchor_StaysOverdue()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true)), Pack(Points(), Bases(), 5), kills: new long[] { 1 });
            for (long tick = 1; tick <= 5; tick++) Expect(Step(w), RespawnPhase.Waiting, 6 - tick, 1, "tick " + tick);
            for (long tick = 6; tick <= 66; tick++)
            {
                Expect(Step(w), RespawnPhase.Overdue, 0, 1, "tick " + tick);
                Assert.That(HudLines.Respawn(w.Session.Current), Is.EqualTo(T.RespawnWaiting));
            }
            Assert.That(w.Session.ControlledKey, Is.EqualTo(Player));
        }

        // (5) No reserve on the player's team: NoReserve for 300 ticks, the ticks left still count down to 0.
        [Test]
        public void C2NoReserve_ForThreeHundredTicks()
        {
            var w = Respawning(45, new long[] { 1 });
            for (long tick = 1; tick <= 300; tick++)
            {
                ViewRespawn r = Step(w);
                Assert.That(r.State, Is.EqualTo(RespawnPhase.NoReserve), "tick " + tick);
                Assert.That(r.Reserves, Is.Zero, "tick " + tick);
                Assert.That(HudLines.Respawn(w.Session.Current), Is.EqualTo(T.NoReserve), "tick " + tick);
                if (tick == 45) Assert.That(r.TicksLeft, Is.EqualTo(1));
                if (tick == 46 || tick == 300) Assert.That(r.TicksLeft, Is.Zero);
            }
            Assert.That(w.Session.ControlledKey, Is.EqualTo(Player));
        }

        // (5) The player on team 2: a team 1 reserve does not count.
        [Test]
        public void C2NoReserve_CountsOnlyThePlayersTeam()
        {
            var units = new System.Collections.Generic.List<UnitSpawn>
            {
                U(Player, 20, 20, team: 2), U(Ally, 20, 38, team: 2), U(Enemy, 78, 38, team: 1), U(Reserve, 0, 0, team: 1, reserve: true),
            };
            var pack = Pack(new[] { Spawn(1, 30, 20, owner: 2), new ControlPointDef(90, P(76, 4), WarCore.Sim.SimFix.FromInt(2), 1) },
                new[] { new SpawnAnchor(2, P(5, 38)), new SpawnAnchor(1, P(78, 20)) }, 45);
            var w = World(units, pack, kills: new long[] { 1 });
            for (long tick = 1; tick <= 60; tick++)
            {
                ViewRespawn r = Step(w);
                Assert.That(r.State, Is.EqualTo(RespawnPhase.NoReserve), "tick " + tick);
                Assert.That(r.Reserves, Is.Zero, "tick " + tick);
            }
            Assert.That(w.Session.Current.Match.ReserveOne, Is.EqualTo(1));
        }

        // (6) Two reserves, K = 1, D = 5, deaths at 1, 8, 15: switches at 6 and 13, then no reserve left.
        [Test]
        public void C2RepeatedRespawn_UntilThePoolIsEmpty()
        {
            var w = Respawning(5, new long[] { 1, 8, 15 }, U(3, 0, 0, reserve: true), U(4, 0, 0, reserve: true));
            for (long tick = 1; tick <= 100; tick++)
            {
                ViewRespawn r = Step(w);
                string at = "tick " + tick;
                if (tick <= 5) Expect(r, RespawnPhase.Waiting, 6 - tick, 2, at);
                else if (tick <= 7) Assert.That(r.State, Is.EqualTo(RespawnPhase.None), at);
                else if (tick <= 12) Expect(r, RespawnPhase.Waiting, 13 - tick, 1, at);
                else if (tick <= 14) Assert.That(r.State, Is.EqualTo(RespawnPhase.None), at);
                else Expect(r, RespawnPhase.NoReserve, Math.Max(0, 20 - tick), 0, at);
            }
        }

        // (6) Deployed and killed on the same tick: that frame is Waiting again, not None.
        [Test]
        public void C2KilledOnTheSwitchTick_IsWaitingAgain()
        {
            var w = Respawning(5, new long[] { 1, 6 }, U(3, 0, 0, reserve: true), U(4, 0, 0, reserve: true));
            for (int i = 0; i < 5; i++) Step(w);
            ViewRespawn r = Step(w);
            Assert.That(w.Session.Tick, Is.EqualTo(6));
            Assert.That(w.Session.ControlledKey, Is.Not.EqualTo(Player));
            Expect(r, RespawnPhase.Waiting, 5, 1, "tick 6");
        }

        // (7) The match ends on time at tick 3, before the delay is met: the frame stays Waiting, the HUD line goes away.
        [Test]
        public void C2MatchOver_HidesTheLine()
        {
            var w = World(Units(U(Reserve, 0, 0, reserve: true)), Pack(Points(Spawn(1, 30, 20)), Bases(HomeBase), 5, max: 3), kills: new long[] { 1 });
            Step(w);
            Step(w);
            Assert.That(HudLines.Respawn(w.Session.Current), Is.EqualTo(In(1)));
            ViewRespawn r = Step(w);
            Assert.That(w.Session.Current.Tick, Is.EqualTo(3));
            Assert.That(w.Session.Current.Match.IsFinished, Is.True);
            Expect(r, RespawnPhase.Waiting, 3, 1, "tick 3");
            Assert.That(HudLines.Respawn(w.Session.Current), Is.Null);
        }

        // (8) Interpolation carries the respawn state; a hand-built frame has none.
        [Test]
        public void C2Interpolate_KeepsTheRespawnState()
        {
            var w = Respawning(45, new long[] { 1 }, U(Reserve, 0, 0, reserve: true));
            for (int i = 0; i < 10; i++) w.Session.StepOnce();
            FrameView mid = FrameView.Interpolate(w.Session.Previous, w.Session.Current, 0.5f);
            Expect(mid.Respawn, RespawnPhase.Waiting, 36, 1, "interpolated");
            Assert.That(mid.Respawn, Is.EqualTo(w.Session.Current.Respawn));
            Assert.That(new FrameView(0, Array.Empty<ViewEntity>()).Respawn.State, Is.EqualTo(RespawnPhase.None));
            Assert.That(HudLines.Respawn(new FrameView(0, Array.Empty<ViewEntity>())), Is.Null);
        }

        // (10) Saved at tick 10, restored forward and reversed: same respawn state on every tick, switch at 46 on both.
        [TestCase(false)]
        [TestCase(true)]
        public void C2Restore_GivesTheSameRespawnState(bool reverse)
        {
            long[] kills = { 1 };
            var original = Respawning(45, kills, U(Reserve, 0, 0, reserve: true));
            for (int i = 0; i < 10; i++) original.Session.StepOnce();
            byte[] saved = original.Sim.SaveRetainedSnapshot();
            var restored = World(Units(U(Reserve, 0, 0, reserve: true)), Pack(Points(Spawn(1, 30, 20)), Bases(HomeBase), 45), reverse, kills);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: reverse);
            restored.Session.RefreshView();
            Expect(restored.Session.Current.Respawn, RespawnPhase.Waiting, 36, 1, "restored");
            Assert.That(restored.Session.Current.Respawn, Is.EqualTo(original.Session.Current.Respawn));
            for (long tick = 11; tick <= 50; tick++)
            {
                original.Session.StepOnce(); restored.Session.StepOnce();
                Assert.That(restored.Session.Current.Respawn, Is.EqualTo(original.Session.Current.Respawn), "tick " + tick);
                Assert.That(restored.Session.ControlledKey, Is.EqualTo(original.Session.ControlledKey), "tick " + tick);
            }
            Assert.That(original.Session.ControlledKey, Is.EqualTo(Reserve));
        }
    }
}
