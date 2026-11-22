using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D6 (the forced downed stance) and D7 (what a casualty may do, and the crawl speed through the speed line).
    public sealed class Down1MoveTests
    {
        // 6031 mm/s: 13174 raw per tick, a speed whose crawl differs between one division and two (see D7 below).
        private const long Speed = 6031;

        private static UnitSpawn Idle(ulong key, long xMm, int team = 2) => Soldier(key, (int)key, xMm, 8000, team, -1, Speed);

        private static Down1Session Pair(Func<Down1Session> make = null) =>
            make != null ? make() : new Down1Session(Down1Session.DownPack(), new[] { Idle(1, 5000), Idle(2, 15000) });

        // ---- D6 ----

        // Downed on tick 5 (Queries): still standing at the end of that step; prone after the next step's Requests (system 0),
        // forced at once, with no transition event.
        [Test]
        public void D6_ProneFromTheNextRequests()
        {
            Down1Session w = Pair();
            int seen = -1;
            w.DownAt(5, 1);
            w.At(6, SimSystemPhase.Requests, 1, c => seen = w.S.Stances.StanceOf(w.Unit(1)).Level);
            w.StepTo(5);
            Assert.That(w.Downed(1), Is.True);
            Assert.That(w.S.StanceOf(1), Is.EqualTo(new StanceView(0, -1, 0)));
            w.Step();
            Assert.That(seen, Is.EqualTo(2));
            Assert.That(w.S.StanceOf(1), Is.EqualTo(new StanceView(2, -1, 0)));
            Assert.That(w.Events(StanceSystem.TransitionEventId), Is.EqualTo(0));
            Assert.That(w.S.StanceOf(2), Is.EqualTo(new StanceView(0, -1, 0)));
        }

        // A world without stances downs a unit without error and has no pose system (order 0 is free).
        [Test]
        public void D6_NoStancesNoError()
        {
            var w = new Down1Session(Down1Session.NoStancePack(), new[] { Idle(1, 5000), Idle(2, 15000) });
            w.DownAt(5, 1);
            Assert.DoesNotThrow(() => w.At(99, SimSystemPhase.Requests, 0, c => { }));
            Assert.DoesNotThrow(() => w.StepTo(10));
            Assert.That(w.Downed(1), Is.True);
            Assert.That(w.S.Stances, Is.Null);
        }

        // Prone and changing to crouch (asked before the first step, due on tick 16) when downed on tick 5: the change is dropped
        // on tick 6, the unit stays prone and no transition event is ever published.
        [Test]
        public void D6_AChangeUnderWayIsDropped()
        {
            Down1Session w = Pair();
            Force(w.S, 1, 2);
            Assert.That(w.S.TryStance(1, 1), Is.True);
            Assert.That(w.S.StanceOf(1), Is.EqualTo(new StanceView(2, 1, 16)));
            w.DownAt(5, 1);
            int events = 0;
            while (w.Tick < 20)
            {
                w.Step();
                events += w.Events(StanceSystem.TransitionEventId);
                if (w.Tick >= 6) Assert.That(w.S.StanceOf(1), Is.EqualTo(new StanceView(2, -1, 0)), "tick " + w.Tick);
            }
            Assert.That(events, Is.EqualTo(0));
        }

        // The same change downed on tick 15: it lands on tick 16 (its event is published), and the step ends prone.
        [Test]
        public void D6_AChangeDueOnTheNextStepLandsThenIsUndone()
        {
            Down1Session w = Pair();
            Force(w.S, 1, 2);
            Assert.That(w.S.TryStance(1, 1), Is.True);
            w.DownAt(15, 1);
            w.StepTo(15);
            Assert.That(w.S.StanceOf(1), Is.EqualTo(new StanceView(2, 1, 16)));
            w.Step();
            Assert.That(w.Events(StanceSystem.TransitionEventId), Is.EqualTo(1));
            Assert.That(w.S.StanceOf(1), Is.EqualTo(new StanceView(2, -1, 0)));
        }

        // ---- D7 ----

        [Test]
        public void D7_ACasualtyMayOnlyMove()
        {
            Down1Session w = Pair();
            w.DownAt(1, 1);
            w.StepTo(2);
            Assert.That(w.S.Gate.Count, Is.EqualTo(2), "the stance row and the casualty row");
            Assert.That(w.S.Gate.Denied(w.Unit(1)), Is.EqualTo(Down1StunTests.All & ~UnitAction.Move));
            Assert.That(w.S.Gate.Denied(w.Unit(2)), Is.EqualTo(UnitAction.None));
            Assert.That(w.S.TryStance(1, 1), Is.False);
            Assert.That(w.S.TryStance(2, 1), Is.True);
        }

        // Without stances the gate has the casualty row alone.
        [Test]
        public void D7_WithoutStancesTheGateHasTheCasualtyRow()
        {
            var w = new Down1Session(Down1Session.NoStancePack(), new[] { Idle(1, 5000), Idle(2, 15000) });
            w.DownAt(1, 1);
            w.StepTo(2);
            Assert.That(w.S.Gate.Count, Is.EqualTo(1));
            Assert.That(w.S.Gate.Denied(w.Unit(1)), Is.EqualTo(Down1StunTests.All & ~UnitAction.Move));
            Assert.That(M3TestWorld.Locomotion(w.S).Motion, Is.InstanceOf<SpeedLine>());
        }

        // A manual reload (a pack with combat.manualReload): refused for a casualty that has fired, taken for a living unit.
        [Test]
        public void D7_ACasualtyDoesNotReload()
        {
            var pack = new CombatPack(new[] { Down1World.Gun() }, new ScriptedDamage { Fallback = 0 }, new NearestVisibleEnemy(), 100,
                body: Ml3World.Rwr, manualReload: true, downed: Down1World.Rules());
            var w = new Down1World(pack, new[] { Down1World.U(0, 0, 1), Down1World.U(2 * M, 0, 2), Down1World.U(0, 4 * M, 1) });
            w.Shoot(1, 0, 1);
            w.Shoot(1, 2, 1);
            w.DownAt(2, 0);
            w.StepTo(2);
            Assert.That(w.Downed(0), Is.True);
            Assert.That(w.Combat.TryReload(w.Entity(0)), Is.False);
            Assert.That(w.Combat.TryReload(w.Entity(2)), Is.True);
        }

        // A downed player does not dash (a world without stances, so only the casualty row denies it); the step is the crawl. The
        // same player not downed dashes.
        [TestCase(true)]
        [TestCase(false)]
        public void D7_ADownedPlayerDoesNotDash(bool downed)
        {
            var w = new Down1Session(Down1Session.NoStancePack(), new[] { Soldier(1, 1, 5000, 8000, 1, -1, Speed).WithDash(4000, 6, 90) }, open: Player(1));
            if (downed) w.DownAt(1, 1);
            w.StepTo(2);
            DashState before = M3TestWorld.Component<DashState>(w.S, 1);
            w.S.SetPlayerInput(w.Tick + 1, new PlayerInput(0, 1000, dash: true));
            List<long> steps = w.Steps(1, 1);
            Assert.That(M3TestWorld.Component<DashState>(w.S, 1).Equals(before), Is.EqualTo(downed));
            if (downed) Assert.That(steps[0], Is.EqualTo(StatLine.Scale(Own(w), new[] { 150 })));
        }

        private static long Own(Down1Session w) => M3TestWorld.Component<MoveIntent>(w.S, 1).SpeedPerTick.Raw;
        private static long Crawl(long own) => StatLine.Scale(own, new[] { 250, 150 });

        // One unit (key 1, squad 1) at (5, 8) m ordered along +x on tick order; downed on tick 1. Straight mover: each step's x
        // is the step length.
        private static Down1Session Walker(long order = 1, CombatPack pack = null, Func<WorldRecipe, WorldRecipe> open = null)
        {
            var w = new Down1Session(pack ?? Down1Session.DownPack(), new[] { Soldier(1, 1, 5000, 8000, 2, -1, Speed) },
                new[] { BridgeCommand.MoveTo(order, 1, 35000, 8000) }, open, MoverKind.Interim);
            w.DownAt(1, 1);
            return w;
        }

        // From tick 2 the downed unit steps Scale(S, [prone 250, crawl 150]), one division; two divisions would give one less.
        [Test]
        public void D7_TheOrderedCasualtyCrawls()
        {
            Down1Session w = Walker();
            w.Step();
            long own = Own(w);
            Assert.That(own, Is.EqualTo(13174L));
            Assert.That(Crawl(own), Is.Not.EqualTo(StatLine.Scale(StatLine.Scale(own, new[] { 250 }), new[] { 150 })));
            Assert.That(w.Steps(1, 10), Is.All.EqualTo(Crawl(own)));
        }

        // An order given to the casualty's squad after it went down is taken; it crawls.
        [Test]
        public void D7_AnOrderToACasualtySquadIsTaken()
        {
            Down1Session w = Walker(3);
            w.StepTo(3);
            Assert.That(M3TestWorld.Component<MoveIntent>(w.S, 1).Active, Is.True);
            Assert.That(w.Steps(1, 10), Is.All.EqualTo(Crawl(Own(w))));
        }

        [Test]
        public void D7_ADownedPlayerCrawls()
        {
            var w = new Down1Session(Down1Session.DownPack(), new[] { Soldier(1, 1, 5000, 8000, 1, -1, Speed) }, open: Player(1));
            w.DownAt(1, 1);
            w.StepTo(1);
            w.S.SetPlayerInput(w.Tick + 1, new PlayerInput(1000, 0));
            Assert.That(w.Steps(1, 10), Is.All.EqualTo(Crawl(Own(w))));
        }

        // Without stances: Scale(S, [150]).
        [Test]
        public void D7_WithoutStancesTheCrawlAlone()
        {
            Down1Session w = Walker(pack: Down1Session.NoStancePack());
            w.Step();
            Assert.That(w.Steps(1, 10), Is.All.EqualTo(StatLine.Scale(Own(w), new[] { 150 })));
        }

        // Inertia (acceleration 137 000 mm/s^2, no braking), ordered on tick 3 after going down: the first step from rest is the
        // crawl speed through inertia (min(crawl, accel)), not the inertia step then scaled.
        [Test]
        public void D7_TheCrawlActsBeforeInertia()
        {
            Down1Session w = Walker(3, open: r => r.WithMotion().With(new InertiaOption(137000, 0)));
            long accel = M3TestWorld.Component<MotionLimits>(w.S, 1).Accel.Raw;
            List<long> steps = w.Steps(1, 6);
            long first = steps.Find(d => d != 0);
            long own = Own(w);
            Assert.That(first, Is.EqualTo(Math.Min(Crawl(own), accel)));
            Assert.That(first, Is.Not.EqualTo(Crawl(Math.Min(own, accel))));
        }
    }
}
