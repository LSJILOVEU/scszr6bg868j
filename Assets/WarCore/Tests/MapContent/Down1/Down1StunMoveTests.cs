using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.DashT;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D8 (the movement half: a stunned unit is held in place) and D9 (the player: no fire while a casualty, the
    // fallback picks a stunned squad mate but not a downed one).
    public sealed class Down1StunMoveTests
    {
        // Unit 1 (squad 1) at (5, 8) m ordered on tick 1 to (35, 8) m; stunned 60 on tick 10 (Queries, after the Movement phase).
        [TestCase(MoverKind.InterimCrowd)]
        [TestCase(MoverKind.Interim)]
        public void D8_AStunnedUnitStandsStillThenWalksOn(MoverKind mover)
        {
            var w = new Down1Session(Down1Session.DownPack(), new[] { Soldier(1, 1, 5000, 8000, 2, -1) }, new[] { BridgeCommand.MoveTo(1, 1, 35000, 8000) },
                mover: mover);
            w.StunAt(10, 1, 60);
            List<long> steps = w.Steps(1, 80);
            long own = M3TestWorld.Component<MoveIntent>(w.S, 1).SpeedPerTick.Raw;
            for (int tick = 2; tick <= 80; tick++)
            {
                long d = steps[tick - 1];
                if (tick >= 11 && tick <= 69) Assert.That(d, Is.EqualTo(0L), "tick " + tick);
                else Assert.That(d, Is.InRange(own - 1, own + 1), "tick " + tick);
            }
            Assert.That(w.Stunned(1), Is.False);
            Assert.That(M3TestWorld.Component<MoveIntent>(w.S, 1).Active, Is.True);
        }

        // A player stunned 60 on tick 10. Between the steps of ticks 69 and 70 the stun still holds (the step just run decides):
        // TryStance is refused and the dash key is ignored. Inside the step of tick 70 (Requests) the unit may change stance.
        [Test]
        public void D8_TheWakeTickIsTheFirstFreeStep()
        {
            var w = new Down1Session(Down1Session.DownPack(), new[] { Soldier(1, 1, 5000, 8000, 1, -1).WithDash(4000, 6, 90) }, open: Player(1));
            w.StunAt(10, 1, 60);
            bool inStep = false;
            w.At(70, SimSystemPhase.Requests, 1, c => inStep = w.S.Stances.TryStance(c, w.Unit(1), 1));
            w.StepTo(69);
            Assert.That(w.Stunned(1), Is.True);
            Assert.That(w.S.TryStance(1, 1), Is.False);
            DashState before = M3TestWorld.Component<DashState>(w.S, 1);
            w.S.SetPlayerInput(70, new PlayerInput(0, 1000, dash: true));
            w.Step();
            Assert.That(M3TestWorld.Component<DashState>(w.S, 1), Is.EqualTo(before));
            Assert.That(inStep, Is.True);
            Assert.That(w.S.StanceOf(1).Target, Is.EqualTo(1));
        }

        // A dash under way when the stun lands is finished: the dash steps of a player stunned on tick 2 are those of one not
        // stunned; afterwards the stunned player stands still with the stick held.
        [Test]
        public void D8_ADashUnderWayGoesOn()
        {
            List<long> Run(bool stun)
            {
                var w = new Down1Session(Down1Session.DownPack(), new[] { Soldier(1, 1, 5000, 4000, 1, -1).WithDash(4000, 6, 90) }, open: Player(1));
                if (stun) w.StunAt(2, 1, 60);
                w.S.SetPlayerInput(1, new PlayerInput(0, 1000, dash: true));
                return w.Steps(1, 10);
            }
            List<long> stunned = Run(true), free = Run(false);
            for (int i = 0; i < 6; i++) Assert.That(stunned[i], Is.EqualTo(free[i]), "dash step " + (i + 1));
            for (int i = 6; i < 10; i++)
            {
                Assert.That(stunned[i], Is.EqualTo(0L), "tick " + (i + 1));
                Assert.That(free[i], Is.GreaterThan(0L));
            }
        }

        // A terrain world: the stun of 60 does not fail the order nor count as a stall; the order arrives after the wake.
        [Test]
        public void D8_TerrainOrdersSurviveAStun()
        {
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { DashTWorlds.Plain(1, 2500, 1500) }, new[] { BridgeCommand.MoveTo(1, 1, 20500, 1500) })
                .WithTerrainRules(DashTWorlds.Pack()).WithCombat(Down1Session.NoStancePack());
            var w = new Down1Session(SimSession.Create(recipe, WarCore.Content.WcMapWriter.Write(DashTWorlds.Map())));
            w.StunAt(5, 1, 60);
            w.StepTo(5);
            MoveProgress before = M3TestWorld.Component<MoveProgress>(w.S, 1);
            SimVec3 at = w.Pos(1);
            w.StepTo(64);
            MoveProgress held = M3TestWorld.Component<MoveProgress>(w.S, 1);
            Assert.That(w.Pos(1), Is.EqualTo(at));
            Assert.That((held.Stall, held.OrderStall, held.Failed), Is.EqualTo((before.Stall, before.OrderStall, before.Failed)));
            Assert.That(Wp23.Intent(w.S).Active, Is.True);
            for (int t = 0; t < 600 && Wp23.Intent(w.S).Active; t++) w.Step();
            Assert.That(w.Pos(1), Is.EqualTo(Wp23.V(20500, 0, 1500)));
            Assert.That(M3TestWorld.Locomotion(w.S).HasFailed(w.Unit(1)), Is.False);
        }

        // ---- D9 the player ----

        // The player (key 1, team 1, armed, at (5, 8) m) aims south and pulls the trigger from tick 2: downed on tick 1, no shot and
        // the facing stays (north, the default); not downed, shots and the player turns south.
        [TestCase(true)]
        [TestCase(false)]
        public void D9_ADownedPlayerDoesNotFire(bool downed)
        {
            var w = new Down1Session(Down1Session.DownPack(), new[] { Soldier(1, 1, 5000, 8000, 1), Soldier(2, 2, 15000, 8000, 2, -1) },
                open: r => Player(1)(r).WithFacing());
            if (downed) w.DownAt(1, 1);
            w.StepTo(1);
            UnitFacing before = M3TestWorld.Component<UnitFacing>(w.S, 1);
            int fired = 0;
            for (int t = 0; t < 30; t++)
            {
                w.S.SetPlayerInput(w.Tick + 1, new PlayerInput(0, 0, 5000, 1000, primary: true));
                w.Step();
                foreach (FireRecord f in w.Combat.Fires) if (f.Shooter.Equals(w.Sim.Entities.StableOf(w.Unit(1)))) fired++;
            }
            UnitFacing after = M3TestWorld.Component<UnitFacing>(w.S, 1);
            if (downed)
            {
                Assert.That(fired, Is.EqualTo(0));
                Assert.That((after.X.Raw, after.Z.Raw), Is.EqualTo((before.X.Raw, before.Z.Raw)));
            }
            else
            {
                Assert.That(fired, Is.GreaterThan(0));
                Assert.That((after.X.Raw, after.Z.Raw), Is.EqualTo((0L, -SimFix.Scale)));
            }
        }

        // The player (key 1) dies on tick 5; squad mates of team 1: key 2 downed (1 m away), key 3 stunned (3 m), key 4 living
        // (6 m). The fallback takes the nearest one that is not downed: the stunned key 3.
        [Test]
        public void D9_TheFallbackSkipsTheDownedButTakesTheStunned()
        {
            var w = new Down1Session(Down1Session.DownPack(), new[]
            {
                Soldier(1, 1, 5000, 8000, 1, -1), Soldier(2, 1, 6000, 8000, 1, -1), Soldier(3, 1, 8000, 8000, 1, -1), Soldier(4, 1, 11000, 8000, 1, -1),
            }, open: Player(1));
            w.DownAt(2, 2);
            w.StunAt(2, 3, 600);
            Kill(w.S, 5, 1);
            w.StepTo(8);
            Assert.That(w.Dead(1), Is.True);
            Assert.That(w.Downed(2), Is.True);
            Assert.That(w.Stunned(3), Is.True);
            Assert.That(w.S.ControlledKey, Is.EqualTo((ulong?)3));
        }
    }
}
