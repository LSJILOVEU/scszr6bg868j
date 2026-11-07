using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // M6 acceptance 1, recorded before the inertia option existed: the snapshot bytes of a world that only opens WithMotion(),
    // at tick 0 and tick 60. A world that adds the inertia option with both rates at zero must keep these exact bytes.
    public sealed class InertiaOffLockTests
    {
        internal const string MotionTick0 = "7FBC24E309433B0745EC679FC7723DB8E153022196A5E7997B9334592DD8E65E";
        internal const string MotionTick60 = "21B61131FD987175B8D5B30A1C8C471D76DBDED0E8579471CC3E2A8F5F7688D9";

        internal static WorldRecipe Recipe() => InertiaWorlds.Line(25000, 12000).WithMotion();

        [Test]
        public void AMotionWorld_KeepsItsRecordedSnapshotBytes()
        {
            SimSession s = InertiaWorlds.Build(Recipe(), InertiaWorlds.FlatMap());
            string at0 = InertiaWorlds.SnapshotSha(s);
            for (int i = 0; i < 60; i++) s.StepOnce();
            Assert.That(s.Tick, Is.EqualTo(60));
            string at60 = InertiaWorlds.SnapshotSha(s);
            TestContext.Out.WriteLine($"tick0={at0} tick60={at60}");
            Assert.That(M3TestWorld.Component<MotionLimits>(s).MaxSpeed.Raw, Is.EqualTo(13107L), "the motion component is open");
            Assert.That(M3TestWorld.Position(s).X.Raw, Is.GreaterThan(InertiaWorlds.StartXMm * InertiaWorlds.S / 1000), "the unit walked");
            Assert.That(at0, Is.EqualTo(MotionTick0));
            Assert.That(at60, Is.EqualTo(MotionTick60));
        }
            private static readonly string[] Off = { "none", "motion", "motion+inertia(0,0)" };

        private static WorldRecipe OffRecipe(string world) => world switch
        {
            "none" => InertiaWorlds.Line(25000, 12000),
            "motion" => Recipe(),
            _ => Recipe().With(new InertiaOption(0, 0)),
        };

        private static void HasNoState(SimSession s, string world)
        {
            WarCore.Sim.Sim sim = M3TestWorld.Sim(s);
            Assert.That(AssemblyWorlds.Components(sim), Does.Not.Contain(MotionState.ComponentId), world);
            Assert.That(Encoding.ASCII.GetString(sim.SaveRetainedSnapshot()), Does.Not.Contain(MotionState.ComponentId), world);
            Assert.Throws<InvalidOperationException>(() => sim.Entities.Resolve<MotionState>(), world);
        }

        [TestCaseSource(nameof(Off))]
        public void AnOffWorld_HasNoMotionState(string world)
        {
            SimSession s = InertiaWorlds.Build(OffRecipe(world), InertiaWorlds.FlatMap());
            HasNoState(s, world);
            for (int i = 0; i < 60; i++) s.StepOnce();
            HasNoState(s, world);
        }

        [Test]
        public void ZeroRates_KeepTheMotionWorldsBytesAndAssembly()
        {
            SimSession plain = InertiaWorlds.Build(Recipe(), InertiaWorlds.FlatMap());
            SimSession zero = InertiaWorlds.Build(OffRecipe("motion+inertia(0,0)"), InertiaWorlds.FlatMap());
            Assert.That(AssemblyWorlds.Components(M3TestWorld.Sim(zero)), Is.EqualTo(AssemblyWorlds.Components(M3TestWorld.Sim(plain))));
            Assert.That(AssemblyWorlds.Systems(M3TestWorld.Sim(zero)), Is.EqualTo(AssemblyWorlds.Systems(M3TestWorld.Sim(plain))));
            Assert.That(InertiaWorlds.SnapshotSha(zero), Is.EqualTo(MotionTick0));
            for (int i = 0; i < 60; i++) zero.StepOnce();
            Assert.That(zero.Tick, Is.EqualTo(60));
            Assert.That(InertiaWorlds.SnapshotSha(zero), Is.EqualTo(MotionTick60));
        }

        // The F2a lock of the world that opens WithMotion(), rebuilt with the inertia option at zero, describes itself the same.
        [Test]
        public void ZeroRates_KeepTheF2aRecordOfTheMotionWorld()
        {
            const string name = "FacingMotionStanding";
            (WorldRecipe recipe, byte[] map) = AssemblyWorlds.Make(name);
            Assert.That(recipe.Motion, Is.True);
            WorldRecipe zero = recipe.With(new InertiaOption(0, 0));
            Dictionary<string, string> recorded = AssemblyGolden.Of(name);
            SimSession session = InertiaWorlds.Build(zero, map);
            WarCore.Sim.Sim sim = M3TestWorld.Sim(session);
            Assert.That(AssemblyWorlds.Sha(sim.SaveRetainedSnapshot()), Is.EqualTo(recorded["snap0"]));
            Assert.That(AssemblyWorlds.Components(sim), Is.EqualTo(recorded["components"]));
            Assert.That(AssemblyWorlds.Systems(sim), Is.EqualTo(recorded["systems"]));
            Assert.That(AssemblyWorlds.Creation(sim), Is.EqualTo(recorded["creation"]));
            session.StepOnce();
            Assert.That(AssemblyWorlds.Sha(sim.SaveRetainedSnapshot()), Is.EqualTo(recorded["snap1"]));
            while (session.Tick < 100) session.StepOnce();
            Assert.That(AssemblyWorlds.Sha(sim.SaveRetainedSnapshot()), Is.EqualTo(recorded["snap100"]));
            WarCore.Sim.Sim reverse = M3TestWorld.Sim(InertiaWorlds.Build(zero, map, true));
            Assert.That(AssemblyWorlds.Sha(reverse.SaveRetainedSnapshot()), Is.EqualTo(recorded["snapReverse0"]));
            Assert.That(AssemblyWorlds.Creation(reverse), Is.EqualTo(recorded["creationReverse"]));
        }

        [TestCase(9000, 0), TestCase(0, 18000), TestCase(100, 1000000)]
        public void NonZeroRates_AddTheMotionState(long accel, long decel)
        {
            SimSession s = InertiaWorlds.Build(InertiaWorlds.Open(InertiaWorlds.Line(25000, 12000), accel, decel), InertiaWorlds.FlatMap());
            WarCore.Sim.Sim sim = M3TestWorld.Sim(s);
            Assert.That(AssemblyWorlds.Components(sim), Does.Contain(MotionState.ComponentId + ":2007:1"));
            Assert.That(Encoding.ASCII.GetString(sim.SaveRetainedSnapshot()), Does.Contain(MotionState.ComponentId));
            Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(default, -1)));
        }

        [TestCase(0, 0), TestCase(9000, 18000)]
        public void TheOptionWithoutWithMotion_IsRefused(long accel, long decel)
        {
            WorldRecipe recipe = InertiaWorlds.Line(25000, 12000).With(new InertiaOption(accel, decel));
            var error = Assert.Throws<ArgumentException>(() => InertiaWorlds.Build(recipe, InertiaWorlds.FlatMap()));
            Assert.That(error.Message, Is.EqualTo("The inertia option needs the motion component (WithMotion)."));
            Assert.That(error.ParamName, Is.Null);
        }

        [TestCase(-1, "accelMmPerS2"), TestCase(1, "accelMmPerS2"), TestCase(99, "accelMmPerS2"), TestCase(1000001, "accelMmPerS2"),
         TestCase(-1, "decelMmPerS2"), TestCase(1, "decelMmPerS2"), TestCase(99, "decelMmPerS2"), TestCase(1000001, "decelMmPerS2")]
        public void ARateOutsideTheRange_IsRefused(long value, string name)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new InertiaOption(name == "accelMmPerS2" ? value : 0, name == "decelMmPerS2" ? value : 0));
            Assert.That(error.ParamName, Is.EqualTo(name));
        }

        [TestCase(0), TestCase(100), TestCase(1000000)]
        public void ARateInTheRange_IsAccepted(long value)
        {
            Assert.That(new InertiaOption(value, value).OptionId, Is.EqualTo("world.inertia"));
            Assert.That(new InertiaOption(value, 0).OptionId, Is.EqualTo("world.inertia"));
            Assert.That(new InertiaOption(0, value).OptionId, Is.EqualTo("world.inertia"));
        }

        // The rates in raw units per tick squared: SimFix.Ratio(mm/s^2, 900000), truncated.
        [TestCase(100, 7), TestCase(9000, 655), TestCase(18000, 1310), TestCase(1000000, 72817)]
        public void TheRatesReachTheMotionLimits(long mmPerS2, long raw)
        {
            SimSession s = InertiaWorlds.Build(InertiaWorlds.Open(InertiaWorlds.Line(25000, 12000), mmPerS2, mmPerS2), InertiaWorlds.FlatMap());
            Assert.That(M3TestWorld.Component<MotionLimits>(s),
                Is.EqualTo(new MotionLimits(SimFix.FromRaw(13107), SimFix.FromRaw(raw), SimFix.FromRaw(raw), default)));
        }
    }
}
