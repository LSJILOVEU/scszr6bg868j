using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("Mt116GameplayCommands")]
    public sealed class Mt116GameplayCommandsTests
    {
        [Test]
        public void ApplyKeyRejectsProducerKindThree()
        {
            var source=StableEntityKey.Placed(81,1).Id;
            Assert.Throws<SimBoundaryException>(()=>new GameplayApplyKey(source,1,3,source,1,0));
        }

        [Test]
        public void DamageActionHasKindZero()
        {
            var source=StableEntityKey.Placed(81,1).Id;
            var key=new GameplayApplyKey(source,1,0,source,1,0);
            var action=GameplayAction.Damage(new GameplayStamp(source),key,1,7);
            Assert.That(action.Command.Kind,Is.EqualTo(0));
        }
    }
}
