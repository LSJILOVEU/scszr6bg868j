using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Dash
{
    // M7a acceptance 2 and 3, and how a dash meets an order, the player's input and inertia.
    public sealed class DashStepTests
    {
        private static SimSession Open(params UnitSpawn[] extra)
        {
            var units = new UnitSpawn[extra.Length + 1];
            units[0] = DashWorlds.Dasher(1, 40500, 12500);
            Array.Copy(extra, 0, units, 1, extra.Length);
            return DashWorlds.World(units, DashWorlds.Map());
        }

        private static void StepTo(SimSession s, long tick) { while (DashWorlds.Tick(s) < tick) s.StepOnce(); }

        // Cooldown 60, counted from the tick of the request. Requested at tick 0: steps on ticks 1..10, refused while dashing
        // (tick 5) and after it up to tick 59, accepted again at tick 60 (steps 61..70, ready again at 120).
        [Test]
        public void Cooldown_RefusesUntilTheReadyTickThenAccepts()
        {
            SimSession s = Open();
            EntityId unit = M3TestWorld.Unit(s);
            Assert.That(DashWorlds.State(s), Is.EqualTo(DashState.Initial));
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True, "tick 0");
            Assert.That(DashWorlds.State(s).Start, Is.EqualTo(0));
            Assert.That(DashWorlds.State(s).Ready, Is.EqualTo(60));
            Assert.That(DashWorlds.Loco(s).IsDashing(unit), Is.True);
            StepTo(s, 5);
            DashState during = DashWorlds.State(s);
            Assert.That(DashWorlds.Dash(s, DashWorlds.MinusX), Is.False, "tick 5, still dashing");
            Assert.That(DashWorlds.State(s), Is.EqualTo(during), "a refused request writes nothing");
            StepTo(s, 10);
            Assert.That(DashWorlds.Loco(s).IsDashing(unit), Is.False, "the tenth step was the last");
            StepTo(s, 59);
            Assert.That(DashWorlds.Dash(s, DashWorlds.MinusX), Is.False, "tick 59");
            SimVec3 at59 = M3TestWorld.Position(s);
            StepTo(s, 60);
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(at59), "no step without a dash");
            Assert.That(DashWorlds.Dash(s, DashWorlds.MinusX), Is.True, "tick 60");
            Assert.That(DashWorlds.State(s).Start, Is.EqualTo(60));
            Assert.That(DashWorlds.State(s).Ready, Is.EqualTo(120));
            s.StepOnce();
            Assert.That(M3TestWorld.Position(s).X.Raw - at59.X.Raw, Is.EqualTo(-2 * DashWorlds.S), "tick 61 takes the first step back");
        }

        // A request made inside a step (here by asking between steps at the same clock reading as a system would) and one made
        // between steps both take their first step on tick Start + 1; a direction without a horizontal part, a unit without a
        // dash and a world without dash are refused.
        [Test]
        public void Refusals_WriteNothing()
        {
            SimSession s = Open(DashWorlds.Plain(2, 60500, 12500));
            Assert.That(DashWorlds.Dash(s, SimVec3.Metres(0, 1, 0)), Is.False, "no horizontal part");
            Assert.That(DashWorlds.Dash(s, default), Is.False, "zero direction");
            Assert.That(DashWorlds.State(s), Is.EqualTo(DashState.Initial));
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX, 2), Is.False, "unit 2 has no dash");
            SimSession plain = DashWorlds.World(new[] { DashWorlds.Plain(1, 40500, 12500) }, DashWorlds.Map());
            Assert.That(DashWorlds.Dash(plain, DashWorlds.PlusX), Is.False, "a world without dash");
        }

        // Acceptance 3, by hand. 1000 mm is floor(1000 * 2^16 / 1000) = 65536 raw. Along +X the direction is (65536, 0) and the total
        // (65536 * 65536) >> 16 = 65536. Step k of 3 is floor(65536k / 3) - floor(65536(k-1) / 3): 21845, 21845, 21846. Along -X the
        // total is -65536 and floor goes toward negative infinity: floor(-65536/3) = -21846, floor(-131072/3) = -43691, so -21846,
        // -21845, -21845. Every product is |total| * k <= 65536 * 3 < 2^18 (the bound in general: 2^23 * 2^9 = 2^32, in 64 bits).
        [TestCase(1, new long[] { 21845, 21845, 21846 })]
        [TestCase(-1, new long[] { -21846, -21845, -21845 })]
        public void Distance_SplitsByFloorAndAddsUpExactly(int sign, long[] steps)
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 40500, 12500, distanceMm: 1000, ticks: 3, cooldown: 3) }, DashWorlds.Map());
            Assert.That(M3TestWorld.Component<DashSpec>(s).Distance.Raw, Is.EqualTo(65536));
            Assert.That(DashWorlds.Dash(s, SimVec3.Metres(sign * 5, 0, 0)), Is.True, "the direction's length does not matter");
            Assert.That(DashWorlds.State(s).TotalX.Raw, Is.EqualTo(sign * 65536L));
            Assert.That(DashWorlds.State(s).TotalZ.Raw, Is.Zero);
            var track = DashWorlds.Track(s, 4);
            for (int k = 1; k <= 3; k++)
            {
                Assert.That(track[k].X.Raw - track[k - 1].X.Raw, Is.EqualTo(steps[k - 1]), "step " + k);
                Assert.That(track[k].Z, Is.EqualTo(track[0].Z));
            }
            Assert.That(track[3].X.Raw - track[0].X.Raw, Is.EqualTo(sign * 65536L));
            Assert.That(track[4], Is.EqualTo(track[3]));
        }

        // From (10.5, 1.5) the unit walks one tick toward (30.5, 1.5), dashes 20 m along +Z (to z 21.5 of the 24 m map), then walks back.
        // A unit with an order stops following it while it dashes and heads for it again from where the dash left it.
        [Test]
        public void Order_WaitsDuringTheDashThenResumes()
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 10500, 1500) }, DashWorlds.Map(),
                new[] { BridgeCommand.MoveTo(1, 1, 30500, 1500) });
            s.StepOnce();
            SimVec3 walked = M3TestWorld.Position(s);
            Assert.That(walked.X.Raw - SimFix.Ratio(21, 2).Raw, Is.EqualTo(13107), "tick 1 walks 0.2 m");
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusZ), Is.True);
            var track = DashWorlds.Track(s, DashWorlds.Ticks);
            for (int k = 1; k <= DashWorlds.Ticks; k++)
            {
                Assert.That(track[k].X, Is.EqualTo(walked.X), "dash step " + k + " leaves x alone");
                Assert.That(track[k].Z.Raw - track[k - 1].Z.Raw, Is.EqualTo(2 * DashWorlds.S), "dash step " + k);
            }
            Assert.That(Wp23.Intent(s).Active, Is.True, "the order is kept");
            SimVec3 waypoint = M3TestWorld.Component<MoveWaypoint>(s).Point, at = track[DashWorlds.Ticks];
            Assert.That(DashWorlds.SegmentDistance(at, at, waypoint), Is.LessThan(1.5), "the route is planned again from where the dash ended");
            for (int t = 0; t < 400 && Wp23.Intent(s).Active; t++) s.StepOnce();
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(new SimVec3(SimFix.FromInt(30) + SimFix.Ratio(1, 2), default, SimFix.FromInt(1) + SimFix.Ratio(1, 2))),
                "the order still arrives");
        }

        // The player's own step waits while the player dashes: the input walks +X, the dash goes +Z.
        [Test]
        public void Player_InputWaitsDuringTheDash()
        {
            var units = new[] { DashWorlds.Dasher(1, 8000, 2000, team: 1), DashWorlds.Plain(2, 70000, 20000, team: 2) };
            SimSession s = DashWorlds.World(units, WcMapWriter.Write(M3TestWorld.Boxes(80, 40)), null,
                r => Wp16TestWorld.Recipe(r.Units.ToArray(), 1, Wp16TestWorld.Match()));
            s.SetPlayerInput(1, new PlayerInput(1000, 0));
            s.StepOnce();
            SimVec3 before = M3TestWorld.Position(s);
            Assert.That(before.X.Raw, Is.GreaterThan(SimFix.FromInt(8).Raw), "the input walks +X");
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusZ), Is.True);
            var track = DashWorlds.Track(s, DashWorlds.Ticks + 1);
            for (int k = 1; k <= DashWorlds.Ticks; k++)
            {
                Assert.That(track[k].X, Is.EqualTo(before.X), "dash step " + k + ": no input step");
                Assert.That(track[k].Z.Raw - track[k - 1].Z.Raw, Is.EqualTo(2 * DashWorlds.S), "dash step " + k);
            }
            Assert.That(track[DashWorlds.Ticks + 1].X.Raw, Is.GreaterThan(before.X.Raw), "the input walks again after the dash");
        }

        // A dash ignores inertia: the motion model is not asked during it, and the first step after it starts from rest.
        // Acceleration 9000 mm/s^2 = 655 raw per tick squared (InertiaStepTests).
        [Test]
        public void Inertia_IsIgnoredAndTheNextStepStartsFromRest()
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 10500, 1500) }, DashWorlds.Map(),
                new[] { BridgeCommand.MoveTo(1, 1, 60500, 1500) }, r => r.WithMotion().With(new InertiaOption(9000, 0)));
            for (int k = 1; k <= 20; k++) s.StepOnce();
            Assert.That(M3TestWorld.Component<MotionState>(s), Is.EqualTo(new MotionState(SimFix.FromRaw(13100), 20)), "near full speed at tick 20");
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusZ), Is.True);
            var track = DashWorlds.Track(s, DashWorlds.Ticks);
            for (int k = 1; k <= DashWorlds.Ticks; k++)
                Assert.That(track[k].Z.Raw - track[k - 1].Z.Raw, Is.EqualTo(2 * DashWorlds.S), "dash step " + k + " at full length from the first");
            Assert.That(M3TestWorld.Component<MotionState>(s), Is.EqualTo(new MotionState(SimFix.FromRaw(13100), 20)), "not asked while dashing");
            s.StepOnce();
            Assert.That(M3TestWorld.Component<MotionState>(s), Is.EqualTo(new MotionState(SimFix.FromRaw(655), 31)), "starts from rest");
        }
    }
}
