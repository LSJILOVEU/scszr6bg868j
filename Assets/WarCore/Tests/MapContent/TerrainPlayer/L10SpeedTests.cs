using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (3): the surface under the player scales its step like an AI unit's (floor toward zero per axis, from the start cell).
    public sealed class L10SpeedTests
    {
        private static long FirstStep(M6World w, int x, int z, bool alongZ)
        {
            SimVec3 before = At(w);
            SimVec3 after = Walk(w, x, z, 1)[0];
            return alongZ ? after.Z.Raw - before.Z.Raw : after.X.Raw - before.X.Raw;
        }

        // Grass (12.5, 6.5) is on the slope, road (5.5, 16.5) and mud (37.5, 8.5) are flat.
        [TestCase(12500, 6500, 1000, 13107)]
        [TestCase(12500, 6500, -1000, -13107)]
        [TestCase(5500, 16500, 1000, 16383)]
        [TestCase(5500, 16500, -1000, -16383)]
        [TestCase(37500, 8500, 1000, 6553)]
        [TestCase(37500, 8500, -1000, -6553)]
        public void L10Speed_XStepBySurface(long x, long z, int input, long expected)
        {
            Assert.That(FirstStep(Demo(x, z), input, 0, false), Is.EqualTo(expected));
        }

        [TestCase(1000, 6553)]
        [TestCase(-1000, -6553)]
        public void L10Speed_ZStepInMud(int input, long expected)
        {
            Assert.That(FirstStep(Demo(37500, 8500), 0, input, true), Is.EqualTo(expected));
        }

        // From the road at z = 14.1 toward the grass: the first step still uses the road (the start cell).
        [Test]
        public void L10Speed_TheStartCellDecides()
        {
            Assert.That(FirstStep(Demo(5500, 14100), 0, -1000, true), Is.EqualTo(-16383));
        }

        // Largest multiplier (2000) at the 7500 mm/s base cap: 32768 raw (0.5 m) per tick, longer than the radius, so the whole
        // path is checked; the player stays on the ground and on passable cells.
        [Test]
        public void L10Speed_LargestMultiplierLongStep()
        {
            var w = Demo(12500, 6500, speed: 7500, rules: Fast());
            SimVec3 before = At(w);
            SimVec3[] p = Walk(w, 1000, 0, 20);
            Assert.That(p[0].X.Raw - before.X.Raw, Is.EqualTo(32768));
            for (int t = 1; t < p.Length; t++) Assert.That(p[t].X.Raw - p[t - 1].X.Raw, Is.EqualTo(32768), "tick " + (t + 1));
            foreach (SimVec3 q in p)
            {
                Assert.That(OnGround(w, q), Is.True);
                Assert.That(w.Session.BlockedCell((int)(q.X.Raw >> 16), (int)(q.Z.Raw >> 16)), Is.False);
            }
        }

        // 900 mm/s = 1966 raw per tick; input (1, 0) is a 1 raw step on grass and 0 in mud. In mud the world equals a world
        // without input in the player's position, facing and move intent, tick by tick (the raw input itself enters the hash).
        [Test]
        public void L10Speed_AStepScaledToNothingDoesNothing()
        {
            // Flat twin (no height field, no surface table: the flat branch) and grass (multiplier 1000): one raw per tick.
            foreach (M6World moving in new[] { Demo(12500, 6500, speed: 900, facing: true, map: Flat()), Demo(12500, 6500, speed: 900, facing: true) })
            {
                SimVec3 before = At(moving);
                SimVec3[] p = Walk(moving, 1, 0, 10);
                foreach (SimVec3 now in p) { Assert.That(now.X.Raw - before.X.Raw, Is.EqualTo(1), "one raw per tick"); before = now; }
            }

            var mud = Demo(37500, 8500, speed: 900, facing: true);
            var still = Demo(37500, 8500, speed: 900, facing: true);
            mud.Session.SetPlayerInput(1, new PlayerInput(1, 0));
            for (int t = 1; t <= 20; t++)
            {
                mud.Session.StepOnce(); still.Session.StepOnce();
                Assert.That(At(mud), Is.EqualTo(At(still)), "position tick " + t);
                Assert.That(mud.Read<UnitFacing>(1), Is.EqualTo(still.Read<UnitFacing>(1)), "facing tick " + t);
                Assert.That(mud.Read<MoveIntent>(1), Is.EqualTo(still.Read<MoveIntent>(1)), "intent tick " + t);
            }
        }
    }
}
