using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2S1")]
    public sealed class GameplaySchemaTests
    {
        private static GameplaySchema Schema(GameplayResourceDef[] resources=null, GameplayDamageTypeDef[] damageTypes=null,
            GameplayPipelineDef[] pipelines=null, GameplayArchetypeDef[] archetypes=null, GameplayCapacities? capacities=null,
            string stream="critical")
        {
            return new GameplaySchema(resources ?? new[] { new GameplayResourceDef(1,"life",-10,100,80,isLife:true) },
                damageTypes ?? new[] { new GameplayDamageTypeDef(2,"impact",1,3) },
                pipelines ?? new[] { new GameplayPipelineDef(3,new GameplayDamageSpec(20)) },
                archetypes ?? new[] { new GameplayArchetypeDef(4,new ulong[] { 1 }) },
                capacities ?? new GameplayCapacities(2,4,8,16),stream);
        }
        [Test]
        public void IllegalResourceRangeIsRejectedAtFrozenSchemaBoundary()
        {
            var resources=new[] { new GameplayResourceDef(1,"life",10,9,10) };
            Assert.Throws<SimBoundaryException>(() => new GameplaySchema(resources,
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[] { new GameplayArchetypeDef(2,new ulong[] { 1 }) },new GameplayCapacities(1,1,4,4),"critical"));
        }

        [TestCase(-140737488355328L,100,0,0,0,true,0)]
        [TestCase(0,140737488355328L,0,0,0,true,0)]
        [TestCase(0,100,-1,0,0,true,0)]
        [TestCase(0,100,101,0,0,true,0)]
        [TestCase(0,100,0,-1,1,true,0)]
        [TestCase(0,100,0,140737488355328L,1,true,0)]
        [TestCase(0,100,0,1,0,true,0)]
        [TestCase(0,100,0,1,-1,true,0)]
        [TestCase(0,100,0,1,1,true,-1)]
        [TestCase(0,100,0,1,1,true,101)]
        [TestCase(0,100,0,1,1,false,1)]
        public void InvalidResourceDefinitionsRejectBeforeWorldCreation(long lower,long upper,long initial,
            long regen,int period,bool life,long threshold)
        {
            Assert.Throws<SimBoundaryException>(() => Schema(resources:new[] {
                new GameplayResourceDef(1,"life",lower,upper,initial,regen,period,isLife:life,lifeThresholdRaw:threshold) }));
        }

        [Test]
        public void AllLegalResourceEndpointsAreAccepted()
        {
            long m=140737488355327L;
            Assert.DoesNotThrow(() => Schema(resources:new[] {
                new GameplayResourceDef(1,"life",-m,m,-m,m,int.MaxValue,isLife:true,lifeThresholdRaw:m) }));
            Assert.DoesNotThrow(() => Schema(resources:new[] {
                new GameplayResourceDef(1,"life",-m,m,m,0,1,isLife:true,lifeThresholdRaw:-m) }));
        }

        [Test]
        public void CapacityDomainIncludesMaximumAndRejectsItsUpperNeighbour()
        {
            Assert.DoesNotThrow(() => Schema(capacities:new GameplayCapacities(1048575,1048575,1048575,1048575)));
            Assert.Throws<SimBoundaryException>(() => Schema(capacities:new GameplayCapacities(1048576,4,8,16)));
            Assert.Throws<SimBoundaryException>(() => Schema(capacities:new GameplayCapacities(2,1048576,8,16)));
            Assert.Throws<SimBoundaryException>(() => Schema(capacities:new GameplayCapacities(2,4,1048576,16)));
            Assert.Throws<SimBoundaryException>(() => Schema(capacities:new GameplayCapacities(2,4,8,1048576)));
            Assert.Throws<SimBoundaryException>(() => Schema(capacities:new GameplayCapacities(-1,4,8,16)));
            Assert.Throws<SimBoundaryException>(() => Schema(capacities:new GameplayCapacities(2,0,8,16)));
        }

        [Test]
        public void MissingReferencesAndDuplicateKeysAreRejected()
        {
            Assert.Throws<SimBoundaryException>(() => Schema(damageTypes:new[] { new GameplayDamageTypeDef(2,"impact",9,3) }));
            Assert.Throws<SimBoundaryException>(() => Schema(damageTypes:new[] { new GameplayDamageTypeDef(2,"impact",1,9) }));
            Assert.Throws<SimBoundaryException>(() => Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 9 }) }));
            Assert.Throws<SimBoundaryException>(() => Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1,1 }) }));
            Assert.Throws<SimBoundaryException>(() => Schema(resources:new[] {
                new GameplayResourceDef(1,"first",0,100,0),new GameplayResourceDef(1,"second",0,100,0) }));
            Assert.Throws<SimBoundaryException>(() => Schema(resources:new[] {
                new GameplayResourceDef(1,"same",0,100,0),new GameplayResourceDef(2,"same",0,100,0) }));
            Assert.Throws<SimBoundaryException>(() => Schema(resources:new[] {
                new GameplayResourceDef(1,"first",0,100,0,isLife:true),new GameplayResourceDef(5,"second",0,100,0,isLife:true) },
                archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1,5 }) }));
        }

        [Test]
        public void UnsupportedPipelineAndDuplicateModifiersAreRejected()
        {
            Assert.Throws<SimBoundaryException>(() => Schema(pipelines:new[] {
                new GameplayPipelineDef(3,new GameplayDamageSpec(1,(GameplayDamageStages)128)) }));
            var modifier=new GameplayModifier(GameplayModifierStage.Product,65536,new GameplayModifierKey(default,0,0,1));
            Assert.Throws<SimBoundaryException>(() => Schema(pipelines:new[] {
                new GameplayPipelineDef(3,new GameplayDamageSpec(1),new[] { modifier,modifier }) }));
        }

        [Test]
        public void CanonicalSchemaCopiesAndSortsAllCallerOwnedArrays()
        {
            var resources=new[] { new GameplayResourceDef(9,"energy",0,100,50),new GameplayResourceDef(1,"life",0,100,80) };
            var keys=new ulong[] { 9,1 };
            var modifiers=new[] {
                new GameplayModifier(GameplayModifierStage.Product,32768,new GameplayModifierKey(default,0,0,9)),
                new GameplayModifier(GameplayModifierStage.PreFlat,7,new GameplayModifierKey(default,0,0,1)) };
            var pipelines=new[] { new GameplayPipelineDef(3,new GameplayDamageSpec(20),modifiers) };
            var archetypes=new[] { new GameplayArchetypeDef(4,keys) };
            var first=Schema(resources:resources,pipelines:pipelines,archetypes:archetypes);
            var second=Schema(resources:new[] { resources[1],resources[0] },pipelines:pipelines,
                archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1,9 }) });
            Assert.That(first.RulesDigest,Is.EqualTo(second.RulesDigest));
            ulong digest=first.RulesDigest;
            resources[0]=default; keys[0]=200; modifiers[0]=default; pipelines[0]=null; archetypes[0]=null;
            Assert.That(first.RulesDigest,Is.EqualTo(digest));
            Assert.That(first.ResourceAt(first.FindResource(9)).Name,Is.EqualTo("energy"));
            Assert.That(first.ArchetypeAt(0).ResourceKeyAt(1),Is.EqualTo(9UL));
            Assert.That(first.PipelineAt(0).ModifierAt(1).MagnitudeRaw,Is.EqualTo(32768));
            Assert.That(first.FindDamageType(2),Is.EqualTo(0));
            Assert.That(first.FindResource(123),Is.EqualTo(-1));
        }

        [Test]
        public void RulesDigestBindsParametersNamesStreamAndCapacity()
        {
            GameplaySchema baseline=Schema();
            Assert.That(Schema(resources:new[] { new GameplayResourceDef(1,"life",-10,100,79,isLife:true) }).RulesDigest,Is.Not.EqualTo(baseline.RulesDigest));
            Assert.That(Schema(resources:new[] { new GameplayResourceDef(1,"\u751f\u547d",-10,100,80,isLife:true) }).RulesDigest,Is.Not.EqualTo(baseline.RulesDigest));
            Assert.That(Schema(stream:"critical-other").RulesDigest,Is.Not.EqualTo(baseline.RulesDigest));
            GameplaySchema capacity=Schema(capacities:new GameplayCapacities(2,4,9,16));
            Assert.That(capacity.CapacityDigest,Is.Not.EqualTo(baseline.CapacityDigest));
            Assert.That(capacity.RulesDigest,Is.Not.EqualTo(baseline.RulesDigest));
            Assert.Throws<SimBoundaryException>(() => Schema(stream:"\ud800"));
        }

        [Test]
        public void TargetDefenseParametersAreFrozenAndBoundByRulesDigest()
        {
            var defenses=new[] { new GameplayDefenseDef(2,32768) };
            var protectedSchema=Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1 },defenses) });
            var exposedSchema=Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1 },
                new[] { new GameplayDefenseDef(2,0) }) });
            Assert.That(protectedSchema.RulesDigest,Is.Not.EqualTo(exposedSchema.RulesDigest));
            defenses[0]=new GameplayDefenseDef(2,65536);
            GameplayArchetypeDef archetype=protectedSchema.ArchetypeAt(0);
            Assert.That(archetype.DefenseAt(archetype.FindDefense(2)).ResistanceRaw,Is.EqualTo(32768));
            Assert.That(archetype.FindDefense(7),Is.EqualTo(-1));
        }

        [Test]
        public void OnePipelineReadsDifferentFrozenTargetDefenses()
        {
            var pipeline=new GameplayPipelineDef(3,new GameplayDamageSpec(100,GameplayDamageStages.Resistance));
            long diagnostics=0;
            Assert.That(pipeline.Evaluate(100,0,0,ref diagnostics).AmountRaw,Is.EqualTo(100));
            Assert.That(pipeline.Evaluate(100,0,32768,ref diagnostics).AmountRaw,Is.EqualTo(50));
            Assert.That(pipeline.Evaluate(100,0,65536,ref diagnostics).AmountRaw,Is.EqualTo(0));
            Assert.That(diagnostics,Is.EqualTo(0));
        }

        [Test]
        public void InvalidTargetDefenseDefinitionsFailDuringSchemaLoading()
        {
            Assert.Throws<SimBoundaryException>(() => Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1 },
                new[] { new GameplayDefenseDef(9,0) }) }));
            Assert.Throws<SimBoundaryException>(() => Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1 },
                new[] { new GameplayDefenseDef(2,-1) }) }));
            Assert.Throws<SimBoundaryException>(() => Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1 },
                new[] { new GameplayDefenseDef(2,65537) }) }));
            Assert.Throws<SimBoundaryException>(() => Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1 },
                new[] { new GameplayDefenseDef(2,0),new GameplayDefenseDef(2,1) }) }));
            Assert.DoesNotThrow(() => Schema(archetypes:new[] { new GameplayArchetypeDef(4,new ulong[] { 1 },
                new[] { new GameplayDefenseDef(2,65536) }) }));
        }
    }
}
