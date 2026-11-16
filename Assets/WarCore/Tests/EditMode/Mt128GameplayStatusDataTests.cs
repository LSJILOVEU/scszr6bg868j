using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt128GameplayStatusDataTests
    {
        [Test] public void DefinitionAcceptsMaximumModifierCount()
        {
            var modifiers=new GameplayStatusModifierDef[65535];
            GameplayStatusDef definition=null;
            Assert.DoesNotThrow(()=>definition=new GameplayStatusDef(1,"capacity",GameplayStatusDuration.Infinite,modifiers:modifiers));
            Assert.That(definition.ModifierCount,Is.EqualTo(65535));
        }

        [Test] public void DefinitionSortsModifiersByKey()
        {
            var definition=new GameplayStatusDef(1,"modifiers",GameplayStatusDuration.Infinite,modifiers:new[]{
                new GameplayStatusModifierDef(2,1,GameplayModifierStage.PreFlat,3),
                new GameplayStatusModifierDef(1,1,GameplayModifierStage.PreFlat,7)});
            Assert.That(definition.ModifierAt(0).Key,Is.EqualTo(1UL));
            Assert.That(definition.ModifierAt(1).Key,Is.EqualTo(2UL));
        }

        [Test] public void DefinitionSortsPeriodicActionsByOrdinal()
        {
            var definition=new GameplayStatusDef(1,"periodic",GameplayStatusDuration.Infinite,
                periodTicks:1,firstPulseDelayTicks:1,periodicActions:new[]{
                    GameplayPeriodicAction.Damage(2,1,3),GameplayPeriodicAction.Damage(1,1,7)});
            Assert.That(definition.PeriodicActionAt(0).Ordinal,Is.EqualTo(1U));
            Assert.That(definition.PeriodicActionAt(1).Ordinal,Is.EqualTo(2U));
        }
    }
}
