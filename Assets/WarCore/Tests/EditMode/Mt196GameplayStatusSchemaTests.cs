using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.M0c2S2Fixture;

namespace WarCore.Tests
{
    [TestFixture,Category("Mt196GameplayStatusSchema")]
    public sealed class Mt196GameplayStatusSchemaTests
    {
        [TestCase(false),TestCase(true)]
        public void StatusDefinitionsAndCapacityMustBothBePresentOrAbsent(bool hasDefinitions)
        {
            var definitions=hasDefinitions
                ?new[]{new GameplayStatusDef(1,"status",GameplayStatusDuration.Infinite)}
                :new GameplayStatusDef[0];
            var error=Assert.Throws<SimBoundaryException>(()=>Schema(statuses:definitions,capacity:hasDefinitions?0:1));
            Assert.That(error.Message,Is.EqualTo("M0C_STATUS_CAPACITY"));
        }

        [Test] public void DuplicateStatusNamesWithDistinctKeysAreRejected()
        {
            var definitions=new[]{new GameplayStatusDef(1,"duplicate",GameplayStatusDuration.Infinite),
                new GameplayStatusDef(2,"duplicate",GameplayStatusDuration.Infinite)};
            var error=Assert.Throws<SimBoundaryException>(()=>Schema(statuses:definitions,capacity:1));
            Assert.That(error.Message,Is.EqualTo("M0C_STATUS_NAME"));
        }

        [TestCase(-1),TestCase(4)]
        public void UnsupportedStatusModifierStageIsRejected(int stage)
        {
            var definition=new GameplayStatusDef(1,"modifier",GameplayStatusDuration.Infinite,
                modifiers:new[]{new GameplayStatusModifierDef(1,1,(GameplayModifierStage)stage,0)});
            var error=Assert.Throws<SimBoundaryException>(()=>Schema(statuses:new[]{definition},capacity:1));
            Assert.That(error.Message,Is.EqualTo("M0C_STATUS_MODIFIER_STAGE"));
        }
    }
}
