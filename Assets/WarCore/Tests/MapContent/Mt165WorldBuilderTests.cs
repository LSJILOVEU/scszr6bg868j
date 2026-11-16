using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt165WorldBuilderTests
    {
        [Test]
        public void CrowdMove_ArrivesAtTwoMetreSlots()
        {
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd,
                new[] {
                    new UnitSpawn(1, 0, 7, 4000, 4000, 6000),
                    new UnitSpawn(2, 0, 7, 6000, 4000, 6000) },
                new[] { BridgeCommand.MoveTo(1, 7, 5000, 6000) });
            var session = SimSession.Create(recipe, WcMapWriter.Write(M3TestWorld.Boxes(12, 12)));
            for (int tick = 0; tick < 30; tick++) session.StepOnce();

            Assert.That(session.Current.TryGet(1, out ViewEntity left), Is.True);
            Assert.That(session.Current.TryGet(2, out ViewEntity right), Is.True);
            Assert.That(left.X, Is.EqualTo(4f));
            Assert.That(right.X, Is.EqualTo(6f));
            Assert.That(left.Z, Is.EqualTo(6f));
            Assert.That(right.Z, Is.EqualTo(6f));
        }

        [Test]
        public void DemoFormation_FirstSlotUsesOneMetreSpacing()
        {
            UnitSpawn first = DemoRecipes.M4().Units[0];
            Assert.That(first.XMm, Is.EqualTo(4500));
            Assert.That(first.ZMm, Is.EqualTo(9000));
        }
    }
}
