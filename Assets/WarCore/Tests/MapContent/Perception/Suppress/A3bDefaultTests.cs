using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1 and 9(d): the default (suppressTicks left out or 0) changes nothing, byte for byte.
    public sealed class A3bDefaultTests
    {
        // 1(a): left out and written as 0 side by side, memory world and search world: the same system table, equal state
        // hashes on every tick to 600, equal snapshot bytes on 300 and 600, the same fire ticks of A.
        [TestCase(A3bBaselineTests.MemoryAi)]
        [TestCase(A3bBaselineTests.SearchAi)]
        public void LeftOutAndZero_StepTogether(string ai)
        {
            string zeroAi = ai.Substring(0, ai.Length - 2) + ", \"suppressTicks\": 0 }";
            M6World off = A3bBaselineTests.World(ai), zero = A3bBaselineTests.World(zeroAi);
            Assert.That(L4.Systems(zero), Is.EqualTo(L4.Systems(off)));
            Assert.That(zero.System<TargetSuppress>(), Is.Null);
            Assert.That(zero.Hash, Is.EqualTo(off.Hash), "tick 0");
            for (long tick = 1; tick <= 600; tick++)
            {
                A3bBaselineTests.Step(off, tick); A3bBaselineTests.Step(zero, tick);
                Assert.That(zero.Hash, Is.EqualTo(off.Hash), "tick " + tick);
                Assert.That(A3bBaselineTests.AFired(zero), Is.EqualTo(A3bBaselineTests.AFired(off)), "fire, tick " + tick);
                if (tick == 300 || tick == 600)
                    Assert.That(zero.Sim.SaveRetainedSnapshot(), Is.EqualTo(off.Sim.SaveRetainedSnapshot()), "snapshot bytes, tick " + tick);
            }
        }

        // 1(b): suppressTicks alone (no memory) on A2's fixture D: the world builds, registers no suppression and runs as A2.
        [Test]
        public void SuppressWithoutMemory_FixtureDUnchanged()
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = A2.D(A3.Load(A3.PackJson("", "{ \"suppressTicks\": 100 }"))));
            Assert.That(L4.Systems(w), Is.EqualTo(A3MemoryBaselineTests.Systems));
            Assert.That(w.System<TargetSuppress>(), Is.Null);
            Assert.That(A2.HashRun(w, 600, null, out int fires), Is.EqualTo(A2BaselineTests.DRun600));
            Assert.That(fires, Is.EqualTo(A2BaselineTests.DFires600));
        }

        // 9(d): with the AI fire state on (R = 15, K = 3), suppressTicks 0 leaves the table without 18, the fire state at
        // version 2, every tick's hash and the tick-300 snapshot bytes as recorded before the change.
        [Test]
        public void FireWorld_ZeroIsByteIdentical()
        {
            string zeroAi = A3bBaselineTests.FireAi.Substring(0, A3bBaselineTests.FireAi.Length - 2) + ", \"suppressTicks\": 0 }";
            M6World off = A3bBaselineTests.World(A3bBaselineTests.FireAi), zero = A3bBaselineTests.World(zeroAi);
            Assert.That(L4.Systems(zero), Is.EqualTo(A3bBaselineTests.FireSystems));
            Assert.That(L4.Systems(off), Is.EqualTo(A3bBaselineTests.FireSystems));
            Assert.That(zero.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            Assert.That(off.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            for (long tick = 1; tick <= 400; tick++)
            {
                A3bBaselineTests.Step(off, tick); A3bBaselineTests.Step(zero, tick);
                Assert.That(zero.Hash, Is.EqualTo(off.Hash), "tick " + tick);
                if (tick == 300)
                {
                    byte[] a = off.Sim.SaveRetainedSnapshot(), b = zero.Sim.SaveRetainedSnapshot();
                    Assert.That(b, Is.EqualTo(a));
                    Assert.That(a.Length, Is.EqualTo(A3bBaselineTests.FireLength300));
                    Assert.That(A3.Digest(a), Is.EqualTo(A3bBaselineTests.FireDigest300));
                }
            }
        }
    }
}
