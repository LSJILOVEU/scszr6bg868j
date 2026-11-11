using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Rai01ContentTests
    {
        private static AiRules Rules(string ai) => A3.Load(P1.PackJson(ai)).Section<AiRules>();

        [Test]
        public void PolicyDefaultsOffAndRegistersOnlyWithBurst()
        {
            Assert.That(Rules(null).BurstPauseFollowsShooter, Is.False);
            Assert.That(Rules("{}").BurstPauseFollowsShooter, Is.False);
            Assert.That(Rules("{ \"burstPauseFollowsShooter\": false }").BurstPauseFollowsShooter, Is.False);
            Assert.That(Rules(Rai01TimingTests.Ai()).BurstPauseFollowsShooter, Is.True);
            M6World inactive = P3.World(P2.Cast(), "{ \"burstPauseFollowsShooter\": true }");
            Assert.That(inactive.System<BurstCadence>(), Is.Null);
            Assert.That(P3.Systems(inactive), Is.EqualTo(P1BaselineTests.Systems));
            M6World enabled = P3.World(P2.Cast(), Rai01TimingTests.Ai());
            Assert.That(enabled.System<BurstCadence>(), Is.Not.Null);
            Assert.That(P3.Systems(enabled), Is.EqualTo(P3BaselineTests.Systems.Replace("battle.ai-fire|0|14;", "battle.ai-fire|0|14;battle.burst-cadence|0|15;")));
        }

        [TestCase("1")]
        [TestCase("\"true\"")]
        public void PolicyRequiresBoolean(string value)
        {
            var error = Assert.Throws<ContentException>(() => Rules("{ \"burstPauseFollowsShooter\": " + value + " }"));
            Assert.That(error.Message, Is.EqualTo("content pack 'a2-test' layer 'a2-test' ai field 'burstPauseFollowsShooter': must be true or false"));
        }

        [TestCase(0)]
        [TestCase(3)]
        public void FalsePreservesHashesSnapshotsAndSystems(int burst)
        {
            string old = P3.AiBurst(burst, 40);
            string disabled = old.Replace("}", ", \"burstPauseFollowsShooter\": false }");
            M6World a = P3.World(P2.Cast(), old, false, new P1.DestroyProbe(60, P3.KeyB));
            M6World b = P3.World(P2.Cast(), disabled, false, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(b.System<BurstCadence>(), Is.Null);
            Assert.That(P3.Systems(b), Is.EqualTo(P3.Systems(a)));
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            for (long tick = 1; tick <= 150; tick++)
            {
                a.Sim.Step(P2.Drive(a, tick, Rai01TimingTests.SwitchScript));
                b.Sim.Step(P2.Drive(b, tick, Rai01TimingTests.SwitchScript));
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
                Assert.That(P1.Fired(b, P3.KeyA), Is.EqualTo(P1.Fired(a, P3.KeyA)), "tick " + tick);
                if (tick == 60 || tick == 150) Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
                if (tick == 64) Assert.That(P1.Fired(b, P3.KeyA), Is.True, "the existing target-switch behaviour");
            }
        }
    }
}
