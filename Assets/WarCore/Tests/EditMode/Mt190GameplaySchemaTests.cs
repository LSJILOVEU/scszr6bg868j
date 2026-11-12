using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class Mt190GameplaySchemaTests
    {
        [Test]
        public void DuplicatePipelineKeysAreRejected()
        {
            Assert.Throws<SimBoundaryException>(() => new GameplaySchema(
                new GameplayResourceDef[0],new GameplayDamageTypeDef[0],
                new[] { new GameplayPipelineDef(3,new GameplayDamageSpec(20)),
                    new GameplayPipelineDef(3,new GameplayDamageSpec(40)) },
                new GameplayArchetypeDef[0],new GameplayCapacities(1,1,1,1),"critical"));
        }

        [Test]
        public void NoticeCapacityBelowResourceCapacityIsRejected()
        {
            Assert.Throws<SimBoundaryException>(() => new GameplaySchema(
                new[] { new GameplayResourceDef(1,"life",0,100,80) },
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new GameplayArchetypeDef[0],new GameplayCapacities(1,2,1,1),"critical"));
        }

        [Test]
        public void FindDamageTypeFindsFirstOfThreeKeys()
        {
            var schema=new GameplaySchema(
                new[] { new GameplayResourceDef(1,"life",0,100,80) },
                new[] { new GameplayDamageTypeDef(8,"heat",1,3),
                    new GameplayDamageTypeDef(2,"impact",1,3),
                    new GameplayDamageTypeDef(5,"cold",1,3) },
                new[] { new GameplayPipelineDef(3,new GameplayDamageSpec(20)) },
                new GameplayArchetypeDef[0],new GameplayCapacities(1,1,1,1),"critical");
            Assert.That(schema.FindDamageType(2),Is.EqualTo(0));
        }
    }
}
