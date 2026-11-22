using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // SHOW-2: the sixth showcase stage (the Swiss valley hamlet, Showcase.Hamlet.cs) and the player's orders (ShowcaseActions):
    // the real grenade and the team's stance order. The stage is played once with its script and its orders to tick 600.
    public sealed class Show2Tests
    {
        private const int Ticks = 600;
        private const string Id = "hamlet";

        private sealed class Run
        {
            public long FirstBand = -1, FirstPinned = -1, Fires, BlastTick = -1;
            public ViewBlast Blast;
            public int Team2NearBefore, Team2NearAfter;
            public readonly Dictionary<long, List<(ulong Key, int Team, int Stance, bool Alive)>> At = new Dictionary<long, List<(ulong, int, int, bool)>>();
            public ulong Player;
            public bool HeightField, Plates;
            public int Walls, Width, Depth;
            public UnitStatus Status;
            // The first tick the player's unit is down, and whether a throw then was refused with nothing queued.
            public long DownAt = -1;
            public bool DownThrowRefused;
            public int DownThrown = -1;
        }

        private static Run cached;

        private static ShowcaseStage Stage(string id)
        {
            foreach (ShowcaseStage stage in Showcase.Stages)
                if (stage.Id == id) return stage;
            throw new ArgumentException(id);
        }

        private static int Team2Within(FrameView frame, float x, float z, float radius)
        {
            int n = 0;
            foreach (ViewEntity e in frame.Entities)
                if (e.Alive && e.Team == 2 && (e.X - x) * (e.X - x) + (e.Z - z) * (e.Z - z) <= radius * radius) n++;
            return n;
        }

        private static Run Play()
        {
            if (cached != null) return cached;
            ShowcaseStage stage = Stage(Id);
            SimSession s = stage.Start();
            var run = new Run
            {
                Player = s.ControlledKey ?? 0, HeightField = s.Map.HasHeightField, Plates = s.Map.Plates != null, Walls = s.Map.Walls.Length,
                Width = s.Map.Width, Depth = s.Map.Depth,
            };
            Assert.That(s.TryUnitStatus(run.Player, out run.Status), Is.True);
            foreach (var item in stage.Script()) s.SetPlayerInput(item.AtTick, item.Input);
            foreach (BridgeCommand command in stage.Commands()) s.Enqueue(command);
            var actions = new ShowcaseActions(stage, stage.StanceScales().Length);
            for (int t = 1; t <= Ticks && !s.IsFinished; t++)
            {
                if (run.DownAt < 0 && s.Current.TryGet(run.Player, out ViewEntity me) && !me.Alive)
                {
                    run.DownAt = t;
                    run.DownThrowRefused = !actions.TryThrow(s, 105000, 80000, out _, out _);
                    run.DownThrown = actions.Thrown;
                }
                if (t == 360) run.Team2NearBefore = Team2Within(s.Current, 105f, 89f, 6f);
                s.StepOnce();
                run.Fires += s.Current.Fires.Length;
                foreach (ViewEntity e in s.Current.Entities)
                {
                    if (e.Band != 0 && run.FirstBand < 0) run.FirstBand = t;
                    if (e.Nerve == 1 && run.FirstPinned < 0) run.FirstPinned = t;
                }
                if (s.Current.Blasts.Length > 0 && run.BlastTick < 0)
                {
                    run.BlastTick = t;
                    run.Blast = s.Current.Blasts[0];
                    run.Team2NearAfter = Team2Within(s.Current, 105f, 89f, 6f);
                }
                if (t == 179 || t == 200 || t == 419 || t == 450)
                {
                    var units = new List<(ulong, int, int, bool)>();
                    foreach (ViewEntity e in s.Current.Entities) units.Add((e.Key, e.Team, e.Stance, e.Alive));
                    run.At[t] = units;
                }
            }
            return cached = run;
        }

        [Test]
        public void TheHamletIsTheCommittedRealTerrainMapWithoutFloors()
        {
            Run run = Play();
            Assert.That(run.HeightField, Is.True);
            Assert.That(run.Plates, Is.False);
            Assert.That(new[] { run.Width, run.Depth, run.Walls }, Is.EqualTo(new[] { 160, 160, 62 }));
            Assert.That(Stage(Id).Title, Is.EqualTo("Swiss valley hamlet"));
        }

        [Test]
        public void BothSidesFightWithSquadsAndPressure()
        {
            Run run = Play();
            Assert.That(run.FirstBand, Is.InRange(1, 60), "squads form in the first two seconds");
            Assert.That(run.FirstPinned, Is.GreaterThan(0), "somebody was pinned");
            Assert.That(run.Fires, Is.GreaterThan(100), "rifles fire");
        }

        [Test]
        public void ThePlayerHasARifleADashAndABlock()
        {
            Run run = Play();
            Assert.That(run.Player, Is.EqualTo(1UL));
            Assert.That(run.Status.HasWeapon, Is.True);
            Assert.That(run.Status.HasDash, Is.True);
            Assert.That(run.Status.Health, Is.EqualTo(100f));
        }

        [Test]
        public void TheScriptedGrenadeBurstsAmongTeamTwoAndHurtsThem()
        {
            Run run = Play();
            Assert.That(run.BlastTick, Is.EqualTo(360));
            Assert.That(new[] { run.Blast.X, run.Blast.Z, run.Blast.Radius }, Is.EqualTo(new[] { 105f, 89f, 6f }).Within(0.001f));
            Assert.That(run.Team2NearBefore, Is.GreaterThan(0), "team 2 is there when it bursts");
            Assert.That(run.Team2NearAfter, Is.LessThan(run.Team2NearBefore), "somebody of team 2 fell to it");
        }

        [Test]
        public void TheStanceOrdersCrouchTeamOneButNotThePlayerAndStandItUpAgain()
        {
            Run run = Play();
            // Reserves that come onto the field after an order were not there to hear it: only the units alive at `before` count.
            int Count(long tick, int team, int stance, bool player, long before = -1)
            {
                var there = new HashSet<ulong>();
                foreach (var u in run.At[before < 0 ? tick : before])
                    if (u.Alive) there.Add(u.Key);
                int n = 0;
                foreach (var u in run.At[tick])
                    if (u.Alive && there.Contains(u.Key) && u.Team == team && u.Stance == stance && (u.Key == run.Player) == player) n++;
                return n;
            }
            int teamOne = Count(179, 1, 0, false);
            Assert.That(teamOne, Is.GreaterThan(30), "standing before the order at tick 180");
            Assert.That(Count(200, 1, 1, false, 179), Is.GreaterThan(30), "crouching after it");
            Assert.That(Count(200, 1, 0, false, 179), Is.EqualTo(0), "nobody of team 1 who was there is left standing");
            Assert.That(Count(200, 1, 0, true, 179), Is.EqualTo(1), "the player is not ordered");
            Assert.That(Count(200, 2, 0, false, 179), Is.EqualTo(Count(179, 2, 0, false)), "team 2 is not ordered");
            Assert.That(Count(200, 2, 1, false), Is.EqualTo(0));
            Assert.That(Count(419, 1, 1, false), Is.GreaterThan(30));
            Assert.That(Count(450, 1, 0, false, 419), Is.GreaterThan(30), "standing again after the order at tick 420");
            Assert.That(Count(450, 1, 1, false, 419), Is.EqualTo(0));
        }

        [Test]
        public void ThePlayerFallsInTheScriptAndCannotThrowWhileDown()
        {
            Run run = Play();
            Assert.That(run.DownAt, Is.InRange(361, Ticks), "after the scripted grenade, before the end of the run");
            Assert.That(run.DownThrowRefused, Is.True);
            Assert.That(run.DownThrown, Is.EqualTo(0));
        }

        [Test]
        public void StanceScalesAreTheStanceHeightsOverTheBody()
        {
            Assert.That(Stage(Id).StanceScales(), Is.EqualTo(new[] { 1f, 1.0f / 1.75f, 0.5f / 1.75f }).Within(1e-5f));
            Assert.That(Stage("battle").StanceScales(), Is.EqualTo(new[] { 1f }));
        }

        [Test]
        public void OnlyTheHamletHasRealGrenadesAndStanceOrders()
        {
            foreach (ShowcaseStage stage in Showcase.Stages)
            {
                bool hamlet = stage.Id == Id;
                Assert.That(stage.GrenadeBlast, hamlet ? Is.EqualTo("rwr.frag") : Is.Null, stage.Id);
                Assert.That(stage.OrderSquad, Is.EqualTo(hamlet ? 1 : 0), stage.Id);
                Assert.That(stage.Commands().Length, Is.EqualTo(hamlet ? 3 : 0), stage.Id);
            }
        }

        // A hamlet session stepped to tick 60 with the player standing still.
        private static SimSession Hamlet(int ticks = 60)
        {
            SimSession s = Stage(Id).Start();
            for (int t = 0; t < ticks; t++) s.StepOnce();
            return s;
        }

        [Test]
        public void AGrenadeIsCutToTheReachBurstsAfterTheFuseAndCoolsDown()
        {
            SimSession s = Hamlet();
            var actions = new ShowcaseActions(Stage(Id), 3);
            Assert.That(s.Current.TryGet(1, out ViewEntity me), Is.True);
            long fromX = (long)Math.Round(me.X * 1000.0), fromZ = (long)Math.Round(me.Z * 1000.0);
            Assert.That(actions.Line(s.Tick), Is.EqualTo("GRENADE ready (G)  TEAM standing (C)\n"));
            Assert.That(actions.TryThrow(s, fromX, fromZ + 100000, out long x, out long z), Is.True);
            Assert.That(new[] { x, z }, Is.EqualTo(new[] { fromX, fromZ + ShowcaseActions.ReachMm }), "cut to 28 m straight ahead");
            Assert.That(actions.Thrown, Is.EqualTo(1));
            long thrownAt = s.Tick;
            Assert.That(actions.WaitTicks(thrownAt), Is.EqualTo(ShowcaseActions.CooldownTicks));
            Assert.That(actions.Line(thrownAt), Is.EqualTo("GRENADE 3.0 s (G)  TEAM standing (C)\n"));
            Assert.That(actions.TryThrow(s, fromX, fromZ + 5000, out _, out _), Is.False, "cooling down");
            Assert.That(actions.Thrown, Is.EqualTo(1));
            long burst = -1;
            ViewBlast seen = default;
            for (int t = 0; t < 40; t++)
            {
                s.StepOnce();
                if (s.Current.Blasts.Length > 0 && burst < 0) { burst = s.Tick; seen = s.Current.Blasts[0]; }
            }
            Assert.That(burst, Is.EqualTo(thrownAt + ShowcaseActions.FuseTicks));
            Assert.That(new[] { seen.X, seen.Z }, Is.EqualTo(new[] { x / 1000f, z / 1000f }).Within(0.001f));
            for (long t = s.Tick; t < thrownAt + ShowcaseActions.CooldownTicks - 1; t++) s.StepOnce();
            Assert.That(actions.WaitTicks(s.Tick), Is.EqualTo(1));
            Assert.That(actions.TryThrow(s, fromX, fromZ + 5000, out _, out _), Is.False);
            s.StepOnce();
            Assert.That(actions.WaitTicks(s.Tick), Is.EqualTo(0));
            Assert.That(actions.Line(s.Tick), Does.StartWith("GRENADE ready (G)"));
            Assert.That(actions.TryThrow(s, fromX + 3000, fromZ + 4000, out x, out z), Is.True, "ready again after the cooldown");
            Assert.That(new[] { x, z }, Is.EqualTo(new[] { fromX + 3000, fromZ + 4000 }), "a throw within reach lands where it was aimed");
            Assert.That(actions.Thrown, Is.EqualTo(2));
        }

        [Test]
        public void AThrowTheSessionRefusesQueuesNothingAndKeepsTheGrenadeReady()
        {
            SimSession s = Hamlet();
            var actions = new ShowcaseActions(Stage(Id), 3);
            Assert.That(s.Current.TryGet(1, out ViewEntity me), Is.True);
            long fromX = (long)Math.Round(me.X * 1000.0);
            Assert.That(me.Z, Is.LessThan(20f), "the player is still near the south edge");
            Assert.That(actions.TryThrow(s, fromX, -5000, out long x, out long z), Is.False, "5 m off the map's south edge, within reach");
            Assert.That(new[] { x, z }, Is.EqualTo(new[] { fromX, -5000L }));
            Assert.That(actions.Thrown, Is.EqualTo(0));
            Assert.That(actions.WaitTicks(s.Tick), Is.EqualTo(0));
            for (int t = 0; t < 40; t++)
            {
                s.StepOnce();
                Assert.That(s.Current.Blasts.Length, Is.EqualTo(0));
            }
            Assert.That(() => actions.TryThrow(null, 0, 0, out _, out _), Throws.ArgumentNullException);
        }

        [Test]
        public void StagesWithoutOrdersRefuseThemAndQueueNothing()
        {
            SimSession s = Stage("battle").Start();
            for (int t = 0; t < 10; t++) s.StepOnce();
            var actions = new ShowcaseActions(Stage("battle"), Stage("battle").StanceScales().Length);
            Assert.That(actions.RealGrenades, Is.False);
            Assert.That(actions.StanceOrders, Is.False);
            Assert.That(actions.TryThrow(s, 100000, 70000, out _, out _), Is.False);
            Assert.That(actions.NextStance(s), Is.False);
            Assert.That(actions.Stance, Is.EqualTo(0));
            Assert.That(actions.Line(s.Tick), Is.EqualTo(""));
            Assert.That(s.CommandLog.Count, Is.EqualTo(0));
            // A stage without an order squad takes no stance orders even with stance levels to give.
            var noSquad = new ShowcaseActions(Stage("battle"), 3);
            Assert.That(noSquad.StanceOrders, Is.False);
            Assert.That(noSquad.NextStance(s), Is.False);
            Assert.That(s.CommandLog.Count, Is.EqualTo(0));
            // The hamlet's stage on a pack read as having no stances takes no stance orders either.
            var flat = new ShowcaseActions(Stage(Id), 1);
            Assert.That(flat.StanceOrders, Is.False);
            Assert.That(flat.RealGrenades, Is.True);
            Assert.That(flat.Line(0), Is.EqualTo("GRENADE ready (G)\n"));
            Assert.That(() => new ShowcaseActions(null, 3), Throws.ArgumentNullException);
            Assert.That(() => new ShowcaseActions(Stage(Id), 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TheStanceOrderStepsDownAndBackToStanding()
        {
            SimSession s = Hamlet();
            var actions = new ShowcaseActions(Stage(Id), 3);
            int[] seen = new int[4];
            for (int k = 0; k < 4; k++)
            {
                var there = new HashSet<ulong>();
                foreach (ViewEntity e in s.Current.Entities)
                    if (e.Alive) there.Add(e.Key);
                Assert.That(actions.NextStance(s), Is.True);
                seen[k] = actions.Stance;
                for (int t = 0; t < 30; t++) s.StepOnce();
                int ordered = 0, other = 0;
                foreach (ViewEntity e in s.Current.Entities)
                {
                    // Reserves that came on after the order were not there to hear it.
                    if (!e.Alive || e.Team != 1 || e.Key == 1 || !there.Contains(e.Key)) continue;
                    if (e.Stance == actions.Stance) ordered++; else other++;
                }
                Assert.That(ordered, Is.GreaterThan(30), "order " + k);
                Assert.That(other, Is.EqualTo(0), "order " + k);
                Assert.That(actions.Line(s.Tick), Does.EndWith("TEAM " + ShowcaseActions.StanceName(actions.Stance) + " (C)\n"));
            }
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 0, 1 }));
            Assert.That(new[] { ShowcaseActions.StanceName(0), ShowcaseActions.StanceName(1), ShowcaseActions.StanceName(2), ShowcaseActions.StanceName(3) },
                Is.EqualTo(new[] { "standing", "crouching", "prone", "stance 3" }));
            Assert.That(() => actions.NextStance(null), Throws.ArgumentNullException);
        }
    }
}
