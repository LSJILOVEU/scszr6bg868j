using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M4ReplanRegressionTests
    {
        [Test]
        public void CrowdWaypoint_NextCellThenExactTarget_ReplansOnCellEntry()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(8, 8), new[] { new UnitSpawn(1, 0, 1, 1100, 1300, 6000) });
            world.Order(0, 3600, 1700);
            Assert.That(world.Sim.Entities.Get(world.Waypoints, world.Unit(0)).Point, Is.EqualTo(M3TestWorld.Point(2500, 1500)));
            int entered = 0;
            for (int tick = 1; tick <= 20; tick++)
            {
                world.Sim.Step(default);
                var point = world.Point(0);
                if (entered == 0 && point.X.Raw >= 2 * 65536)
                {
                    entered = tick;
                    Assert.That(point, Is.Not.EqualTo(M3TestWorld.Point(2500, 1500)), "Replan before hitting the cell centre.");
                    Assert.That(world.Sim.Entities.Get(world.Waypoints, world.Unit(0)).Point, Is.EqualTo(M3TestWorld.Point(3500, 1500)));
                }
                if (point.X.Raw >= 3 * 65536 && world.Intent(0).Active)
                    Assert.That(world.Sim.Entities.Get(world.Waypoints, world.Unit(0)).Point, Is.EqualTo(M3TestWorld.Point(3600, 1700)));
            }
            Assert.That(entered, Is.Positive);
            Assert.That(world.Point(0), Is.EqualTo(M3TestWorld.Point(3600, 1700)));
            Assert.That(world.Intent(0).Active, Is.False);
        }

        // Self-review counter-example: entering the next cell early turns toward a wall corner closer than the radius.
        [Test]
        public void SingleUnit_ClippingWallCorner_StillReachesTarget()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(4, 4, (1, 2, 1, 1)), new[] { new UnitSpawn(1, 0, 1, 1010, 1500, 6000) });
            world.Order(0, 2500, 2500);
            for (int tick = 1; tick <= 100; tick++) world.Sim.Step(default);
            Assert.That(world.Point(0), Is.EqualTo(M3TestWorld.Point(2500, 2500)));
        }

        [Test]
        public void TwoHeadOn_MustReachTargetsBy600()
        {
            var session = SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd,
                new[] { new UnitSpawn(1, 0, 1, 4500, 4500, 6000), new UnitSpawn(2, 0, 2, 24500, 4500, 6000) },
                new[] { BridgeCommand.MoveTo(1, 1, 24500, 4500), BridgeCommand.MoveTo(2, 2, 4500, 4500) }),
                WcMapWriter.Write(M3TestWorld.Boxes(32, 12)));
            for (int tick = 1; tick <= 600; tick++)
            {
                session.StepOnce();
                if (tick == 49 || tick == 50 || tick == 600)
                {
                    var a = M3TestWorld.Position(session); var b = M3TestWorld.Position(session, 2);
                    var wa = M3TestWorld.Component<MoveWaypoint>(session).Point;
                    var wb = M3TestWorld.Component<MoveWaypoint>(session, 2).Point;
                    TestContext.Out.WriteLine($"tick={tick} a={a.X.Raw},{a.Z.Raw} wa={wa.X.Raw},{wa.Z.Raw} b={b.X.Raw},{b.Z.Raw} wb={wb.X.Raw},{wb.Z.Raw}");
                }
            }
            Assert.That(M3TestWorld.Position(session), Is.EqualTo(M3TestWorld.Point(24500, 4500)));
            Assert.That(M3TestWorld.Position(session, 2), Is.EqualTo(M3TestWorld.Point(4500, 4500)));
        }
    }
}
