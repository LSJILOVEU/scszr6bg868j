using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2")]
    public sealed class Mt130GameplayStatusSchemaTests
    {
        [Test]
        public void StatusModifierCapacityAcceptsExactly65535()
        {
            var statuses=new[]{new GameplayStatusDef(1,"modifier",GameplayStatusDuration.Infinite,
                modifiers:new[]{new GameplayStatusModifierDef(1,1,GameplayModifierStage.PreFlat,1)})};
            GameplaySchema schema=null;
            Assert.DoesNotThrow(()=>schema=M0c2S2Fixture.Schema(statuses:statuses,capacity:65535));
            Assert.That(schema.StatusAt(0).ModifierCount,Is.EqualTo(1));
            Assert.That(schema.Capacities.Statuses,Is.EqualTo(65535));
        }
    }
}
