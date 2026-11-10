using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt023PlayerFireTests
    {
        [Test]
        public void HoldingAtOwnPositionUsesFacing()
        {
            var w = new M6World(DemoRecipes.WithPlayerBlock(
                Wp18TestWorld.Recipe().WithFacing(), new BlockSpec(500, 1000)));
            w.Session.SetPlayerInput(1, new PlayerInput(0, 0, 10000, 10000, false, true));
            w.Session.StepOnce();

            BlockState held = w.Read<BlockState>(1);
            Assert.That(held.Holding, Is.True);
            Assert.That((held.DirX, held.DirZ), Is.EqualTo((0L, SimFix.Scale)));
        }
    }
}
