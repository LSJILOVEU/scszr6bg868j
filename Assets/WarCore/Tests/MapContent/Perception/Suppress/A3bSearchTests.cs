using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 6: search (A3a) and suppression together on A3a's fixture (B at 6 m/s into the grass, S = 36).
    public sealed class A3bSearchTests
    {
        private static readonly SimVec3 P = A3b.Raw(1622832, 0, 1295152);
        private static readonly SimVec3 Point2 = A2.Cell(34, 4), BEnd = A2.Cell(37, 29);
        private const string Both = "{ \"targetMemoryTicks\": 300, \"searchTicks\": 60, \"suppressTicks\": 300 }";
        private const string OnlySuppress = "{ \"targetMemoryTicks\": 300, \"suppressTicks\": 300 }";

        private static List<long> Run(M6World w, long last, System.Action<M6World, long> after = null)
        {
            var fires = new List<long>();
            for (long t = 1; t <= last; t++)
            {
                w.Sim.Step(A3a.Drive(w, A3a.Script(t, BEnd, 20)));
                if (A3b.Fired(w)) fires.Add(t);
                after?.Invoke(w, t);
            }
            return fires;
        }

        // 6(a): the search orders are A3a's ({46 -> P, 206 -> Point2}); A fires while walking (61, 121), not while it
        // stands on P ([146, 205], Arrived), and again from 209 on its way to Point2; the entry goes with the memory at 336.
        [Test]
        public void WithSearch_NoShotWhileStandingOnP()
        {
            M6World w = A3a.World(A3a.BStart, Both, conceal: 500, bSpeed: 6000);
            L4.Calls calls = L4.Observe(w);
            Assert.That(L4.Systems(w), Does.Contain("battle.ai-contacts|0|13;battle.ai-search|0|17;battle.ai-suppress|0|18;"));
            var records = new Dictionary<long, FireRecord>();
            SimVec3 before = default;
            List<long> fires = new List<long>();
            for (long t = 1; t <= 400; t++)
            {
                before = A3a.At(w, A3a.KeyA);
                w.Sim.Step(A3a.Drive(w, A3a.Script(t, BEnd, 20)));
                if (A3b.Fired(w, out FireRecord f)) { fires.Add(t); records[t] = f; }
                if (t == 46)
                {
                    Assert.That(A3b.Count(w), Is.EqualTo(1));
                    Assert.That(A3b.Entry(w, A3b.KeyA, out long seen, out long since), Is.True);
                    Assert.That(seen, Is.EqualTo(36)); Assert.That(since, Is.EqualTo(46));
                }
                if (t >= 146 && t <= 205) Assert.That(A3a.Arrived(before, P), Is.True, "arrived, tick " + t);
                if (t == 335) Assert.That(A3b.Count(w), Is.EqualTo(1));
                if (t >= 336) Assert.That(A3b.Count(w), Is.EqualTo(0), "count, tick " + t);
            }
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 209, 239, 299, 329)));
            Assert.That(A3b.Same(records[61].Origin, A3b.Raw(1548688, 0, 762256)), A3b.V(records[61].Origin));
            Assert.That(A3b.Same(records[121].Origin, A3b.Raw(1605541, 0, 1140602)), A3b.V(records[121].Origin));
            Assert.That(records[61].Outcome, Is.EqualTo(FireOutcome.Open));
            Assert.That(records[121].Outcome, Is.EqualTo(FireOutcome.Open));
            Assert.That(calls.Orders.Count, Is.EqualTo(2));
            Assert.That(calls.Orders[0].tick, Is.EqualTo(46)); Assert.That(A3b.Same(calls.Orders[0].target, P), A3b.V(calls.Orders[0].target));
            Assert.That(calls.Orders[1].tick, Is.EqualTo(206)); Assert.That(A3b.Same(calls.Orders[1].target, Point2), A3b.V(calls.Orders[1].target));
        }

        // 6(b): both allocation orders agree on every tick.
        [Test]
        public void WithSearch_ReverseEqual()
        {
            M6World f = A3a.World(A3a.BStart, Both, conceal: 500, bSpeed: 6000), r = A3a.World(A3a.BStart, Both, reverse: true, conceal: 500, bSpeed: 6000);
            for (long t = 1; t <= 400; t++)
            {
                f.Sim.Step(A3a.Drive(f, A3a.Script(t, BEnd, 20))); r.Sim.Step(A3a.Drive(r, A3a.Script(t, BEnd, 20)));
                Assert.That(r.Hash, Is.EqualTo(f.Hash), "tick " + t);
            }
        }

        // 6(c): without the search A walks to Point2 from 46 and keeps firing at P on the way.
        [Test]
        public void WithoutSearch_FiresOnTheWayToPoint2()
        {
            M6World w = A3a.World(A3a.BStart, OnlySuppress, conceal: 500, bSpeed: 6000);
            List<long> fires = Run(w, 400, (x, t) =>
            {
                if (t == 335) Assert.That(A3b.Count(x), Is.EqualTo(1));
                if (t >= 336) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 151, 181, 241, 271, 301)));
        }
    }
}
