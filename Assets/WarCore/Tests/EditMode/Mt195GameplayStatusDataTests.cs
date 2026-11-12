using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt195GameplayStatusDataTests
    {
        [Test]
        public void DescendingModifiersPreserveBothEntriesInKeyOrder()
        {
            var first=new GameplayStatusModifierDef(1,3,GameplayModifierStage.PreFlat,7);
            var second=new GameplayStatusModifierDef(2,5,GameplayModifierStage.Product,32768);
            var definition=new GameplayStatusDef(1,"modifiers",GameplayStatusDuration.Infinite,
                modifiers:new[]{second,first});

            Assert.That(definition.ModifierCount,Is.EqualTo(2));
            Assert.That(definition.ModifierAt(0),Is.EqualTo(first));
            Assert.That(definition.ModifierAt(1),Is.EqualTo(second));
        }

        [Test]
        public void DescendingPeriodicActionsPreserveBothEntriesInOrdinalOrder()
        {
            var first=GameplayPeriodicAction.ChangeResource(1,3,7);
            var second=GameplayPeriodicAction.Damage(2,5,11);
            var definition=new GameplayStatusDef(1,"periodic",GameplayStatusDuration.Infinite,
                periodTicks:2,firstPulseDelayTicks:1,periodicActions:new[]{second,first});

            Assert.That(definition.PeriodicActionCount,Is.EqualTo(2));
            Assert.That(definition.PeriodicActionAt(0),Is.EqualTo(first));
            Assert.That(definition.PeriodicActionAt(1),Is.EqualTo(second));
        }

        [Test]
        public void InstanceKeyWithoutGroupSourceSortsBeforeKeyWithGroupSource()
        {
            var withoutSource=new GameplayStatusInstanceKey(new StableId(1),2,false,default,7);
            var withSource=new GameplayStatusInstanceKey(new StableId(1),2,true,default,7);

            Assert.That(withoutSource.CompareTo(withSource),Is.EqualTo(-1));
            Assert.That(withSource.CompareTo(withoutSource),Is.EqualTo(1));
        }
    }
}
