using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L5 acceptance 7, fixture H: a route exists, but walking it fails. A one-cell corridor (z in [10, 11), x in [15, 25)) on a
    // 40 x 20 all-grass terrain map; the AI unit (team 1) starts at cell (10, 10); a team-0 unit rests at cell (20, 10). The
    // pathfinder ignores units, so the slot at cell (30, 10) is reachable; the crowd never lets a mover through a resting unit
    // of another team, so the order is given up by the order-level guard. Everything is written here; nothing may be tuned.
    public sealed class L5WalkFailTests
    {
        private const long S = L5.S;
        private const int Think = 10, Memory = 300, Limit = 600;
        private const ulong Ai = 1, Blocker = 2;

        private const string PackJson = "{ \"id\": \"l5-walk\", \"extends\": \"rwr\", \"surfaces\": [ "
            + "{ \"id\": \"grass\", \"passable\": true, \"speedMilli\": 1000, \"cost\": 1, \"concealMilli\": 1000 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }, "
            + "\"ai\": { \"failedSlotTicks\": 300, \"targetMemoryTicks\": 0, \"searchTicks\": 0 } }";

        private static ContentPack Pack() => L5.Load("l5-walk", PackJson);

        private static WcMap Map() => Wp23.Make(40, 20, 0, 4 * S, new[] { (15, 0, 10, 10), (15, 11, 10, 9) }, 2 * S, null,
            new[] { "grass" }, (x, z) => 0);

        private static readonly SimVec3 Slot = L4.Cell(30, 10), Start = L4.Cell(10, 10), Rest = L4.Cell(20, 10);

        private static WorldRecipe Recipe()
        {
            var point = new ControlPointDef(1, Slot, SimFix.Ratio(1, 2));
            var match = new MatchPack(new[] { point }, new[] { new SpawnAnchor(1, L4.Cell(1, 10)), new SpawnAnchor(2, L4.Cell(38, 10)) },
                2, 1, false, 3, InterruptMode.Reset, 1, 90, Think, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2));
            var units = new[]
            {
                new UnitSpawn(Ai, 0, 1, 10500, 10500, L5.Speed, 1, -1, false),
                new UnitSpawn(Blocker, 0, 2, 20500, 10500, L5.Speed, 0, -1, false),
            };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithMatch(match).WithCombat(L5.Combat()).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId).WithTerrainRules(Pack());
        }

        private static M6World World() => new M6World(Recipe(), WcMapWriter.Write(Map()));

        private static List<long> OrdersTo(L5.Orders o, ulong id, long after)
        {
            var list = new List<long>();
            foreach (var e in o.Log)
                if (e.id == id && e.tick > after && e.target.X.Raw == Slot.X.Raw && e.target.Z.Raw == Slot.Z.Raw) list.Add(e.tick);
            return list;
        }

        // The first thinking tick of the unit at or after `from` (SimpleAiSystem: (tick + id % T) % T == 0).
        private static long ThinkAtOrAfter(StableId id, long from)
        {
            long t = from;
            while ((t + (long)(id.Value % (ulong)Think)) % Think != 0) t++;
            return t;
        }

        // Every tick: the resting unit is exactly where it spawned (X, Y and Z), and the AI unit has not passed it.
        private static void CheckCorridor(M6World w, SimVec3 rest)
        {
            long tick = w.Sim.Clock.Tick;
            Assert.That(w.Read<Position>(Blocker).Value, Is.EqualTo(rest), "tick " + tick + ": the resting unit never moves");
            Assert.That(w.Read<Position>(Ai).Value.X.Raw, Is.LessThan(Rest.X.Raw), "tick " + tick + ": the AI unit never passes the resting unit");
        }

        [Test]
        public void L5_RouteExists_WalkingFails_MemorySkipsThenRetries_AndSnapshotsResume()
        {
            M6World w = World();
            L5.Orders o = L5.Install(w);
            StableId id = L5.Id(w, Ai);
            SimVec3 rest = w.Read<Position>(Blocker).Value;
            Assert.That((rest.X.Raw, rest.Z.Raw), Is.EqualTo((Rest.X.Raw, Rest.Z.Raw)));
            CheckCorridor(w, rest);

            // (a) the first order and a route at that moment, with this fixture's own content layer.
            while (o.Log.Count == 0) { w.Step(); CheckCorridor(w, rest); Assert.That(w.Sim.Clock.Tick, Is.LessThanOrEqualTo(Think), "the AI orders at its first thinking tick"); }
            long t0 = o.Log[0].tick;
            Assert.That(o.Log[0].id, Is.EqualTo(id.Value));
            Assert.That((o.Log[0].target.X.Raw, o.Log[0].target.Z.Raw), Is.EqualTo((Slot.X.Raw, Slot.Z.Raw)));
            Assert.That(new GridPathfinder(TerrainDerivation.Derive(Map(), Pack())).Find(Start, Slot), Is.EqualTo(GridPathStatus.Found), "a route exists");

            // (b) (c) walking fails within the limit, the blocker never moves, the AI unit never passes it.
            long f = -1;
            while (f < 0)
            {
                w.Step();
                long tick = w.Sim.Clock.Tick;
                CheckCorridor(w, rest);
                if (o.HasFailed(id)) f = tick;
                else Assert.That(tick - t0, Is.LessThan(Limit), "the order fails within t0 + " + Limit);
            }
            MoveProgress progress = w.Read<MoveProgress>(Ai);
            TestContext.Out.WriteLine($"L5 H t0={t0} f={f} f-t0={f - t0} margin={Limit - (f - t0)} orderStall={progress.OrderStall} retries={progress.Retries}");
            Assert.That(f - t0, Is.LessThanOrEqualTo(Limit));
            Assert.That(progress.OrderStall, Is.GreaterThanOrEqualTo(240), "the order-level guard (OrderStallLimit 240) gave up");

            // (d) the memory records the failure on the next tick.
            w.Step();
            CheckCorridor(w, rest);
            Assert.That(L4.Memory(w), Is.EqualTo((-1, 0, f + 1)), "recorded at f + 1");

            // (f) a snapshot at f + 1 restored onto a reversed allocation.
            byte[] snapshot = w.Sim.SaveRetainedSnapshot();
            M6World c = World();
            c.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true);
            L5.Orders oc = L5.Install(c);
            Assert.That(c.Unit(Ai), Is.Not.EqualTo(w.Unit(Ai)), "physical allocation must actually change");
            Assert.That(c.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));

            // (e) no order to the slot before f + 301; the retry is the first thinking tick at or after f + 301.
            long retry = ThinkAtOrAfter(id, f + Memory + 1);
            for (long tick = f + 2; tick <= f + 320; tick++)
            {
                w.Step(); c.Step();
                CheckCorridor(w, rest); CheckCorridor(c, rest);
                Assert.That(c.Hash, Is.EqualTo(w.Hash), "tick " + tick);
            }
            Assert.That(OrdersTo(o, id.Value, f), Is.EqualTo(new List<long> { retry }), "skipped until f + 301, then ordered again");
            Assert.That(OrdersTo(oc, L5.Id(c, Ai).Value, f), Is.EqualTo(new List<long> { retry }), "the restored world retries at the same tick");
        }
    }
}
