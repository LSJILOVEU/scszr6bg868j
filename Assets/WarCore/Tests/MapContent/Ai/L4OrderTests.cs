using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Why the memory runs at order 9, before the AI (12): a failure made while walking (locomotion, 20) must be recorded on
    // the next tick before the AI thinks. There is no map yet that makes a reachable route fail on the way (left to L5), so
    // the give-up is written between two ticks the way locomotion writes it (stop the intent, clear the waypoint, Failed = 1),
    // just before a thinking tick.
    public sealed class L4OrderTests
    {
        [Test]
        public void AGiveUpWhileWalking_IsRecordedBeforeTheNextThinkingTick()
        {
            const int think = 10;
            M6World w = L4.C(think, L4.Ticks(300), false);
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, think);
            long t = t0 + 2 * think - 1;   // t + 1 is a thinking tick; the unit is still walking (10 m at 0.1 m per tick)
            w.Step((int)t);
            Assert.That(calls.TicksTo(L4.CCenter), Is.EqualTo(new List<long> { t0 }));
            Assert.That(calls.Stops, Is.EqualTo(0));
            Assert.That(calls.Inner.TryGetOrder(L4.Id(w), out _), Is.True, "still walking at the end of t");
            MoveIntent intent = w.Read<MoveIntent>(L4.Unit);
            w.Set(L4.Unit, new MoveIntent(intent.Target, intent.SpeedPerTick, intent.Squad, false));
            w.Set(L4.Unit, default(MoveWaypoint));
            MoveProgress p = w.Read<MoveProgress>(L4.Unit); p.Failed = 1; w.Set(L4.Unit, p);
            Assert.That(calls.Inner.HasFailed(L4.Id(w)), Is.True);
            w.Step();
            Assert.That(L4.Memory(w), Is.EqualTo((-1, 0, t + 1)), "recorded at t + 1");
            Assert.That(calls.TicksTo(L4.CCenter), Is.EqualTo(new List<long> { t0 }), "no order at the thinking tick t + 1");
            w.Step(300);
            Assert.That(calls.TicksTo(L4.CCenter), Is.EqualTo(new List<long> { t0, t + 301 }), "ordered again once t - T reaches N");
        }
    }
}
