using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairECritic3OccupancyTests
    {
        [Test]
        public void FairECritic3_SearchRespectsStandingAlly()
        {
            SimVec3 standing = M6World.P(40);
            var units = new[]
            {
                A1.Unit(1, 1, 10, 20, weapon: 0, melee: -1),
                A1.Unit(2, 1, 60, 20, melee: -1),
                A1.Unit(3, 2, 40, 20, melee: -1)
            };
            var recipe = A1.Recipe("{ \"targetMemoryTicks\": 100, \"searchTicks\": 10, \"fairDecisions\": true }", units, endless: true)
                .WithMatch(M6World.Pack(new[]
                {
                    new ControlPointDef(1, standing, SimFix.Ratio(1, 2), initialOwner: 1)
                }, think: 1));
            var w = new M6World(recipe, A1.Map());
            w.Step();
            StableId observer = w.Sim.Entities.StableOf(w.Unit(1));
            Assert.That(w.System<TargetMemory>().TryRecall(observer, 1, out _, out SimVec3 remembered, out _), Is.True);
            Assert.That(remembered, Is.EqualTo(standing));
            w.Set(3, new Position(M6World.P(70)));
            w.Set(2, new Position(standing));
            w.Sim.CompleteSpatialBoundary();

            w.Step();

            Assert.That(w.Read<MoveIntent>(2).Active, Is.False);
            Assert.That(w.Read<Position>(2).Value, Is.EqualTo(standing));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False,
                "A search must not reserve a guard slot occupied by a standing ally.");
        }
    }
}
