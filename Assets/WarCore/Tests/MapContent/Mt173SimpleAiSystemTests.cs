using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt173SimpleAiSystemTests
    {
        [Test]
        public void EqualDistanceGuardSlotsPreferTheLowerSlotIndex()
        {
            var point = new ControlPointDef(1, M6World.P(30), SimFix.FromInt(1));
            var units = new[] { M6World.U(1, 1, 30, 25) };
            var w = new M6World(M6World.Endless(
                M6World.RecipeOf(units, M6World.Pack(new[] { point }, think: 1))));

            w.Step();

            // Slots 1 and 2 share Z and have X offsets of -0.5 m and +0.5 m, so both are nearest.
            var expected = point.Center + new SimVec3(SimFix.Ratio(-1, 2), default, SimFix.FromRaw(56755));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(expected));
        }
    }
}
