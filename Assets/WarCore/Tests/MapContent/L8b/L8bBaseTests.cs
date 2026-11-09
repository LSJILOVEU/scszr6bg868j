using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L8b, recorded before the change. (a) The layered M6/M7 entrances build the same worlds as the demo recipes (L8b adds a
    // terrain entrance next to them). (b) The A3a search fixture timeline that the A3a demo replays: the behaviour fixture of
    // A3aSearchTests.GoesStandsAndGivesUp (grass 500, memory 300, search 60, B at 6 m/s hiding at cell (37, 29)), run to 900.
    public sealed class L8bBaseTests
    {
        [Test]
        public void L8bBase_LayeredEntrancesMatchTheDemoRecipes()
        {
            Compare(ContentLayers.M6("rwr-m6"), DemoRecipes.M6(), DemoRecipes.M6MapBytes());
            Compare(ContentLayers.M7("rwr-m6"), DemoRecipes.M7(), DemoRecipes.M7MapBytes());
        }

        private static void Compare(WorldRecipe layered, WorldRecipe demo, byte[] map)
        {
            SimSession a = SimSession.Create(layered, map), b = SimSession.Create(demo, map);
            Assert.That(a.StateHash, Is.EqualTo(b.StateHash), "tick 0");
            for (int t = 0; t < 300; t++) { a.StepOnce(); b.StepOnce(); }
            Assert.That(a.Tick, Is.EqualTo(300));
            Assert.That(a.StateHash, Is.EqualTo(b.StateHash), "tick 300");
        }

        internal const long LastSeen = 36, Chase = 46, DriveHold = 140, Arrive = 146, GiveUp = 206, Forget = 336, End = 900;
        internal static readonly SimVec3 Hide = A2.Cell(37, 29);

        // The fixture run: the hashes of ticks 0..End and the ticks Drive added a hold for B.
        internal static M6World Fixture(List<ulong> hashes, List<long> holds, System.Action<M6World, long> after = null)
        {
            M6World w = A3a.World(A3a.BStart, A3a.Ai(300, 60), conceal: 500, bSpeed: 6000);
            hashes?.Add(w.Hash);
            for (long t = 1; t <= End; t++)
            {
                InputFrame f = A3a.Drive(w, A3a.Script(t, Hide, A3a.MoveTick));
                if (t != 1 && f.Squad.HasValue && f.Squad.Value.Kind == SquadCommandKind.Hold) holds?.Add(t);
                w.Sim.Step(f);
                hashes?.Add(w.Hash);
                after?.Invoke(w, t);
            }
            return w;
        }

        [Test]
        public void L8bBase_A3aFixtureTimeline()
        {
            var holds = new List<long>();
            var countChanges = new List<(long tick, int count)>();
            var seenTicks = new List<long>();
            long lastSeen = -1;
            int count = 0;
            var noMemory = new List<long>();
            L4.Calls calls = null;
            M6World w = A3a.World(A3a.BStart, A3a.Ai(300, 60), conceal: 500, bSpeed: 6000);
            calls = L4.Observe(w);
            for (long t = 1; t <= End; t++)
            {
                InputFrame f = A3a.Drive(w, A3a.Script(t, Hide, A3a.MoveTick));
                if (t != 1 && f.Squad.HasValue && f.Squad.Value.Kind == SquadCommandKind.Hold) holds.Add(t);
                w.Sim.Step(f);
                int c = A3a.SearchCount(w);
                if (c != count) { countChanges.Add((t, c)); count = c; }
                if (A3a.Recall(w, t, out _, out _, out long seen))
                {
                    if (seen != lastSeen) { seenTicks.Add(seen); lastSeen = seen; }
                    if (t >= LastSeen && t < Forget) Assert.That(seen, Is.EqualTo(LastSeen), "seen, tick " + t);
                }
                else if (t >= LastSeen) noMemory.Add(t);
            }
            Assert.That(holds, Is.EqualTo(new List<long> { DriveHold }), "Drive adds one hold for B");
            Assert.That(seenTicks, Is.EqualTo(new List<long> { 6, 16, 26, LastSeen }), "B last seen on 36");
            var orderTicks = new List<long>();
            foreach (var o in calls.Orders) orderTicks.Add(o.tick);
            Assert.That(orderTicks, Is.EqualTo(new List<long> { Chase, GiveUp }), "A's move orders up to 900");
            Assert.That(calls.Orders[1].target, Is.Not.EqualTo(calls.Orders[0].target));
            Assert.That(A3a.IsSlot(w, calls.Orders[1].target), Is.True, "the give-up order is a guard slot");
            Assert.That(countChanges, Is.EqualTo(new List<(long, int)> { (Arrive, 1), (Forget, 0) }), "search entry");
            Assert.That(noMemory.Count, Is.EqualTo(End - Forget + 1), "no memory from 336 on");
            Assert.That(noMemory[0], Is.EqualTo(Forget));
        }
    }
}
