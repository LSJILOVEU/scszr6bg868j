using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class Mt117GameplayDataTests
    {
        [Test]
        public void PipelineAcceptsMaximumModifierCount()
        {
            var modifiers=new GameplayModifier[65535];
            for(int i=0;i<modifiers.Length;i++)
                modifiers[i]=new GameplayModifier(GameplayModifierStage.PreFlat,0,new GameplayModifierKey(default,0,0,(ulong)i));
            GameplayPipelineDef pipeline=null;
            Assert.DoesNotThrow(() => pipeline=new GameplayPipelineDef(3,new GameplayDamageSpec(20),modifiers));
            Assert.That(pipeline.ModifierCount,Is.EqualTo(65535));
        }

        [Test]
        public void PipelineSortsSameStageModifiersByKey()
        {
            var pipeline=new GameplayPipelineDef(3,new GameplayDamageSpec(20),new[] {
                new GameplayModifier(GameplayModifierStage.PreFlat,7,new GameplayModifierKey(default,0,0,9)),
                new GameplayModifier(GameplayModifierStage.PreFlat,5,new GameplayModifierKey(default,0,0,1)) });
            Assert.That(pipeline.ModifierAt(0).Key.ModifierKey,Is.EqualTo(1UL));
            Assert.That(pipeline.ModifierAt(1).Key.ModifierKey,Is.EqualTo(9UL));
        }

        [Test]
        public void PipelineSortsByStageBeforeKey()
        {
            var pipeline=new GameplayPipelineDef(3,new GameplayDamageSpec(20),new[] {
                new GameplayModifier(GameplayModifierStage.Product,32768,new GameplayModifierKey(default,0,0,1)),
                new GameplayModifier(GameplayModifierStage.PreFlat,7,new GameplayModifierKey(default,0,0,9)) });
            Assert.That(pipeline.ModifierAt(0).Stage,Is.EqualTo(GameplayModifierStage.PreFlat));
            Assert.That(pipeline.ModifierAt(1).Stage,Is.EqualTo(GameplayModifierStage.Product));
        }
    }
}
