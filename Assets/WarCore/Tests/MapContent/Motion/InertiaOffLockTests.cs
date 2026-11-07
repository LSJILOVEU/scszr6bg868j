using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;

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
    }
}
