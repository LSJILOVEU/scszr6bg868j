using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairECritic5ArbitrationTests
    {
        [Test, Category("Manual"), Explicit("Reproduces a new chase bypassing standing ally occupancy.")]
        public void FairECritic5_NewChaseRespectsStandingAlly()
        {
            SimVec3 destination = M6World.P(40), standing = M6World.P(41), retreat = M6World.P(60);
            var units = new[]
            {
                A1.Unit(1, 1, 10, 20), A1.Unit(2, 1, 41, 20, melee: -1),
                A1.Unit(3, 2, 40, 20, melee: -1)
            };
            var recipe = A1.Recipe("{ \"meleeChaseRange\": 60, \"fairDecisions\": true }", units)
                .WithMatch(M6World.Pack(new[]
                {
                    new ControlPointDef(1, destination, SimFix.Ratio(1, 2), initialOwner: 1),
                    new ControlPointDef(2, retreat, SimFix.Ratio(1, 2), initialOwner: 1)
                }, think: 1)).WithStandingOrders();
            var w = new M6World(recipe, A1.Map());

            Assert.That(new GuardSlots(recipe.Match.Points).Nearest(standing), Is.EqualTo(0));

            w.Sim.Step(L4.Squad(SquadCommandKind.MoveTo, 2, retreat));

            Assert.That(w.Read<MoveIntent>(3).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(3).Target, Is.EqualTo(retreat));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.False);
            Assert.That(w.Read<Position>(2).Value, Is.EqualTo(standing));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False,
                "A new chase must not reserve a guard slot occupied by a standing ally.");
        }

        [Test, Category("Manual"), Explicit("Reproduces an enemy masking a standing ally during retained arbitration.")]
        public void FairECritic5_RetainedChaseRespectsAllySharingEnemySlot()
        {
            SimVec3 destination = M6World.P(40), standing = M6World.P(41);
            var units = new[]
            {
                A1.Unit(1, 1, 10, 20), A1.Unit(2, 1, 41, 20, melee: -1),
                A1.Unit(3, 2, 40, 20, melee: -1)
            };
            var recipe = A1.Recipe("{ \"meleeChaseRange\": 60, \"fairDecisions\": true }", units)
                .WithMatch(M6World.Pack(new[]
                {
                    new ControlPointDef(1, destination, SimFix.Ratio(1, 2), initialOwner: 1)
                }, think: 1));
            var w = new M6World(recipe, A1.Map());
            L4.Moves(w).Order(w.Sim.Entities.StableOf(w.Unit(1)), destination);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(new GuardSlots(recipe.Match.Points).Nearest(standing), Is.EqualTo(0));

            w.Step();

            Assert.That(w.Read<MoveIntent>(2).Active, Is.False);
            Assert.That(w.Read<Position>(2).Value, Is.EqualTo(standing));
            Assert.That(w.Read<MoveIntent>(3).Active, Is.False);
            Assert.That(w.Read<Position>(3).Value, Is.EqualTo(destination));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False,
                "A standing enemy must not hide an ally occupying the retained chase slot.");
        }
    }
}
