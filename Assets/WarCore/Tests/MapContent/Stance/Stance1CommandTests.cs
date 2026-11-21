using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Stance
{
    // STANCE-1 S11 (the scripted stance command and the action gate on the dash) and the dash rows of S4.
    public sealed class Stance1CommandTests
    {
        // ---- BridgeCommand.Stance ----

        // Squad 1 is keys 3, 1, 2 in spawn order (not key order) and walks from tick 1; key 2 dies at tick 2. The command of tick
        // 3 asks the two living units (due 3 + 6 = 9), skips the dead one and is no squad order: the walk goes on as ordered.
        [Test]
        public void S11_TheCommandAsksTheLivingUnitsOfTheSquad()
        {
            UnitSpawn[] units =
            {
                Soldier(3, 1, 5000, 4000, 1, -1), Soldier(1, 1, 5000, 8000, 1, -1), Soldier(2, 1, 5000, 12000, 1, -1),
                Soldier(4, 2, 35000, 8000, 2, -1),
            };
            BridgeCommand[] script = { BridgeCommand.MoveTo(1, 1, 30000, 8000), BridgeCommand.Stance(3, 1, 1) };
            SimSession s = World(Pack(), units, Map(), script);
            Kill(s, 2, 2);
            StepTo(s, 2);
            MoveIntent before = M3TestWorld.Component<MoveIntent>(s, 1);
            s.StepOnce();
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(0, 1, 9)));
            Assert.That(s.StanceOf(3), Is.EqualTo(new StanceView(0, 1, 9)));
            Assert.That(s.StanceOf(2), Is.EqualTo(new StanceView(0, -1, 0)), "the dead unit is skipped");
            Assert.That(s.StanceOf(4), Is.EqualTo(new StanceView(0, -1, 0)), "another squad is not asked");
            MoveIntent after = M3TestWorld.Component<MoveIntent>(s, 1);
            Assert.That((after.Target, after.Active, after.Squad), Is.EqualTo((before.Target, before.Active, before.Squad)));
            Assert.That(after.Active, Is.True);
            StepTo(s, 8);
            FrameView previous = s.Current;
            s.StepOnce();
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(1, -1, 0)));
            Assert.That(previous.TryGet(1, out ViewEntity was) && was.Stance == 0, Is.True);
            Assert.That(s.Current.TryGet(1, out ViewEntity now) && now.Stance == 1, Is.True, "the frame shows the stance");
            Assert.That(FrameView.Interpolate(previous, s.Current, 0.5f).TryGet(1, out ViewEntity mid) && mid.Stance == 1, Is.True,
                "an interpolated frame keeps the newer stance");
        }

        // The player's own unit is not asked by its squad's command; the other member is.
        [Test]
        public void S11_TheCommandSkipsThePlayer()
        {
            UnitSpawn[] units = { Soldier(1, 1, 5000, 8000, 1, -1), Soldier(2, 1, 5000, 10000, 1, -1) };
            SimSession s = World(Pack(), units, Map(), new[] { BridgeCommand.Stance(2, 1, 2) }, Player(1));
            StepTo(s, 2);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(0, -1, 0)));
            Assert.That(s.StanceOf(2), Is.EqualTo(new StanceView(0, 2, 23)));
        }

        // The command's level is checked against the world: 0 to the last level with stances, only 0 without.
        [Test]
        public void S11_TheLevelIsChecked()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.Stance(1, 1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.Stance(1, 1, CombatPack.MaxStances + 1));
            Assert.DoesNotThrow(() => BridgeCommand.Stance(1, 1, CombatPack.MaxStances));
            UnitSpawn[] units = { Soldier(1, 1, 5000, 8000, 1, -1) };
            SimSession stanced = World(Pack(), units, Map()), field = World(Pack("rwr-field"), units, Map());
            Assert.DoesNotThrow(() => stanced.Enqueue(BridgeCommand.Stance(1, 1, 2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => stanced.Enqueue(BridgeCommand.Stance(2, 1, 3)));
            Assert.DoesNotThrow(() => field.Enqueue(BridgeCommand.Stance(1, 1, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => field.Enqueue(BridgeCommand.Stance(2, 1, 1)));
            // In the field world the command does nothing.
            field.StepOnce();
            Assert.That(field.StanceOf(1), Is.EqualTo(new StanceView(0, -1, 0)));
        }

        // ---- the dash key and the gate ----

        private const long DashMm = 4000;
        private const int DashTicks = 6, Cooldown = 90;

        // The player (key 1) with a 4 m / 6 tick / 90 tick dash, unarmed and alone.
        private static SimSession Dasher(CombatPack pack = null) =>
            World(pack ?? Pack(), new[] { Soldier(1, 1, 5000, 8000, 1, -1).WithDash(DashMm, DashTicks, Cooldown) }, Map(), open: Player(1));

        private static DashState Dash(SimSession s) => M3TestWorld.Component<DashState>(s, 1);

        // Pressed toward +z at tick 1: the z distance of the first step and the dash state after it.
        private static (long Step, DashState State) Press(SimSession s)
        {
            s.SetPlayerInput(1, new PlayerInput(0, 1000, dash: true));
            long z = M3TestWorld.Position(s, 1).Z.Raw;
            s.StepOnce();
            return (M3TestWorld.Position(s, 1).Z.Raw - z, Dash(s));
        }

        [Test]
        public void S11_StandingDashes()
        {
            (long step, DashState state) = Press(Dasher());
            Assert.That(step, Is.EqualTo(DashPart(DashMm * SimFix.Scale / 1000)));
            Assert.That((state.Start, state.Ready), Is.EqualTo((0L, (long)Cooldown)));
        }

        // Crouched (its deny holds the dash): no dash step, the walking step of the crouch, the cooldown untouched.
        [Test]
        public void S11_CrouchedDoesNotDash()
        {
            SimSession s = Dasher();
            Force(s, 1, 1);
            DashState initial = Dash(s);
            (long step, DashState state) = Press(s);
            Assert.That(step, Is.EqualTo(StatLine.Scale(M3TestWorld.Component<MoveIntent>(s, 1).SpeedPerTick.Raw, new[] { 500 })));
            Assert.That(state, Is.EqualTo(initial));
        }

        // A unit changing stance may not dash either (the change limit), even towards standing.
        [Test]
        public void S11_ChangingDoesNotDash()
        {
            SimSession s = Dasher();
            Assert.That(s.Stances.TryStance(Unit(s, 1), 1), Is.True);
            DashState initial = Dash(s);
            Assert.That(Press(s).State, Is.EqualTo(initial));
        }

        // Prone with the shipped deny: no dash. A fixture pack whose prone stance denies nothing: the prone player dashes.
        [Test]
        public void S11_AnEmptyProneDenyAllowsTheDash()
        {
            SimSession shipped = Dasher();
            Force(shipped, 1, 2);
            DashState initial = Dash(shipped);
            Assert.That(Press(shipped).State, Is.EqualTo(initial));

            StanceDef[] st = Pack().Stances.ToArray();
            st[1] = new StanceDef(st[1].Id, st[1].Body, st[1].SpeedPermille, st[1].SpreadPermille, st[1].Ticks, UnitAction.None);
            SimSession open = Dasher(Exact(stances: st));
            Force(open, 1, 2);
            (long step, DashState state) = Press(open);
            Assert.That(step, Is.EqualTo(DashPart(DashMm * SimFix.Scale / 1000)));
            Assert.That(state.Start, Is.EqualTo(0L));
        }

        // S4: a dashing unit may not change stance; the dashing limit also holds the dash, traverse, throw and interact. Once the
        // dash has no step left, the change starts.
        [Test]
        public void S4_ADashingUnitMayNotChangeStance()
        {
            SimSession s = Dasher();
            EntityId u = Unit(s, 1);
            Press(s);
            Assert.That(s.Gate.Denied(u), Is.EqualTo(UnitAction.Dash | UnitAction.Stance | UnitAction.Traverse | UnitAction.Throw | UnitAction.Interact));
            Assert.That(s.Stances.TryStance(u, 1), Is.False);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(0, -1, 0)));
            StepTo(s, DashTicks - 1);
            Assert.That(s.Stances.TryStance(u, 1), Is.False, "one step left");
            s.StepOnce();
            Assert.That(s.Gate.Denied(u), Is.EqualTo(UnitAction.None));
            Assert.That(s.Stances.TryStance(u, 1), Is.True);
        }

        // Step 1 of the dash: floor(total / ticks).
        private static long DashPart(long total) => total / DashTicks;
    }
}
