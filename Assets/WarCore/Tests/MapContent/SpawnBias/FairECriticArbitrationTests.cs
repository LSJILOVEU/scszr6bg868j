using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairECriticArbitrationTests
    {
        [Test]
        public void FairECritic_ChaseReservationRemainsExclusiveOnNextTick()
        {
            var units = new[] { A1.Unit(1, 1, 10, 18), A1.Unit(2, 1, 10, 22), A1.Unit(3, 2, 40, 20, melee: -1) };
            SimVec3 enemy = SimVec3.Metres(40, 0, 20);
            var recipe = A1.Recipe("{ \"meleeChaseRange\": 60, \"fairDecisions\": true }", units)
                .WithMatch(M6World.Pack(new[] { new ControlPointDef(1, enemy, SimFix.FromInt(8)) }, think: 1));
            var w = new M6World(recipe, A1.Map());
            for (int tick = 1; tick <= 2; tick++)
            {
                w.Step();
                int active = 0;
                for (ulong key = 1; key <= 2; key++)
                {
                    MoveIntent intent = w.Read<MoveIntent>(key);
                    if (!intent.Active) continue;
                    active++;
                    Assert.That(intent.Target, Is.EqualTo(enemy));
                }
                Assert.That(active, Is.EqualTo(1), "Only one chase may reserve the guard slot at tick " + tick);
            }
        }
    }
}
