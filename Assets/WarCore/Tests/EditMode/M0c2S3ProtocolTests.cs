using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Protocol")]
    public sealed class M0c2S3ProtocolTests
    {
        [TestCase(false),TestCase(true)]
        public void EveryIntermediateCastAndStatusCheckpointContinuesWithIdenticalPerTickHash(bool packageB)
        {
            var snapshots=new byte[301][];var hashes=new ulong[601];Simulation direct=M0c2S3Fixture.Create(packageB);
            snapshots[0]=direct.SaveRetainedSnapshot();hashes[0]=direct.StateHashRetained();
            for(int tick=1;tick<=600;tick++){hashes[tick]=M0c2S3Fixture.Step(direct);if(tick<=300)snapshots[tick]=direct.SaveRetainedSnapshot();}
            for(int checkpoint=0;checkpoint<=300;checkpoint++)
            {
                Simulation restored=M0c2S3Fixture.Create(packageB,true,1);restored.RestoreRetainedSnapshot(snapshots[checkpoint],true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(hashes[checkpoint]),"restore at "+checkpoint);
                for(int follow=1;follow<=300;follow++)
                    Assert.That(M0c2S3Fixture.Step(restored),Is.EqualTo(hashes[checkpoint+follow]),"checkpoint "+checkpoint+" follow "+follow);
            }
            TestContext.WriteLine("S3 checkpoints: package="+(packageB?"B":"A")+" 301 boundaries x 300 continuation ticks; mismatches=0");
        }
        [Test]
        public void ConflictingDuplicatePayloadPermutationsHaveOneCanonicalSnapshotAndAllReject()
        {
            var a=M0c2S3Fixture.Command(1);var b=M0c2S3Fixture.Command(2);
            var first=M0c2S3Fixture.Create();var second=M0c2S3Fixture.Create(reverse:true,slotVariant:1);
            M0c2S3Fixture.Input(first,a,b,a);M0c2S3Fixture.Input(second,b,a,a);
            for(int i=0;i<3;i++)Assert.That(first.GameplayInputResultAt(i).Failure,Is.EqualTo(GameplayAbilityFailure.DuplicateCommand));
            Assert.That(first.StateHashRetained(),Is.EqualTo(second.StateHashRetained()));
            Assert.That(first.SaveRetainedSnapshot(),Is.EqualTo(second.SaveRetainedSnapshot()));
            var restored=M0c2S3Fixture.Create();restored.RestoreRetainedSnapshot(first.SaveRetainedSnapshot(),true);
            restored.Step(default);first.Step(default);Assert.That(restored.StateHashRetained(),Is.EqualTo(first.StateHashRetained()));
        }
        [TestCase("cast-cursor"),TestCase("cast-generation"),TestCase("cast-token-sentinel"),TestCase("cast-phase"),TestCase("cast-entered"),
         TestCase("cast-due"),TestCase("cast-ordinal"),TestCase("cast-target-mode"),TestCase("cast-target-count"),TestCase("cast-source-captured"),
         TestCase("cast-source-attribute-key"),TestCase("cast-source-binding"),TestCase("cooldown-ready"),TestCase("cooldown-key"),
         TestCase("input-order"),TestCase("diagnostic-negative"),TestCase("orphan-cast-task")]
        public void CorruptS3RowsRejectAfterIndependentTableLeafRecalculation(string fault)
        {
            Simulation original=M0c2S3Fixture.Create();
            M0c2S3Fixture.Input(original,M0c2S3Fixture.Command(1),M0c2S3Fixture.Command(1,M0c2S3Fixture.OtherActor));
            var document=new M0c2S2ProtocolTests.Document(original);byte[] cast=document.Tables[5][0];
            ulong owner=M0c2S2ProtocolTests.Word(cast,1);byte[] ownerRow=null;
            foreach(byte[] row in document.Tables[2])if(M0c2S2ProtocolTests.Word(row,1)==owner)ownerRow=row;
            int targets=(int)M0c2S2ProtocolTests.Word(cast,82),source=90+25*targets+48;
            switch(fault)
            {
                case "cast-cursor":M0c2S2ProtocolTests.Put(ownerRow,25,0);break;
                case "cast-generation":M0c2S2ProtocolTests.Put(cast,9,1);break;
                case "cast-token-sentinel":M0c2S2ProtocolTests.Put(cast,17,0x0000FFFFFFFFFFFFUL);break;
                case "cast-phase":M0c2S2ProtocolTests.Put(cast,33,4);break;
                case "cast-entered":M0c2S2ProtocolTests.Put(cast,41,0);break;
                case "cast-due":M0c2S2ProtocolTests.Put(cast,49,4);break;
                case "cast-ordinal":M0c2S2ProtocolTests.Put(cast,57,1UL<<32);break;
                case "cast-target-mode":M0c2S2ProtocolTests.Put(cast,73,0);break;
                case "cast-target-count":M0c2S2ProtocolTests.Put(cast,82,9);break;
                case "cast-source-captured":M0c2S2ProtocolTests.Put(cast,source+25,2);break;
                case "cast-source-attribute-key":M0c2S2ProtocolTests.Put(cast,source+41,999);break;
                case "cast-source-binding":M0c2S2ProtocolTests.Put(cast,source+49+16*3,999);break;
                case "cooldown-ready":M0c2S2ProtocolTests.Put(document.Tables[4][0],25,0);break;
                case "cooldown-key":M0c2S2ProtocolTests.Put(document.Tables[4][0],17,999);break;
                case "input-order":
                    var input=document.Tables[1][0];var chunk=new byte[98];Array.Copy(input,16,chunk,0,98);Array.Copy(input,114,input,16,98);Array.Copy(chunk,0,input,114,98);break;
                case "diagnostic-negative":M0c2S2ProtocolTests.Put(document.Tables[8][0],48+8,ulong.MaxValue);break;
                case "orphan-cast-task":document.Tables[5].RemoveAt(0);break;
            }
            var restored=M0c2S3Fixture.Create();Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true),fault);
            Assert.Throws<InvalidOperationException>(()=>restored.Step(default),fault+" faulted");
        }
        [Test]
        public void IndependentProtocolRowsMatchAdmissionActiveAndRecoveryVectors()
        {
            VectorJson cases=VectorJson.Load("m0c2-s3-ability-v6-v8.json")["cases"];
            for(int i=0;i<cases.Count;i++)
            {
                var item=cases[i];Simulation sim=VectorWorld();
                M0c2S3Fixture.Input(sim,new GameplayInputCommand(new GameplayStamp(VectorActor.Id),7,GameplayInputKind.Activate,1));
                while(sim.Clock.Tick<item["tick"].Integer)sim.Step(default);
                var document=new M0c2S2ProtocolTests.Document(sim);
                var sectionWriter=new SimStateWriter(sim.Entities,materialize:true);sim.Entities.Gameplay.Save(sectionWriter);
                Assert.That(sectionWriter.ToArray(),Is.EqualTo(Hex(item["gameplay_snapshot"].Text)),"complete snapshot "+item["name"].Text);
                var hashWriter=new SimStateWriter(sim.Entities,materialize:true);sim.Entities.Gameplay.WriteHash(hashWriter);
                Assert.That(hashWriter.ToArray(),Is.EqualTo(Hex(item["gameplay_hash"].Text)),"complete hash "+item["name"].Text);
                Assert.That(sim.GameplayResource(VectorActor.Id,7),Is.EqualTo(item["value"].Integer),item["name"].Text);
                Assert.That(document.Tables[1][0],Is.EqualTo(Hex(item["input"].Text)),"input "+item["name"].Text);
                Assert.That(document.Tables[2][0],Is.EqualTo(Hex(item["owner"].Text)),"owner "+item["name"].Text);
                Assert.That(document.Tables[3][0],Is.EqualTo(Hex(item["resource"].Text)),"resource "+item["name"].Text);
                if(item["cast"].Text.Length==0)Assert.That(document.Tables[5].Count,Is.Zero);
                else Assert.That(document.Tables[5][0],Is.EqualTo(Hex(item["cast"].Text)),"cast "+item["name"].Text);
                if(item["cooldown"].Text.Length==0)Assert.That(document.Tables[4].Count,Is.Zero);
                else Assert.That(document.Tables[4][0],Is.EqualTo(Hex(item["cooldown"].Text)),"cooldown "+item["name"].Text);
            }
            TestContext.WriteLine("S3 independent protocol vectors: admission, windup, release, two pulses, recovery, completion, cooldown boundary; mismatches=0");
        }
        internal static readonly StableEntityKey VectorActor=StableEntityKey.Placed(79,1);
        internal static Simulation VectorWorld()
        {
            var ability=new GameplayAbilityDef(11,"timeline",1,12,new GameplayTargetSpec(GameplayTargetMode.Self),new[]{
                new GameplayAbilityAction(1,GameplayActionTrigger.OnRelease,GameplayPrimitive.ChangeResource,7,11),
                new GameplayAbilityAction(2,GameplayActionTrigger.ActivePulse,GameplayPrimitive.ChangeResource,7,3),
                new GameplayAbilityAction(3,GameplayActionTrigger.OnEnd,GameplayPrimitive.ChangeResource,7,5)},
                new[]{new GameplayCost(7,3)},21,11,2,5,2,2);
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(7,"resource",0,1000,100)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new[]{new GameplayArchetypeDef(12,new[]{7UL})},
                new GameplayCapacities(1,1,16,16,cooldowns:1,casts:1,inputCommands:4,targetsPerCast:1),"s3.vector.critical",
                abilities:new[]{ability},inputAuthority:new GameplayInputAuthority(1,new[]{VectorActor.Id}));
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(79,1,1));
            var sim=Simulation.CreateRetainedWorld(91,catalog,new SimConfig(entityCapacity:2,timerCapacity:16),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(VectorActor),12);return sim;
        }
        [TestCase(0UL),TestCase(11UL)]
        public void StatusSourceWithNoAttributesStillRestoresCompleteActionCapture(ulong abilityKey)
        {
            Simulation direct=EmptyAttributeWorld(abilityKey);
            M0c2S3Fixture.Input(direct,new GameplayInputCommand(new GameplayStamp(VectorActor.Id),1,GameplayInputKind.Activate,1));
            while(direct.Clock.Tick<4)direct.Step(default);
            Assert.That(direct.GameplayStatusAt(0).SourceSnapshot.AttributeCount,Is.Zero);
            Assert.That(direct.GameplayStatusAt(0).SourceSnapshot.ActionCaptureCount,Is.EqualTo(1));
            Simulation restored=EmptyAttributeWorld(abilityKey);restored.RestoreRetainedSnapshot(direct.SaveRetainedSnapshot(),true);
            for(int i=0;i<5;i++){direct.Step(default);restored.Step(default);Assert.That(restored.StateHashRetained(),Is.EqualTo(direct.StateHashRetained()));}
        }
        private static Simulation EmptyAttributeWorld(ulong abilityKey)
        {
            var ability=new GameplayAbilityDef(abilityKey,"empty-source",1,12,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{new GameplayAbilityAction(1,GameplayActionTrigger.OnRelease,GameplayPrimitive.ApplyStatus,31,-1)},windupTicks:3);
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(7,"unused",0,1000,100)},new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(12,new ulong[0])},new GameplayCapacities(1,1,16,16,statuses:4,casts:1,inputCommands:4,targetsPerCast:1),
                "s3.empty-source",statuses:new[]{new GameplayStatusDef(31,"state",GameplayStatusDuration.Timed,4)},abilities:new[]{ability},
                inputAuthority:new GameplayInputAuthority(1,new[]{VectorActor.Id}));
            var sim=Simulation.CreateRetainedWorld(91,new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(79,1,1)),
                new SimConfig(entityCapacity:2,timerCapacity:16),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(VectorActor),12);return sim;
        }
        private static GameplaySchema TargetContinuationSchema(bool requery,int maximum=1)
        {
            var target=new GameplayTargetSpec(GameplayTargetMode.Query,GameplayTargetShape.Sphere,GameplayTargetSelection.AllCapped,
                timing:requery?GameplayTargetTiming.RequeryAtRelease:GameplayTargetTiming.LockAtAdmission,
                rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer,maximumTargets:maximum,radiusRaw:5*M0c2S3Fixture.S,
                useTargetPoint:true,excludeSelf:true,relation:GameplayRelation.OtherFaction);
            var ability=new GameplayAbilityDef(1,"target-continuation",1,1,target,new[]{
                M0c2S3Fixture.Action(1,GameplayPrimitive.Damage,1,11),
                M0c2S3Fixture.Action(2,GameplayPrimitive.Damage,1,3,GameplayActionTrigger.ActivePulse)},windupTicks:3,activeTicks:5,pulsePeriodTicks:2);
            return M0c2S3Fixture.Schema(abilities:new[]{ability});
        }
        private static GameplayInputCommand TargetContinuationCommand()=>new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id),1,
            GameplayInputKind.Activate,1,GameplayTargetMode.Query,aimDirection:SimVec3.Metres(1,0,0),targetPoint:SimVec3.Metres(6,0,0));
        [TestCase(false,0),TestCase(true,2)]
        public void ZeroCappedLockedTargetsAndReleaseRequeryRestoreAcrossEveryPhase(bool requery,int maximum)
        {
            for(int checkpoint=1;checkpoint<=8;checkpoint++)
            {
                var schema=TargetContinuationSchema(requery,maximum);var direct=M0c2S3Fixture.CreateWith(schema);
                M0c2S3Fixture.Input(direct,TargetContinuationCommand());Assert.That(direct.GameplayInputResultAt(0).TotalHits,Is.EqualTo(3));
                while(direct.Clock.Tick<checkpoint)direct.Step(default);
                var restored=M0c2S3Fixture.CreateWith(schema,true,1);restored.RestoreRetainedSnapshot(direct.SaveRetainedSnapshot(),true);
                for(int follow=0;follow<9;follow++){direct.Step(default);restored.Step(default);Assert.That(restored.StateHashRetained(),Is.EqualTo(direct.StateHashRetained()));}
            }
        }
        [Test]
        public void RetiredLockedTargetCanRestoreBeforeReleaseThenRejectsAtTheSameReleaseTick()
        {
            var schema=TargetContinuationSchema(false);var direct=M0c2S3Fixture.CreateWith(schema);
            M0c2S3Fixture.Input(direct,TargetContinuationCommand());
            direct.Entities.Destroy((EntityId)direct.Entities.Resolve(new EntityLink(M0c2S3Fixture.Target2.Id)));direct.Step(default);
            Assert.That(direct.GameplayCastCount,Is.EqualTo(1));var restored=M0c2S3Fixture.CreateWith(schema,true,1);
            restored.RestoreRetainedSnapshot(direct.SaveRetainedSnapshot(),true);
            for(int i=0;i<3;i++){direct.Step(default);restored.Step(default);Assert.That(restored.StateHashRetained(),Is.EqualTo(direct.StateHashRetained()));}
            Assert.That(direct.GameplayAbilityFailureCount(GameplayAbilityFailure.TargetGone),Is.EqualTo(1));Assert.That(direct.GameplayCastCount,Is.Zero);
        }
        [TestCase("future-explicit-generation"),TestCase("explicit-list-mismatch"),TestCase("self-list-mismatch")]
        public void CanonicalCastIntentRejectsImpossibleGenerationAndTargetIdentity(string fault)
        {
            bool self=fault=="self-list-mismatch";var mode=self?GameplayTargetMode.Self:GameplayTargetMode.Explicit;
            var definition=new GameplayAbilityDef(11,"intent",1,1,new GameplayTargetSpec(mode,rangeRaw:12*M0c2S3Fixture.S,
                layerMask:M0c2S3Fixture.UnitLayer),new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,1)},windupTicks:3);
            var schema=M0c2S3Fixture.Schema(abilities:new[]{definition});var original=M0c2S3Fixture.CreateWith(schema);
            M0c2S3Fixture.Input(original,M0c2S3Fixture.Command(1,mode:mode));
            var document=new M0c2S2ProtocolTests.Document(original);var cast=document.Tables[5][0];
            if(fault=="future-explicit-generation")M0c2S2ProtocolTests.Put(cast,90,1);
            else M0c2S2ProtocolTests.Put(cast,self?91:107,M0c2S3Fixture.OtherActor.Id.Value);
            var restored=M0c2S3Fixture.CreateWith(schema);
            Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true),fault);
        }
        [Test]
        public void CooldownCannotReachBeyondEveryPossibleAdmissionEvenWithMatchingTaskAndLeaves()
        {
            var original=M0c2S3Fixture.Create();M0c2S3Fixture.Input(original,M0c2S3Fixture.Command(1));
            var before=new M0c2S2ProtocolTests.Document(original);byte[] row=before.Tables[4][0];
            var key=new TaskKey((long)M0c2S2ProtocolTests.Word(row,34),M0c2S2ProtocolTests.Word(row,42),
                new StableId(M0c2S2ProtocolTests.Word(row,50)),M0c2S2ProtocolTests.Word(row,58),M0c2S2ProtocolTests.Word(row,66));
            Assert.That(original.Scheduler.CancelStable(key),Is.True);
            var replacement=original.Scheduler.ScheduleFor(1000,0,key.Origin,key.Kind,
                new StableSimEvent(GameplayEvents.AbilityCooldown,target:new EntityLink(key.Origin)));
            var document=new M0c2S2ProtocolTests.Document(original);row=document.Tables[4][0];
            M0c2S2ProtocolTests.Put(row,25,1000);M0c2S2ProtocolTests.Put(row,34,1000);M0c2S2ProtocolTests.Put(row,58,replacement.Counter);
            var restored=M0c2S3Fixture.Create();
            Assert.Throws<SimSnapshotException>(()=>restored.RestoreRetainedSnapshot(document.Encode(),true));
        }
        [Test]
        public void SharedCooldownKeyAcceptsTheLongestDeclaredDuration()
        {
            var self=new GameplayTargetSpec(GameplayTargetMode.Self);
            var actions=new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,1)};
            var schema=M0c2S3Fixture.Schema(abilities:new[]{new GameplayAbilityDef(1,"short",1,1,self,actions,cooldownKey:1,cooldownTicks:2),
                new GameplayAbilityDef(2,"long",2,1,self,actions,cooldownKey:1,cooldownTicks:100)});
            var original=M0c2S3Fixture.CreateWith(schema);M0c2S3Fixture.Input(original,M0c2S3Fixture.Command(2,mode:GameplayTargetMode.Self));
            Assert.That(original.GameplayCooldownReady(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(101));
            var restored=M0c2S3Fixture.CreateWith(schema);restored.RestoreRetainedSnapshot(original.SaveRetainedSnapshot(),true);
            for(int i=0;i<101;i++){original.Step(default);restored.Step(default);Assert.That(restored.StateHashRetained(),Is.EqualTo(original.StateHashRetained()));}
        }
        private static byte[] Hex(string value)
        {var result=new byte[value.Length/2];for(int i=0;i<result.Length;i++)result[i]=Convert.ToByte(value.Substring(i*2,2),16);return result;}
    }
}
