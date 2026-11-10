using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class TestCapCaptureTests
    {
        [TestCase(false, 3, 2, 1)]
        [TestCase(false, 3, 2, 2)]
        [TestCase(true, 2, 1, 1)]
        [TestCase(true, 2, 1, 2)]
        public void RatioDenominatorControlsProgressAndCaptureTick(bool strict, int leading, int trailing, int team)
        {
            var units = new UnitSpawn[leading + trailing];
            for (int i = 0; i < units.Length; i++)
                units[i] = M6World.U((ulong)i + 1, i < leading ? team : 3 - team, 20, 18 + i);
            var world = new M6World(M6World.Endless(M6World.RecipeOf(units,
                M6World.Pack(num: 3, den: 2, strict: strict, ticks: 4))));

            world.Step();
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(1));
            Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(team));
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(0));
            world.Step(2);
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(3));
            Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(3));
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(0));
            Assert.That(world.Capture.Changes.Length, Is.EqualTo(0));
            world.Step();
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(team));
            Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(0));
            Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(0));
            Assert.That(world.Capture.Changes.Length, Is.EqualTo(1));
            Assert.That(world.Capture.Changes[0].Tick, Is.EqualTo(4));
            Assert.That(world.Capture.Changes[0].PointId, Is.EqualTo(7));
            Assert.That(world.Capture.Changes[0].OldOwner, Is.EqualTo(0));
            Assert.That(world.Capture.Changes[0].NewOwner, Is.EqualTo(team));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void OccupyingOwnerKeepsZeroProgressAndEmitsNoCapture(int team)
        {
            var points = new[] { new ControlPointDef(7, M6World.P(20), SimFix.FromInt(6), team) };
            var world = new M6World(M6World.Endless(M6World.RecipeOf(
                new[] { M6World.U(1, team) }, M6World.Pack(points, ticks: 3))));
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(team));

            for (int tick = 1; tick <= 4; tick++)
            {
                world.Step();
                Assert.That(world.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(team));
                Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(0));
                Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(0));
                Assert.That(world.Capture.Changes.Length, Is.EqualTo(0));
            }
        }

        [Test]
        public void DecayPastRemainingProgressStaysAtZeroOnEmptyTicks()
        {
            var world = new M6World(M6World.Endless(M6World.RecipeOf(
                new[] { M6World.U(1, 1) }, M6World.Pack(ticks: 10, mode: InterruptMode.Decay, decay: 2))));
            world.Step();
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(1));
            Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(1));
            Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(1));
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(0));
            Assert.That(world.Capture.Changes.Length, Is.EqualTo(0));

            world.Set(1, new Position(M6World.P(50)));
            for (int tick = 2; tick <= 3; tick++)
            {
                world.Step();
                Assert.That(world.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(0));
                Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(0));
                Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(0));
                Assert.That(world.Capture.Changes.Length, Is.EqualTo(0));
            }
        }

        [Test]
        public void ConfiguredDecayRemovesThreeProgressTicksAndClampsAtZero()
        {
            var world = new M6World(M6World.Endless(M6World.RecipeOf(
                new[] { M6World.U(1, 1) }, M6World.Pack(ticks: 10, mode: InterruptMode.Decay, decay: 3))));
            world.Step(5);
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(5));
            Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(5));
            Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(1));

            world.Set(1, new Position(M6World.P(50)));
            world.Step();
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(6));
            Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(2));
            Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(1));
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(0));
            world.Step();
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo(7));
            Assert.That(world.Capture.Points[0].Progress, Is.EqualTo(0));
            Assert.That(world.Capture.Points[0].Capturing, Is.EqualTo(0));
            Assert.That(world.Capture.Points[0].Owner, Is.EqualTo(0));
            Assert.That(world.Capture.Changes.Length, Is.EqualTo(0));
        }
    }
}
