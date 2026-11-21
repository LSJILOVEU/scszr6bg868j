// Managed direct counter tests are excluded from Unity discovery, as M0c2S2AllocationTests.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // ML-11b-1 acceptance 13: a Step allocates nothing with the memory, the search, the suppressive fire and the chase reading
    // layers: the connectivity table is built with the world.
    public sealed class Ml11b1AllocationTests
    {
        private static long Window(M6World w, int ticks)
        {
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < ticks; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            return total;
        }

        private static void Counter()
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - start;
            GC.KeepAlive(control);
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
        }

        // The playtest battle on the assault map, from tick 13 to its end (tick 196): thinking, memory, search, suppressive
        // fire, reaction fire, deaths.
        [Test]
        public void ThePlaytestBattleAllocatesNothing()
        {
            M6World warm = Ml11b1BattleTests.Assault();
            warm.Step(196);
            Assert.That(warm.Match.Winner, Is.EqualTo(1));
            M6World w = Ml11b1BattleTests.Assault();
            w.Step(12);
            long total = Window(w, 184);
            Assert.That(w.Match.Winner, Is.EqualTo(1), "the window covers the battle to its end");
            Assert.That(total, Is.EqualTo(0L));
            Counter();
        }

        private static M6World SearchAndSuppress()
        {
            M6World w = World(new[] { Gunner(A, 33500, 31500), Gunner(2, 33500, 30500), Still(B, FarX, FarZ) },
                "{ \"targetMemoryTicks\": 300, \"searchTicks\": 40, \"suppressTicks\": 30 }", Guns(8));
            Plant(w, A, B, Up(41500, 32500), 1);
            Plant(w, 2, B, Up(40500, 30500), 1);
            Send(w, A, 0);
            return w;
        }

        // One soldier climbs to a remembered place and stands there; another shoots at a remembered place on the floor.
        [Test]
        public void TheSearchAndTheSuppressiveFireAllocateNothing()
        {
            M6World warm = SearchAndSuppress();
            warm.Step(230);
            M6World w = SearchAndSuppress();
            w.Step(2);
            int shots = 0;
            long total = 0;
            for (int i = 0; i < 22; i++)
            {
                total += Window(w, 10);
                foreach (FireRecord fire in w.Combat.Fires) if (fire.Shooter.Equals(Id(w, 2))) shots++;
            }
            Assert.That(shots, Is.GreaterThan(0), "the window covers the suppressive fire");
            Assert.That((Layer(w, A), Ordered(w, A)), Is.EqualTo((0, true)), "the window covers the search to its end and the way down");
            Assert.That(total, Is.EqualTo(0L));
            Counter();
        }

        private static M6World Chase() =>
            World(new[] { Blade(A, 33500, 31500), Still(B, 40500, 31500, 1) }, "{ \"meleeChaseRange\": 12 }",
                Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: 1.8)));

        // A soldier with a blade climbs the stair to an enemy and swings.
        [Test]
        public void TheChaseAllocatesNothing()
        {
            M6World warm = Chase();
            warm.Step(100);
            M6World w = Chase();
            w.Step(2);
            long total = Window(w, 98);
            Assert.That(Layer(w, A), Is.EqualTo(2), "the window covers the climb");
            Assert.That(total, Is.EqualTo(0L));
            Counter();
        }
    }
}
#endif
