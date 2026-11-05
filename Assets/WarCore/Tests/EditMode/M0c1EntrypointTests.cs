using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture, Category("M0c1"), Category("M0c1Mutation")]
    public sealed class M0c1EntrypointTests
    {
        private sealed class NewMode : IRetainedGameplayMode
        {
            public bool IsFinished=>false;
            public string StateTypeId=>"test.new-mode";
            public int StateVersion=>1;
            public void Start(SimContext c){}
            public void Step(SimContext c){}
            public void WriteState(StateHash h){}
            public void SaveState(SimStateWriter w){}
            public void LoadState(SimStateReader r){}
        }
        [TestCase(0),TestCase(1),TestCase(2)]
        public void OldWorldsRejectNewSystemsThroughBothRegistrationPaths(int kind)
        {
            var sim=kind==2?Simulation.CreateRetainedWorld(1):new Simulation(1,identityMode:kind==0?IdentityMode.Legacy:IdentityMode.StableWorld);
            var system=new M0c1Lifecycle(M0c1Fixture.Parent);
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.RegisterGameplay("new",system)).Message,Does.Contain("M0C_RETAINED_REQUIRED"));
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Register("new",system)).Message,Does.Contain("M0C_RETAINED_REQUIRED"));
        }
        [TestCase(0),TestCase(1),TestCase(2)]
        public void OldWorldsRejectNewModesBeforeInitialization(int kind)
        {
            Assert.That(Assert.Throws<SimBoundaryException>(()=>new Simulation(1,new NewMode(),identityMode:kind==0?IdentityMode.Legacy:IdentityMode.StableWorld,
                retainedWorld:kind==2?new RetainedWorldOptions():null)).Message,Does.Contain("M0C_RETAINED_REQUIRED"));
        }
        [Test]
        public void PublicSurfaceCannotConstructOrRegisterCompatibilityWorlds()
        {
            Assert.That(typeof(Simulation).GetConstructors(),Is.Empty);Assert.That(typeof(EntityStore).GetConstructors(),Is.Empty);
            foreach(string name in new[]{"Register","StateHash","StateHashV2","WriteState","WriteStateV2","WriteAuthoritativeState","WriteAuthoritativeStateV2","StateHashStable","SaveStableSnapshot","RestoreStableSnapshot","SaveSnapshot","RestoreSnapshot"})
                Assert.That(typeof(Simulation).GetMethod(name,BindingFlags.Public|BindingFlags.Instance),Is.Null,name);
            int factories=0;
            foreach(var method in typeof(Simulation).GetMethods(BindingFlags.Public|BindingFlags.Static))
                if(method.Name=="CreateRetainedWorld")
                {factories++;Assert.That(method.GetParameters()[1].ParameterType,Is.EqualTo(typeof(PlacedCatalog)));Assert.That(method.GetParameters()[1].IsOptional,Is.False);}
            Assert.That(factories,Is.EqualTo(1));
            var sim=M0c1Fixture.Create(out _);
            Assert.Throws<SimBoundaryException>(()=>((IStateHash)sim).WriteState(new StateHash()));
        }
        [TestCase("new Sim(1);")]
        [TestCase("new Sim(1,null,\"x\",null,null,null,(IdentityMode)1);")]
        [TestCase("using Alias = WarCore.Sim.Sim; new Alias(1);")]
        [TestCase("Sim value = new(1);")]
        [TestCase("Sim Build() => new(1);")]
        [TestCase("new EntityStore(4);")]
        [TestCase("class NewFeature : ISimSystem { }")]
        [TestCase("class NewMode : IMatchMode { }")]
        public void SourceGuardsRejectBypassesAndCompatibilityFileHiding(string source)
        {
            Assert.That(RetainedEntrypointSourceRules.Violations(source,"Content/NewFeature.cs"),Is.Not.Empty);
            if(!source.Contains("class "))
                foreach(string file in new[]{"Sim/RetainedWorld.cs","Sim/StableSimState.cs","Sim/PlacedWorld.cs"})
                    Assert.That(RetainedEntrypointSourceRules.Violations(source,file),Is.Not.Empty,file);
        }
        [Test]
        public void QueryPhaseRestoreFailsBeforeMutation()
        {
            var sim=M0c1Fixture.Create(out _,0);sim.Entities.Destroy(sim.Entities.Create(M0c1Fixture.Parent));sim.Step(default);
            // A separate query registration receives the same context but cannot clear the bit.
            var query=new QueryRestore();sim=Simulation.CreateRetainedWorld(1,sim.Placed.Catalog,
                content:M0c1Package.Ranged.Manifest(),initial:new InitialPlacedState(sim.Placed.Catalog.Digest,new[]{0}));
            sim.RegisterGameplay("query",query,SimSystemPhase.Queries);
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Step(default)).Message,Does.Contain("M0C_RESTORE_PHASE"));
            Assert.That(sim.Placed.IsDestroyed(M0c1Fixture.Parent),Is.True);
        }
        private sealed class QueryRestore : IRetainedGameplaySystem
        {
            public int Order=>20;
            public string StateTypeId=>"test.query-restore";
            public int StateVersion=>1;
            public void Step(SimContext c)=>c.RestorePlaced(M0c1Fixture.Parent);
            public void WriteState(StateHash h){}
            public void SaveState(SimStateWriter w){}
            public void LoadState(SimStateReader r){}
        }
    }
}
