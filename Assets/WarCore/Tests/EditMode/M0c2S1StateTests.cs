using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S1State"),Category("M0c2S1")]
    public sealed class M0c2S1StateTests
    {
        [Test] public void BindingInitializesNamedResourceFromFrozenData()
        {
            var content=new StableContentManifest(0);
            var key=StableEntityKey.Placed(17,1);
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"health",-10,100,73)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(1,new[]{1UL})},new GameplayCapacities(2,4,8,16),"test.critical");
            var sim=Simulation.CreateRetainedWorld(41,new PlacedCatalog(content,new PlacedCatalogEntry(17,1,1)),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(key),1);
            Assert.That(sim.GameplayResource(key.Id,1),Is.EqualTo(73),"Resource initialization uses frozen data.");
        }

        [Test] public void DamageBeforeHealingPublishesFinalFactWithoutDestroy()
        {
            var sim=M0c2S1Fixture.Create();
            sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:1));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(50*M0c2S1Fixture.S));
            Assert.That(sim.Entities.TryResolve(M0c2S1Fixture.Target.Id,out _),Is.True);
            var damage=sim.ReadStableEvent(1,0);var heal=sim.ReadStableEvent(1,1);var final=sim.ReadStableEvent(1,2);
            Assert.That(damage.Type,Is.EqualTo(GameplayEvents.Damage));Assert.That(heal.Type,Is.EqualTo(GameplayEvents.Resource));
            Assert.That(final.Type,Is.EqualTo(GameplayEvents.FinalLife));Assert.That(final.Tag,Is.EqualTo(0));
            Assert.That(final.Payload.Magnitude.Raw,Is.EqualTo(50*M0c2S1Fixture.S));
        }
        [Test] public void InsufficientMultiResourceCostHasNoPartialDebit()
        {
            var sim=M0c2S1Fixture.CreateWith(out S1Script script);
            sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:2));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.InsufficientResource));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(80*M0c2S1Fixture.S));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,2),Is.EqualTo(10*M0c2S1Fixture.S));
        }
        [Test] public void ProducerTokenOrdersNoncommutingChanges()
        {
            var sim=M0c2S1Fixture.Create();sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:3));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(0));
        }
        [Test] public void CapacityAtLimitAcceptsAndAboveLimitRejects()
        {
            var sim=M0c2S1Fixture.CreateWith(out S1Script script,commands:1);
            Assert.Throws<SimBoundaryException>(()=>sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:4)));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(80*M0c2S1Fixture.S));
            var fits=M0c2S1Fixture.CreateWith(out _,commands:2);
            fits.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:4));
            Assert.That(fits.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(80*M0c2S1Fixture.S-2));
        }
        [TestCase(false),TestCase(true)] public void SameLogicalKeysReverseCreationAndSubmissionKeepEveryTickHash(bool packageB)
        {
            var a=M0c2S1Fixture.Create(packageB);var b=M0c2S1Fixture.Create(packageB,true);
            Assert.That(a.StateHashRetained(),Is.EqualTo(b.StateHashRetained()));
            Assert.That(a.SaveRetainedSnapshot(),Is.EqualTo(b.SaveRetainedSnapshot()));
            for(int i=0;i<40;i++)Assert.That(M0c2S1Fixture.Step(a),Is.EqualTo(M0c2S1Fixture.Step(b)));
        }
        [Test] public void SourceStableIdIsTheUnsignedSettlementOrder()
        {
            var sim=M0c2S1Fixture.Create();M0c2S1Fixture.Step(sim);
            Assert.That(sim.ReadStableEvent(1,0).Source.Id,Is.EqualTo(M0c2S1Fixture.Low));
            Assert.That(sim.ReadStableEvent(1,1).Source.Id,Is.EqualTo(M0c2S1Fixture.High));
        }
        [Test] public void RegenerationRunsBeforeAdmissionOnDueTick()
        {
            var sim=M0c2S1Fixture.Create();for(int i=0;i<3;i++)M0c2S1Fixture.Step(sim);
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Low,2),Is.EqualTo(7*M0c2S1Fixture.S));
            M0c2S1Fixture.Step(sim);Assert.That(sim.GameplayResource(M0c2S1Fixture.Low,2),Is.EqualTo(9*M0c2S1Fixture.S));
        }
        [Test] public void GameplayOwnersRejectColdStorage()
        {
            var sim=M0c2S1Fixture.Create();
            Assert.Throws<SimBoundaryException>(()=>sim.Entities.MakeCold(sim.Entities.Resolve(new EntityLink(M0c2S1Fixture.Target.Id))));
        }
        [TestCase(false),TestCase(true)] public void EveryCheckpointRestoresWithReverseSlotsAndContinues(bool packageB)
        {
            var baseline=M0c2S1Fixture.Create(packageB);
            var bytes=new byte[301][];var hashes=new ulong[601];
            hashes[0]=baseline.StateHashRetained();bytes[0]=baseline.SaveRetainedSnapshot();
            for(int tick=1;tick<=600;tick++){hashes[tick]=M0c2S1Fixture.Step(baseline);if(tick<=300)bytes[tick]=baseline.SaveRetainedSnapshot();}
            for(int checkpoint=0;checkpoint<=300;checkpoint++)
            {
                var restored=M0c2S1Fixture.Create(packageB,true);restored.RestoreRetainedSnapshot(bytes[checkpoint],true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(hashes[checkpoint]),"restore checkpoint "+checkpoint);
                for(int tick=checkpoint+1;tick<=checkpoint+300;tick++)
                    Assert.That(M0c2S1Fixture.Step(restored),Is.EqualTo(hashes[tick]),"continued tick "+tick);
            }
            TestContext.WriteLine("S1 checkpoint|package="+(packageB?"B":"A")+"; checkpoints=301; continuation=300; matches=90300");
        }

        [Test] public void CapacityReservationRejectsWholeBatchBeforeResourceDebit()
        {
            var sim=M0c2S1Fixture.CreateWith(out S1Script script,commands:1);
            sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:5));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.CommandCapacity));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,2),Is.EqualTo(10*M0c2S1Fixture.S));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(80*M0c2S1Fixture.S));
        }
        [Test] public void TargetDefenseIsReadByArchetypeForTheSameDamageType()
        {
            var a=M0c2S1Fixture.CreateWith(out _,armorRaw:M0c2S1Fixture.S/2);
            var b=M0c2S1Fixture.CreateWith(out _,armorRaw:M0c2S1Fixture.S);
            M0c2S1Fixture.Step(a);M0c2S1Fixture.Step(b);
            Assert.That(a.ReadStableEvent(1,0).Payload.Magnitude.Raw,Is.EqualTo(3*b.ReadStableEvent(1,0).Payload.Magnitude.Raw));
        }
        private static ulong RandomHash(Simulation sim) { var h=new StateHash();sim.Rng.WriteState(h);return h.Value; }
        [Test] public void RejectedTargetAndDisabledCriticalDoNotDrawRandom()
        {
            var registry=new SimRandom(41);var absent=new RandomStreamId("not-registered");
            Assert.Throws<SimBoundaryException>(()=>registry.NextRegisteredInt(absent,65536));
            registry.Stream("not-registered");Assert.That(registry.NextRegisteredInt(absent,65536),Is.InRange(0,65535));
            var sim=M0c2S1Fixture.CreateWith(out S1Script script);ulong before=RandomHash(sim);
            sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:6));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.TargetLifeMismatch));Assert.That(RandomHash(sim),Is.EqualTo(before));
            sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:1));Assert.That(RandomHash(sim),Is.EqualTo(before));
        }
        [Test] public void DuplicateApplicationAcrossPrimitivesIsRejectedBeforeSettlement()
        {
            var sim=M0c2S1Fixture.Create();
            Assert.Throws<SimBoundaryException>(()=>sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:7)));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(80*M0c2S1Fixture.S));
        }
        [Test] public void Q0AdmissionRejectsNoncanonicalSourceOrder()
        {
            var sim=M0c2S1Fixture.Create();
            Assert.Throws<SimBoundaryException>(()=>sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:8)));
        }
        [Test] public void LethalDamageOnlyPublishesThresholdFact()
        {
            var sim=M0c2S1Fixture.Create();sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:9));
            Assert.That(sim.Entities.TryResolve(M0c2S1Fixture.Target.Id,out _),Is.True);
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(-100*M0c2S1Fixture.S));
            Assert.That(sim.ReadStableEvent(1,1).Type,Is.EqualTo(GameplayEvents.FinalLife));Assert.That(sim.ReadStableEvent(1,1).Tag,Is.EqualTo(1));
        }
        [Test] public void EveryStepLeafSummariesMatchIndependentSnapshotRows()
        {
            var sim=M0c2S1Fixture.Create();
            for(int tick=0;tick<12;tick++)
            {
                if(tick>0)M0c2S1Fixture.Step(sim);
                var bytes=sim.SaveRetainedSnapshot();int start=GameplayStart(bytes,sim.Entities.Gameplay);
                int at=start+40;
                for(int tag=1;tag<=9;tag++)
                {
                    Assert.That(System.BitConverter.ToUInt64(bytes,at),Is.EqualTo(tag));at+=8;
                    int count=(int)System.BitConverter.ToUInt64(bytes,at);at+=8;ulong sum=0;
                    for(int row=0;row<count;row++)
                    {
                        int length=(int)System.BitConverter.ToUInt64(bytes,at);at+=8;
                        ulong hash=14695981039346656037UL;
                        for(int b=0;b<8;b++)hash=unchecked((hash^((ulong)tag>>(8*b)&255))*1099511628211UL);
                        for(int b=0;b<length;b++)hash=unchecked((hash^bytes[at+b])*1099511628211UL);
                        hash=unchecked((hash^(hash>>30))*0xBF58476D1CE4E5B9UL);hash=unchecked((hash^(hash>>27))*0x94D049BB133111EBUL);hash^=hash>>31;
                        sum=unchecked(sum+hash);at+=length;
                    }
                    Assert.That(System.BitConverter.ToUInt64(bytes,at),Is.EqualTo(count));at+=8;
                    Assert.That(System.BitConverter.ToUInt64(bytes,at),Is.EqualTo(sum),"canonical snapshot row leaf tag "+tag);at+=8;
                }
                Assert.That(at,Is.EqualTo(bytes.Length));
                var restored=M0c2S1Fixture.Create(reverse:true);restored.RestoreRetainedSnapshot(bytes,true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
            }
        }
        private static int GameplayStart(byte[] bytes,GameplayState state)
        {
            var w=new SimStateWriter(null,materialize:true);state.Save(w);return bytes.Length-w.ToArray().Length;
        }
    }
}
