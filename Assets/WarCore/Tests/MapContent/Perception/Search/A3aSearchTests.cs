using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 2-5, 7 and 13 on the search fixture (A3aFixture.cs). A thinks on 6, 16, 26, ... B holds from tick 1 and
    // gets its only move on tick 24; it is last selected on 46 at P = (22.5, 19.7) (dirt, 9.2 m from A) and then walks
    // into the grass, where it cannot be seen from more than 2 m. "Before the step" is the end of the previous tick: the
    // AI decides in Requests, movement comes after.
    public sealed class A3aSearchTests
    {
        private const long Move = 24, S = 46, Go = 56, H = 146, G = 206;
        private static readonly SimVec3 P = new SimVec3(SimFix.FromRaw(22 * A2.S + A2.S / 2), default, SimFix.FromRaw(1291046));  // z = 19.7 m (recorded raw)

        // One tick: the script, A's point before the step, the AI's orders since the start.
        private sealed class Run
        {
            internal readonly M6World W;
            internal readonly L4.Calls Calls;
            internal readonly List<SimVec3> ABefore = new List<SimVec3> { default };
            private readonly SimVec3 hide;
            internal Run(int memory, int? search, SimVec3 hide, int failed = 0)
            {
                W = A3a.World(A3a.BStart, A3a.Ai(memory, search, failed));
                Calls = L4.Observe(W); this.hide = hide;
                Assert.That(A3a.FirstThink(W, A3a.KeyA), Is.EqualTo(6), "A's thinking phase");
            }
            internal long Tick => W.Sim.Clock.Tick;
            internal void Step(InputFrame extra = default)
            {
                ABefore.Add(A3a.At(W, A3a.KeyA));
                long t = Tick + 1;
                W.Sim.Step(extra.Squad.HasValue ? extra : A3a.Drive(W, A3a.Script(t, hide, Move)));
            }
            internal void To(long tick) { while (Tick < tick) Step(); }
            internal List<long> To(SimVec3 at) => Calls.TicksTo(at);
            internal int OrdersIn(long from, long to) { int n = 0; foreach (var o in Calls.Orders) if (o.tick >= from && o.tick <= to) n++; return n; }
            internal long Seen() { Assert.That(A3a.Recall(W, Tick, out _, out _, out long s), Is.True, "recall, tick " + Tick); return s; }
        }

        private static readonly SimVec3 Hide = A2.Cell(22, 26);

        // 2: the walk to P, ordered once; 3: the stand, the give-up, no second walk, the deletion.
        [Test]
        public void GoesStandsAndGivesUp()
        {
            var r = new Run(300, 60, Hide);
            r.To(Move - 1);
            Assert.That(r.ABefore[1], Is.EqualTo(A3a.AAt));
            Assert.That(A3a.At(r.W, A3a.KeyA), Is.EqualTo(A3a.AAt), "A never moved before B did");
            Assert.That(r.Calls.Orders, Is.Empty);
            r.To(S);
            Assert.That(r.Seen(), Is.EqualTo(S));
            Assert.That(A3a.Recall(r.W, S, out _, out SimVec3 p, out _), Is.True);
            Assert.That(new[] { p.X.Raw, p.Z.Raw }, Is.EqualTo(new[] { P.X.Raw, P.Z.Raw }), "P");
            // 2(i): the next thinking tick, nothing in sight: one order to P, no search entry.
            r.To(Go);
            Assert.That(r.Seen(), Is.EqualTo(S), "B not seen again");
            Assert.That(r.OrdersIn(Go, Go), Is.EqualTo(1));
            Assert.That(r.To(P), Is.EqualTo(new List<long> { Go }));
            Assert.That(A3a.SearchCount(r.W), Is.EqualTo(0));
            // 2(ii)/(iii): the distance to P never grows on the way; no second order up to H (H >= S + 30).
            Assert.That(H, Is.GreaterThanOrEqualTo(S + 30));
            SimWide last = A3a.Flat(A3a.At(r.W, A3a.KeyA), P);
            for (long t = Go + 1; t < H; t++)
            {
                r.Step();
                Assert.That(r.Seen(), Is.EqualTo(S), "no re-sighting, tick " + t);
                if ((t - 6) % 10 == 0)
                {
                    SimWide d = A3a.Flat(A3a.At(r.W, A3a.KeyA), P);
                    Assert.That(d, Is.LessThanOrEqualTo(last), "distance, tick " + t); last = d;
                    Assert.That(A3a.Arrived(r.ABefore[(int)t], P), Is.False, "not arrived before the step, tick " + t);
                }
            }
            // 3(i): H is the first thinking tick that starts within 1/4 m of P; A was still walking, 0.2 m short; it stops.
            Assert.That(A3a.Order(r.W, out SimVec3 walking), Is.True, "still walking before H");
            Assert.That(A3a.Same(walking, P), Is.True);
            r.Step();
            Assert.That(A3a.Arrived(r.ABefore[(int)H], P), Is.True);
            Assert.That(A3a.Flat(r.ABefore[(int)H], P), Is.GreaterThan(default(SimWide)), "short of P");
            Assert.That(A3a.Order(r.W, out _), Is.False, "stopped on H");
            Assert.That(r.To(P), Is.EqualTo(new List<long> { Go }), "one order to P from Go to H");
            Assert.That(A3a.SearchCount(r.W), Is.EqualTo(1));
            Assert.That(A3a.Entry(r.W, out long seen, out long since), Is.True);
            Assert.That(seen, Is.EqualTo(S)); Assert.That(since, Is.EqualTo(H));
            SimVec3 stand = A3a.At(r.W, A3a.KeyA);
            // 3(ii): standing from H to G - 1: no order, no move order, the same point; B unseen, A not commanded, G < S + M.
            Assert.That(G, Is.EqualTo(H + 60)); Assert.That(G + 10, Is.LessThan(S + 300));
            for (long t = H + 1; t < G; t++)
            {
                r.Step();
                Assert.That(A3a.At(r.W, A3a.KeyA), Is.EqualTo(stand), "stands, tick " + t);
                Assert.That(A3a.Order(r.W, out _), Is.False, "no move order, tick " + t);
                Assert.That(r.Seen(), Is.EqualTo(S));
                Assert.That(r.W.Has<SquadStanding>(A3a.KeyA), Is.False);
            }
            Assert.That(r.OrdersIn(H, G - 1), Is.EqualTo(0));
            // 3(iii): G gives up: an order to point 2's guard slot, not P.
            r.Step();
            Assert.That(r.OrdersIn(G, G), Is.EqualTo(1));
            SimVec3 slot = r.Calls.Orders[r.Calls.Orders.Count - 1].target;
            Assert.That(A3a.Same(slot, A3a.Point2), Is.True, "point 2's only slot");
            // 3(iv): to S + M - 1 no second walk to P; on G + 10 A is not within reach of P, B unseen, A not commanded.
            for (long t = G + 1; t < S + 300; t++)
            {
                r.Step();
                if (t == G + 10) Assert.That(A3a.Arrived(r.ABefore[(int)t], P), Is.False);
                Assert.That(r.Seen(), Is.EqualTo(S));
                Assert.That(r.W.Has<SquadStanding>(A3a.KeyA), Is.False);
            }
            Assert.That(r.To(P), Is.EqualTo(new List<long> { Go }));
            // 3(v): S + M: the memory is gone and so is the entry.
            r.Step();
            Assert.That(A3a.MemoryCount(r.W), Is.EqualTo(0)); Assert.That(A3a.SearchCount(r.W), Is.EqualTo(0));
        }

        // 3(vi): N not a multiple of the thinking interval: the stand ends on the first thinking tick at least N after H.
        [Test]
        public void StandRoundsUpToTheThinkingInterval()
        {
            var r = new Run(300, 55, Hide);
            r.To(G - 1);
            Assert.That(r.OrdersIn(H, G - 1), Is.EqualTo(0));
            Assert.That(A3a.Entry(r.W, out _, out long since), Is.True); Assert.That(since, Is.EqualTo(H));
            r.Step();
            Assert.That(r.OrdersIn(G, G), Is.EqualTo(1));
            Assert.That(A3a.Same(r.Calls.Orders[r.Calls.Orders.Count - 1].target, A3a.Point2), Is.True);
        }

        // 4: B stops 1.1 m past P in the grass: A sees it again on the way and engages; then B leaves and A follows the new P.
        [Test]
        public void SeenAgainOnTheWay_Engages()
        {
            SimVec3 near = new SimVec3(P.X, default, SimFix.FromRaw(20 * A2.S + 8 * A2.S / 10));
            var r = new Run(300, 60, near);
            const long R = 146;
            r.To(R - 1);
            Assert.That(r.Seen(), Is.EqualTo(S), "not selected before R");
            Assert.That(A3a.SearchCount(r.W), Is.EqualTo(0));
            int stops = r.Calls.Stops;
            r.Step();
            Assert.That(r.Seen(), Is.EqualTo(R), "selected on R");
            Assert.That(A3a.Order(r.W, out _), Is.False, "stopped to engage");
            Assert.That(r.Calls.Stops, Is.EqualTo(stops + 1));
            Assert.That(A3a.SearchCount(r.W), Is.EqualTo(0));
            // B walks away deep into the grass: lost again, A goes for the new last known point.
            r.Step(A3a.Command(SquadCommandKind.MoveTo, 2, Hide));
            r.To(R + 60);
            long s2 = r.Seen();
            Assert.That(s2, Is.GreaterThan(R)); Assert.That(s2 + 10, Is.LessThanOrEqualTo(R + 60), "lost again");
            Assert.That(A3a.Recall(r.W, r.Tick, out _, out SimVec3 p2, out _), Is.True);
            Assert.That(A3a.Same(p2, P), Is.False, "a new last known point");
            Assert.That(r.To(p2), Is.EqualTo(new List<long> { s2 + 10 }));
        }

        // 5: the memory runs out before A gets there: back to the points on the first thinking tick after S + M, no entry ever.
        [Test]
        public void MemoryRunsOutOnTheWay()
        {
            var r = new Run(40, 60, Hide);
            for (long t = 1; t <= S + 50; t++) { r.Step(); Assert.That(A3a.SearchCount(r.W), Is.EqualTo(0), "tick " + t); }
            Assert.That(r.To(P), Is.EqualTo(new List<long> { Go }));
            Assert.That(A3a.Arrived(r.ABefore[(int)(S + 40)], P), Is.False, "not there when the memory ends");
            Assert.That(r.OrdersIn(S + 40, S + 40), Is.EqualTo(1), "back to the points on 86");
            Assert.That(A3a.Same(r.Calls.Orders[r.Calls.Orders.Count - 1].target, A3a.Point2), Is.True);
        }

        // 7(a): a squad hold on the way: no further order to P while commanded. (b) a hold while standing: no new entry, no give-up order.
        [TestCase(76)]
        [TestCase(156)]
        public void SquadCommand_TakesOver(long at)
        {
            var r = new Run(300, 60, Hide);
            r.To(at - 1);
            int entries = A3a.SearchCount(r.W), orders = r.Calls.Orders.Count;
            r.Step(A3a.Command(SquadCommandKind.Hold, 1));
            for (long t = at + 1; t <= S + 299; t++)
            {
                r.Step();
                Assert.That(r.W.Has<SquadStanding>(A3a.KeyA), Is.True, "commanded, tick " + t);
                Assert.That(A3a.SearchCount(r.W), Is.EqualTo(entries), "tick " + t);
            }
            Assert.That(r.Calls.Orders.Count, Is.EqualTo(orders), "no order while commanded");
            Assert.That(r.To(P), Is.EqualTo(new List<long> { Go }));
        }

        // 13: A loses its weapon between S and the next thinking tick: it does not go for P.
        [Test]
        public void Unarmed_DoesNotSearch()
        {
            var r = new Run(300, 60, Hide);
            r.To(S + 1);
            Combatant c = r.W.Read<Combatant>(A3a.KeyA);
            r.W.Set(A3a.KeyA, new Combatant(c.Team, -1, c.Ammo, c.DueMilli, c.SpreadStored, c.SpreadLastMilli));
            r.To(Go - 1);
            Assert.That(r.Seen(), Is.EqualTo(S));
            Assert.That(A3a.Recall(r.W, Go, out _, out _, out _), Is.True, "the memory is readable on Go");
            Assert.That(A3a.Arrived(A3a.At(r.W, A3a.KeyA), P), Is.False);
            Assert.That(A3a.SearchCount(r.W), Is.EqualTo(0));
            r.To(S + 299);
            Assert.That(r.To(P), Is.Empty);
            Assert.That(A3a.SearchCount(r.W), Is.EqualTo(0));
        }
    }
}
