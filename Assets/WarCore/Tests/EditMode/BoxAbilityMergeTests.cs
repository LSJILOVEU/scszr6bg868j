using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c3a"), Category("BoxAbilityMerge")]
    public sealed class BoxAbilityMergeTests
    {
        private const uint EmptyOcclusionLayer = 0x40000000u;

        private static GameplaySchema Schema(GameplayTargetSpec spec)
        {
            var ability = new GameplayAbilityDef(1, "box-life-merge", 1, 1, spec,
                new[] { M0c2S3Fixture.Action(1, GameplayPrimitive.Damage, 1, 17) },
                new[] { new GameplayCost(2, 3) }, 1, 4, 1);
            return new GameplaySchema(new[] {
                    new GameplayResourceDef(1, "health", -100, 10000, 8000, isLife: true),
                    new GameplayResourceDef(2, "energy", 0, 1000000, 100000) },
                new[] { new GameplayDamageTypeDef(1, "direct", 1, 1) },
                new[] { new GameplayPipelineDef(1, new GameplayDamageSpec(0, GameplayDamageStages.None)) },
                new[] { new GameplayArchetypeDef(1, new[] { 1UL, 2UL }, factionKey: 1),
                    new GameplayArchetypeDef(2, new[] { 1UL, 2UL }, factionKey: 2) },
                new GameplayCapacities(8, 16, 128, 256, 0, 16, 8, 16, 8), "box-life-merge",
                new GameplayStatusDef[0], new[] { ability },
                new GameplayInputAuthority(1, new[] { M0c2S3Fixture.Actor.Id, M0c2S3Fixture.OtherActor.Id }),
                new[] { new GameplayLifeRuleDef(1, 1, false, 0), new GameplayLifeRuleDef(2, 1, false, 0) });
        }

        private static GameplayTargetSpec Area(GameplayTargetTiming timing, GameplayTargetSelection selection, uint occlusion = 0)
            => new GameplayTargetSpec(GameplayTargetMode.Query, GameplayTargetShape.Sphere, selection,
                rangeRaw: 12 * M0c2S3Fixture.S, layerMask: M0c2S3Fixture.UnitLayer,
                maximumTargets: selection == GameplayTargetSelection.Nearest ? 1 : 2,
                radiusRaw: 3 * M0c2S3Fixture.S, useTargetPoint: true, timing: timing,
                relation: occlusion == 0 ? GameplayRelation.Any : GameplayRelation.OtherFaction, occlusionMask: occlusion);

        private static GameplayInputCommand AreaCommand()
            => new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id), 1, GameplayInputKind.Activate,
                1, GameplayTargetMode.Query, targetPoint: SimVec3.Metres(6, 0, 0));

        private static Simulation Scene(GameplayTargetSpec spec, bool livingBox = true, bool corpseBox = true)
        {
            Simulation sim = M0c2S3Fixture.CreateWith(Schema(spec));
            if (corpseBox)
                SetBox(sim, M0c2S3Fixture.Target2);
            if (livingBox)
                SetBox(sim, M0c2S3Fixture.Target);
            sim.RegisterGameplay("box-life-merge.kill", new KillMiddleBox(), SimSystemPhase.Queries);
            sim.Step(default);
            Assert.That(sim.GameplayLife(M0c2S3Fixture.Target2.Id).State, Is.EqualTo(GameplayLifeState.Dead));
            // The corpse remains a geometric hit. Life filtering, not spatial deletion, must remove it.
            var sphere = SpatialQueries.Sphere(SimVec3.Metres(6, 0, 0), SimFix.FromInt(3), M0c2S3Fixture.UnitLayer);
            Assert.That(sim.SpatialQuery.Nearest(in sphere, out SpatialHit nearest), Is.True);
            Assert.That(sim.Entities.StableOf(nearest.Entity), Is.EqualTo(M0c2S3Fixture.Target2.Id));
            return sim;
        }

        private static void SetBox(Simulation sim, StableEntityKey key)
        {
            Assert.That(sim.Entities.TryResolve(key.Id, out EntityRef entity), Is.True);
            sim.Entities.Set(sim.Entities.Resolve<Collider>(), (EntityId)entity,
                Collider.Box(SimFix.Ratio(1, 2), SimFix.FromInt(1), SimFix.Ratio(1, 4),
                    BoxYaw.Identity, M0c2S3Fixture.UnitLayer));
        }

        [TestCase(GameplayTargetTiming.LockAtAdmission, GameplayTargetSelection.Nearest)]
        [TestCase(GameplayTargetTiming.LockAtAdmission, GameplayTargetSelection.AllCapped)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease, GameplayTargetSelection.Nearest)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease, GameplayTargetSelection.AllCapped)]
        public void CorpseBoxNeverDisplacesAdjacentLivingBoxOrCapsule(GameplayTargetTiming timing, GameplayTargetSelection selection)
        {
            Simulation sim = Scene(Area(timing, selection));
            M0c2S3Fixture.Input(sim, AreaCommand());
            var result = sim.GameplayInputResultAt(0);
            Assert.That(result.Failure, Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(result.TotalHits, Is.EqualTo(2));
            Assert.That(result.Written, Is.EqualTo(selection == GameplayTargetSelection.Nearest ? 1 : 2));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(8000), "one-tick windup has not released");
            sim.Step(default);
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(7983), "living box surface is nearest");
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target3.Id, 1), Is.EqualTo(selection == GameplayTargetSelection.Nearest ? 8000 : 7983));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id, 1), Is.EqualTo(-100));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.TargetGone), Is.Zero);
            Assert.That(sim.GameplayCastCount, Is.Zero);
        }

        [TestCase(GameplayTargetTiming.LockAtAdmission)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease)]
        public void ExplicitLivingBoxBesideCorpseIsRejectedBeforeCost(GameplayTargetTiming timing)
        {
            var spec = new GameplayTargetSpec(GameplayTargetMode.Explicit, rangeRaw: 12 * M0c2S3Fixture.S,
                layerMask: M0c2S3Fixture.UnitLayer, timing: timing);
            var command = M0c2S3Fixture.Command(1, mode: GameplayTargetMode.Explicit,
                target: new GameplayStamp(M0c2S3Fixture.Target.Id));
            Simulation control = Scene(spec, livingBox: false);
            M0c2S3Fixture.Input(control, command);
            Assert.That(control.GameplayInputResultAt(0).Failure, Is.EqualTo(GameplayAbilityFailure.None));
            control.Step(default);
            Assert.That(control.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(7983));
            Simulation boxed = Scene(spec);
            M0c2S3Fixture.Input(boxed, command);
            Assert.That(boxed.GameplayInputResultAt(0).Failure, Is.EqualTo(GameplayAbilityFailure.InvalidTarget));
            Assert.That(boxed.GameplayResource(M0c2S3Fixture.Actor.Id, 2), Is.EqualTo(100000));
            Assert.That(boxed.GameplayCooldownReady(M0c2S3Fixture.Actor.Id, 1), Is.Zero);
            boxed.Step(default);
            Assert.That(boxed.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(8000));
            Assert.That(boxed.GameplayCastCount, Is.Zero);
        }

        [TestCase(GameplayTargetTiming.LockAtAdmission, GameplayTargetSelection.Nearest)]
        [TestCase(GameplayTargetTiming.LockAtAdmission, GameplayTargetSelection.AllCapped)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease, GameplayTargetSelection.Nearest)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease, GameplayTargetSelection.AllCapped)]
        public void OcclusionSelectionSkipsCorpseAndLivingBoxButKeepsAdjacentCapsule(GameplayTargetTiming timing, GameplayTargetSelection selection)
        {
            Simulation sim = Scene(Area(timing, selection, EmptyOcclusionLayer));
            M0c2S3Fixture.Input(sim, AreaCommand());
            var result = sim.GameplayInputResultAt(0);
            Assert.That(result.Failure, Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(result.TotalHits, Is.EqualTo(1));
            Assert.That(result.Written, Is.EqualTo(1));
            sim.Step(default);
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id, 1), Is.EqualTo(-100));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(8000));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target3.Id, 1), Is.EqualTo(7983));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.TargetGone), Is.Zero);
            Assert.That(sim.GameplayCastCount, Is.Zero);
        }

        [TestCase(GameplayTargetTiming.LockAtAdmission)]
        [TestCase(GameplayTargetTiming.RequeryAtRelease)]
        public void DeadCapsuleIsRejectedBeforeAnyOcclusionRay(GameplayTargetTiming timing)
        {
            Simulation sim = Scene(Area(timing, GameplayTargetSelection.AllCapped, EmptyOcclusionLayer), corpseBox: false);
            // Existing grid regression tests use this same scratch stamp probe. Each query increments it once.
            // A capsule corpse reaches Any if life filtering moves after occlusion; a box would short-circuit first.
            GridQueryContext context = ((SpatialQueryView)sim.SpatialQuery).QueryContext;
            uint before = (uint)GridIndexRegressionAudit.Field(context, "stamp");
            M0c2S3Fixture.Input(sim, AreaCommand());
            Assert.That((uint)GridIndexRegressionAudit.Field(context, "stamp") - before, Is.EqualTo(2u),
                "one AllCapped scan and one living capsule occlusion ray; no corpse ray");
            Assert.That(sim.GameplayInputResultAt(0).TotalHits, Is.EqualTo(1));
            before = (uint)GridIndexRegressionAudit.Field(context, "stamp");
            sim.Step(default);
            // With zero active duration, OnRelease and OnEnd each refresh requery targets this tick.
            Assert.That((uint)GridIndexRegressionAudit.Field(context, "stamp") - before,
                Is.EqualTo(timing == GameplayTargetTiming.RequeryAtRelease ? 4u : 0u));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target2.Id, 1), Is.EqualTo(-100));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(8000));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target3.Id, 1), Is.EqualTo(7983));
        }

        [Test]
        public void AbilityCandidateFiltersKeepOwnerLifeRelationOcclusionOrder()
        {
            // The required order is structural: moving the pure filters can leave final damage unchanged.
            // Keep this source-contract assertion distinct from the behavioral merge cases above.
            string source = Regex.Replace(SourceRules.StripComments(File.ReadAllText("Assets/WarCore/Sim/GameplayAbilityTargets.cs")), @"\s+", "");
            int start = source.IndexOf("for(inti=0;i<result.Written;i++)", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "complete ordered candidate loop must exist");
            int open = source.IndexOf('{', start), depth = 1, end = open + 1;
            for (; end < source.Length && depth != 0; end++)
            {
                if (source[end] == '{') depth++;
                if (source[end] == '}') depth--;
            }
            Assert.That(depth, Is.Zero);
            string loop = source.Substring(open, end - open);
            int owner = loop.IndexOf("if(OwnerIndex(id)<0)continue;", StringComparison.Ordinal);
            int life = loop.IndexOf("if(AbilityTargetLife(target)!=GameplayAbilityFailure.None)continue;", StringComparison.Ordinal);
            int relation = loop.IndexOf("if(!AbilityRelation(actor,target,spec))continue;", StringComparison.Ordinal);
            int occlusion = loop.IndexOf("if(spec.OcclusionMask!=0", StringComparison.Ordinal);
            Assert.That(owner, Is.GreaterThanOrEqualTo(0));
            Assert.That(life, Is.GreaterThan(owner), "life check must follow owner lookup");
            Assert.That(relation, Is.GreaterThan(life), "life check must precede relation filtering");
            Assert.That(occlusion, Is.GreaterThan(relation), "life and relation checks must precede occlusion filtering");
        }

        private sealed class KillMiddleBox : IRetainedGameplaySystem
        {
            public int Order => 10;
            public string StateTypeId => "test.box-life-merge.kill";
            public int StateVersion => 1;
            public void Step(SimContext context)
            {
                if (context.Clock.Tick == 1)
                    context.Damage(context.Owner.GameplayTarget(M0c2S3Fixture.Target2.Id),
                        new GameplayApplyKey(M0c2S3Fixture.Actor.Id, 1, 0, M0c2S3Fixture.Actor.Id, 1, 1), 1, 8100);
            }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
    }
}
