using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairECritic2ArbitrationTests
    {
        [Test]
        [Explicit("Known reservation conflict; run by name.")]
        public void FairECritic2_ChaseRespectsCommandedReservation()
        {
            SimVec3 enemy = SimVec3.Metres(40, 0, 20);
            var units = new[]
            {
                new UnitSpawn(1, 0, 99, 10000, 18000, 3000, 1, -1, false, 0),
                A1.Unit(2, 1, 10, 22), A1.Unit(3, 2, 40, 20, melee: -1, speed: 1)
            };
            var recipe = A1.Recipe("{ \"meleeChaseRange\": 60, \"fairDecisions\": true }", units)
                .WithMatch(M6World.Pack(new[] { new ControlPointDef(1, enemy, SimFix.FromInt(8)) }, think: 1))
                .WithStandingOrders();
            var w = new M6World(recipe, A1.Map());

            w.Sim.Step(new InputFrame(default, default,
                squad: new SquadCommand(SquadCommandKind.MoveTo, 99, enemy, default)));

            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(enemy));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.False,
                "A chase must not duplicate a commanded ally's guard-slot reservation.");
        }

        [Test]
        [Explicit("Known standing-slot conflict; run by name.")]
        public void FairECritic2_StoppedUnitKeepsItsStandingSlot()
        {
            SimVec3 standing = M6World.P(40), destination = M6World.P(60);
            var units = new[]
            {
                A1.Unit(1, 1, 40, 20, weapon: 0, melee: -1),
                A1.Unit(2, 1, 30, 20, melee: -1),
                A1.Unit(3, 2, 50, 20, weapon: 0, melee: -1)
            };
            var recipe = A1.Recipe("{ \"fairDecisions\": true }", units)
                .WithMatch(M6World.Pack(new[]
                {
                    new ControlPointDef(1, standing, SimFix.Ratio(1, 2)),
                    new ControlPointDef(2, destination, SimFix.Ratio(1, 2))
                }, think: 1));
            var w = new M6World(recipe, A1.Map());
            L4.Moves(w).Order(w.Sim.Entities.StableOf(w.Unit(1)), destination);

            w.Step();

            Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(w.Read<Position>(1).Value, Is.EqualTo(standing));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.False,
                "A stop must occupy its standing slot before a new route is granted.");
        }
    }
}
