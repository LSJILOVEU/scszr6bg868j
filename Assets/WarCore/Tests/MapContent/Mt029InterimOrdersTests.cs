using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Mt029InterimOrdersTests
    {
        private static M6World World()
        {
            return new M6World(new WorldRecipe(41, MoverKind.Interim,
                new[] { U(1, 10000, 10000, squad: 1), U(2, 20000, 10000, squad: 2) },
                new[] { BridgeCommand.Follow(1, 1, 2) }).WithStandingOrders());
        }

        [Test]
        public void Follow_RetargetsAfterOneAndAHalfMetres()
        {
            var w = World();
            w.Session.StepOnce();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(SimVec3.Metres(20, 0, 10)));

            SimVec3 target = M3TestWorld.Point(21500, 10000);
            w.Set(2, new Position(target));
            w.Session.StepOnce();

            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(target));
        }

        [Test]
        public void Follow_StopsAtExactlyThreeMetres()
        {
            var w = World();
            w.Session.StepOnce();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);

            SimVec3 at = SimVec3.Metres(17, 0, 10);
            w.Set(1, new Position(at));
            w.Session.StepOnce();

            Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(Point(w, 1), Is.EqualTo(at));
        }

        [Test]
        public void Follow_KeepsTargetAfterExactlyOneMetre()
        {
            var w = World();
            w.Session.StepOnce();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(SimVec3.Metres(20, 0, 10)));

            w.Set(2, new Position(SimVec3.Metres(21, 0, 10)));
            w.Session.StepOnce();

            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(SimVec3.Metres(20, 0, 10)));
        }
    }
}
