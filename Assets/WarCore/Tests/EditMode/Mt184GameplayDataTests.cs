using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class Mt184GameplayDataTests
    {
        [Test]
        public void PipelineRejectsModifierCountAboveCapacity()
        {
            Assert.Throws<SimBoundaryException>(() => new GameplayPipelineDef(3,new GameplayDamageSpec(1),
                new GameplayModifier[GameplayMath.MaxModifiers+1]));
        }

        [Test]
        public void FindDefenseReturnsEntryBelowMidpoint()
        {
            var archetype=new GameplayArchetypeDef(4,new ulong[] { 1 },new[] {
                new GameplayDefenseDef(3,300),new GameplayDefenseDef(1,100),new GameplayDefenseDef(2,200) });
            Assert.That(archetype.FindDefense(1),Is.EqualTo(0));
        }
    }
}
