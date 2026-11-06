using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // A feature defined here, outside the builder: one component and one system, switched on through WorldRecipe.With.
    // It works without any line of WorldBuilder.cs or WorldRecipe.cs knowing about it.
    internal struct FakeCharge : IStateHash
    {
        public int Ticks;
        public void WriteState(StateHash hash) { hash.Add(Ticks); }
    }

    internal sealed class FakeChargeSerializer : IStableComponentSerializer<FakeCharge>
    {
        public int TypeId => 9101;
        public int Version => 1;
        public void Write(SimStateWriter writer, in FakeCharge value) { writer.Int32(value.Ticks); }
        public FakeCharge Read(SimStateReader reader) => new FakeCharge { Ticks = reader.Int32() };
    }

    internal sealed class FakeChargeSystem : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly ComponentType<FakeCharge> charges;
        private readonly EntityStage stage;
        internal FakeChargeSystem(Simulation sim, ComponentType<FakeCharge> charges)
        { this.sim = sim; this.charges = charges; stage = new EntityStage(sim.Entities); }
        public int Order => 940;
        public string StateTypeId => "test.fake-charge";
        public int StateVersion => 1;
        public void Step(SimContext context)
        {
            foreach (EntityId unit in stage.Capture())
                if (sim.Entities.TryGet(charges, unit, out FakeCharge value))
                    sim.Entities.Set(charges, unit, new FakeCharge { Ticks = value.Ticks + 1 });
        }
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }

    internal sealed class FakeChargeFeature : WorldFeature
    {
        private ComponentType<FakeCharge> charges;
        public override string OptionId => "test.fake-charge";
        public override void RegisterComponents(WorldAssembly a) =>
            charges = a.Sim.Entities.Register<FakeCharge>("test.fake-charge", new FakeChargeSerializer());
        public override void PlaceUnit(WorldAssembly a, int index, EntityId unit) => a.Sim.Entities.Set(charges, unit, new FakeCharge());
        public override void RegisterSystems(WorldAssembly a) =>
            a.Sim.RegisterGameplay("test.fake-charge", new FakeChargeSystem(a.Sim, charges), SimSystemPhase.Queries);
    }

    public sealed class AssemblyFeatureTests
    {
        private static SimSession Build(WorldRecipe recipe) => AssemblyWorlds.Build(recipe, AssemblyWorlds.FmsMap(), false);

        [Test]
        public void AFeatureAddedWithTheOption_IsPlacedAndRuns()
        {
            SimSession session = Build(AssemblyWorlds.FmsRecipe().With(new FakeChargeFeature()));
            Simulation sim = M3TestWorld.Sim(session);
            Assert.That(AssemblyWorlds.Components(sim), Does.Contain("test.fake-charge:9101:1"));
            Assert.That(AssemblyWorlds.Systems(sim), Does.Contain("test.fake-charge|"));
            for (ulong key = 1; key <= 5; key++)
                Assert.That(M3TestWorld.Component<FakeCharge>(session, key).Ticks, Is.EqualTo(0));
            for (int i = 0; i < 12; i++) session.StepOnce();
            for (ulong key = 1; key <= 5; key++)
                Assert.That(M3TestWorld.Component<FakeCharge>(session, key).Ticks, Is.EqualTo(12), "unit " + key);
        }

        [Test]
        public void WithoutTheOption_NothingOfTheFeatureExists_AndTheRecordedAssemblyHolds()
        {
            SimSession plain = Build(AssemblyWorlds.FmsRecipe());
            Simulation sim = M3TestWorld.Sim(plain);
            Assert.That(AssemblyWorlds.Components(sim), Does.Not.Contain("test.fake-charge"));
            Assert.That(AssemblyWorlds.Systems(sim), Does.Not.Contain("test.fake-charge"));
            Assert.Throws<InvalidOperationException>(() => sim.Entities.Resolve<FakeCharge>());
            Assert.That(AssemblyWorlds.Sha(sim.SaveRetainedSnapshot()), Is.EqualTo(AssemblyGolden.Of("FacingMotionStanding")["snap0"]));
            SimSession with = Build(AssemblyWorlds.FmsRecipe().With(new FakeChargeFeature()));
            Assert.That(AssemblyWorlds.Sha(M3TestWorld.Sim(with).SaveRetainedSnapshot()), Is.Not.EqualTo(AssemblyGolden.Of("FacingMotionStanding")["snap0"]));
        }

        [Test]
        public void AWorldWithAFeature_SnapshotsAndRestoresIntoAnotherBuildOfTheSameRecipe()
        {
            SimSession source = Build(AssemblyWorlds.FmsRecipe().With(new FakeChargeFeature()));
            for (int i = 0; i < 20; i++) source.StepOnce();
            byte[] snapshot = M3TestWorld.Sim(source).SaveRetainedSnapshot();
            SimSession target = Build(AssemblyWorlds.FmsRecipe().With(new FakeChargeFeature()));
            M3TestWorld.Sim(target).RestoreRetainedSnapshot(snapshot);
            Assert.That(target.StateHash, Is.EqualTo(source.StateHash));
            Assert.That(M3TestWorld.Component<FakeCharge>(target, 3).Ticks, Is.EqualTo(20));
        }

        [Test]
        public void TheOptionIsOneSwitch_KeptByEveryCopy_InTheOrderAdded()
        {
            var feature = new FakeChargeFeature();
            WorldRecipe recipe = AssemblyWorlds.FmsRecipe().With(feature).With(feature);
            Assert.That(recipe.Options, Is.EqualTo(new List<IWorldOption> { WorldOptions.Facing, WorldOptions.Motion, WorldOptions.Standing, feature }));
            WorldRecipe copy = recipe.WithPlayer(1).WithBlockAi().WithCrowdPass(CrowdPassKind.TeamIdle);
            Assert.That(copy.Options, Is.EqualTo(recipe.Options));
            Assert.Throws<ArgumentNullException>(() => recipe.With(null));
        }
    }
}
