using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairECritic3LifecycleTests
    {
        [Test]
        public void FairECritic3_ReinforcementCannotExposeRetainedLosersStandingSlot()
        {
            SimVec3 slot = M6World.P(40), enemy = M6World.P(60);
            var units = new[]
            {
                A1.Unit(1, 1, 40, 20),
                new UnitSpawn(2, 0, 1, 50000, 22000, 3000, 1, -1, true, 0),
                A1.Unit(3, 1, 30, 20, melee: -1), A1.Unit(4, 2, 60, 20, melee: -1)
            };
            var recipe = A1.Recipe("{ \"meleeChaseRange\": 60, \"fairDecisions\": true }", units)
                .WithMatch(M6World.Pack(new[]
                {
                    new ControlPointDef(1, slot, SimFix.Ratio(1, 2)),
                    new ControlPointDef(2, enemy, SimFix.Ratio(1, 2))
                }, interval: 2, think: 1, columns: 1, rows: 1,
                    bases: new[] { new SpawnAnchor(1, M6World.P(50, 22)) }));
            var w = new M6World(recipe, A1.Map());

            w.Step();
            Assert.That(w.Has<Position>(2), Is.False);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(enemy));
            Assert.That(w.Read<MoveIntent>(3).Active, Is.False);
            SimVec3 standing = w.Read<Position>(1).Value;
            w.Step();

            Assert.That(w.Reinforce.Successes(1), Is.EqualTo(1));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(2).Target, Is.EqualTo(enemy));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(w.Read<Position>(1).Value, Is.EqualTo(standing));
            Assert.That(w.Read<MoveIntent>(3).Active, Is.False,
                "A retained chase loser must occupy its standing slot before a new route is granted.");
        }
    }
}
