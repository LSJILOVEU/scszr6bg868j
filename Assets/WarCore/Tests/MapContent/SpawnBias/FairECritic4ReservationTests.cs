using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairECritic4ReservationTests
    {
        [Category("Manual"), Test, Explicit("Reproduces a retained search reservation entering an occupied guard slot.")]
        public void FairECritic4_RetainedSearchRespectsReinforcedAlly()
        {
            SimVec3 standing = M6World.P(40);
            var units = new[]
            {
                A1.Unit(1, 1, 10, 20, weapon: 0, melee: -1),
                new UnitSpawn(2, 0, 1, 40000, 20000, 3000, 1, -1, true),
                A1.Unit(3, 2, 40, 20, melee: -1)
            };
            var recipe = A1.Recipe("{ \"targetMemoryTicks\": 100, \"searchTicks\": 10, \"fairDecisions\": true }", units, endless: true)
                .WithMatch(M6World.Pack(new[]
                {
                    new ControlPointDef(1, standing, SimFix.Ratio(1, 2), initialOwner: 1)
                }, interval: 3, think: 1, columns: 1, rows: 1,
                    bases: new[] { new SpawnAnchor(1, standing) })).WithStandingOrders();
            var w = new M6World(recipe, A1.Map());
            w.Step();
            w.Set(3, new Position(M6World.P(70)));
            w.Sim.CompleteSpatialBoundary();
            w.Sim.Step(new InputFrame(default, default,
                squad: new SquadCommand(SquadCommandKind.Hold, 2, default, default)));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(standing));
            Assert.That(w.Has<Position>(2), Is.False);

            w.Step();

            Assert.That(w.Reinforce.Successes(1), Is.EqualTo(1));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.False);
            Assert.That(w.Read<Position>(2).Value, Is.EqualTo(standing));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False,
                "A retained search must release a guard slot occupied by a standing ally.");
        }
    }
}
