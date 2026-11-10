using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class Mt123GameplaySchemaTests
    {
        [Test]
        public void ConstantResourceRangeIsAcceptedAndPreserved()
        {
            GameplaySchema schema=null;
            Assert.DoesNotThrow(() => schema=new GameplaySchema(
                new[] { new GameplayResourceDef(1,"constant",7,7,7) },
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new GameplayArchetypeDef[0],
                new GameplayCapacities(1,1,1,1),"critical"));
            Assert.That(schema.ResourceCount,Is.EqualTo(1));
            Assert.That(schema.ResourceAt(0).LowerRaw,Is.EqualTo(7));
            Assert.That(schema.ResourceAt(0).UpperRaw,Is.EqualTo(7));
            Assert.That(schema.ResourceAt(0).InitialRaw,Is.EqualTo(7));
        }
    }
}
