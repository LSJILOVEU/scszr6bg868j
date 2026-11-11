using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairECritic4LifecycleTests
    {
        [Category("Manual"), Test, Explicit("Reproduces a failed departure exposing its standing slot.")]
        public void FairECritic4_FailedDepartureKeepsItsStandingSlot()
        {
            SimVec3 standing = L4.Cell(40, 20), blocked = L4.Cell(30, 20), previous = L4.Cell(60, 20);
            var units = new[]
            {
                new UnitSpawn(1, 0, 2, 40500, 20500, 3000, 2, -1),
                new UnitSpawn(2, 0, 1, 45500, 20500, 3000, 1, -1)
            };
            var recipe = A1.Recipe("{ \"fairDecisions\": true }", units)
                .WithMatch(M6World.Pack(new[]
                {
                    new ControlPointDef(1, standing, SimFix.Ratio(1, 2), initialOwner: 2),
                    new ControlPointDef(2, blocked, SimFix.Ratio(1, 2), initialOwner: 1),
                    new ControlPointDef(3, previous, SimFix.Ratio(1, 2), initialOwner: 2)
                }, think: 1));
            var w = new M6World(recipe, A1.Map(L4.Ring(30, 20)));
            L4.Moves(w).Order(w.Sim.Entities.StableOf(w.Unit(1)), previous);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(L4.Moves(w).CanStandAt(blocked), Is.True);

            w.Step();

            Assert.That(L4.Moves(w).HasFailed(w.Sim.Entities.StableOf(w.Unit(1))), Is.True);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(w.Read<Position>(1).Value, Is.EqualTo(standing));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.False,
                "A failed departure must keep its standing slot occupied during arbitration.");
        }
    }
}
