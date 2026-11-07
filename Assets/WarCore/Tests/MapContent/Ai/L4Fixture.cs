using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L4 fixtures: terrain worlds (an all-grass surface table) with one AI unit, no enemies, no reserves, a match that never
    // ends. Everything that shapes behaviour is written here: seed 41, the map, the unit table, the content layer, the
    // thinking interval, the speed and MaxTicks.
    internal static class L4
    {
        internal const long S = SimFix.Scale;
        internal const ulong Unit = 1;
        internal const int Width = 40, Depth = 20;

        // The content layer: grass only, the slope rule, and the ai section when given (null leaves the key out).
        internal static string PackJson(string ai) => "{ \"id\": \"l4-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"grass\" } ], \"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }"
            + (ai == null ? "" : ", \"ai\": " + ai) + " }";
        internal static string Ticks(int n) => "{ \"failedSlotTicks\": " + n + " }";
        internal static ContentPack Pack(string ai) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["l4-test"] = PackJson(ai) }, FileContentSource.Rwr()), "l4-test");

        // Cell (x, z) has its centre at (x + 0.5, z + 0.5) m.
        internal static SimVec3 Cell(int x, int z) => new SimVec3(SimFix.FromRaw(x * S + S / 2), default, SimFix.FromRaw(z * S + S / 2));
        // Eight one-cell walls around cell (x, z): the cell stays standable, nothing outside reaches it.
        internal static (int, int, int, int)[] Ring(int x, int z) =>
            new[] { (x - 1, z - 1, 3, 1), (x - 1, z + 1, 3, 1), (x - 1, z, 1, 1), (x + 1, z, 1, 1) };
        internal static (int, int, int, int)[] Join(params (int, int, int, int)[][] parts)
        {
            var all = new List<(int, int, int, int)>();
            foreach (var p in parts) all.AddRange(p);
            return all.ToArray();
        }

        internal static WcMap Map((int, int, int, int)[] boxes, bool terrain = true) =>
            Wp23.Make(Width, Depth, 0, 4 * S, boxes, 2 * S, null, terrain ? new[] { "grass" } : null, terrain ? (Func<int, int, byte>)((x, z) => 0) : null);

        // Speed 3000 mm/s is 0.1 m per tick (30 ticks per second), under the 0.25 m arrival distance.
        internal const long Speed = 3000;
        internal static UnitSpawn Soldier(SimVec3 at, int squad = 1) =>
            new UnitSpawn(Unit, 0, squad, at.X.Raw * 1000 / S, at.Z.Raw * 1000 / S, Speed, 1, -1, false);

        internal static MatchPack Match(ControlPointDef point, int think) =>
            new MatchPack(new[] { point }, new[] { new SpawnAnchor(1, Cell(1, 10)), new SpawnAnchor(2, Cell(38, 10)) },
                2, 1, false, 3, InterruptMode.Reset, 1, 90, think, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2));

        // ai == null: no ai key. terrain == false drops the surface table (a flat world, no terrain rules).
        internal static WorldRecipe Recipe(ControlPointDef point, SimVec3 start, int think, string ai, bool terrain = true, bool standing = false)
        {
            WorldRecipe r = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { Soldier(start) }, Array.Empty<BridgeCommand>())
                .WithMatch(Match(point, think)).WithCombat(RwrPack.CreateM6()).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId);
            if (terrain) r = r.WithTerrainRules(Pack(ai));
            if (standing) r = r.WithStandingOrders();
            return r;
        }

        // Fixture A: one point of radius 500 mm (only the centre slot) on cell (20, 10), walled in; the unit at cell (10, 10).
        internal static readonly SimVec3 ACenter = Cell(20, 10), AStart = Cell(10, 10);
        internal static readonly ControlPointDef APoint = new ControlPointDef(1, ACenter, SimFix.Ratio(1, 2));
        internal static readonly (int, int, int, int)[] AWalls = Ring(20, 10);
        internal static M6World A(int think, string ai, bool terrain = true, bool reverse = false) =>
            new M6World(Recipe(APoint, AStart, think, ai, terrain), WcMapWriter.Write(Map(AWalls, terrain)), reverse);

        internal static Calls Observe(M6World w)
        {
            var ai = w.System<SimpleAiSystem>();
            FieldInfo field = typeof(SimpleAiSystem).GetField("moves", BindingFlags.NonPublic | BindingFlags.Instance);
            var calls = new Calls((IMoveOrders)field.GetValue(ai), w.Sim);
            field.SetValue(ai, calls); return calls;
        }

        internal static IMoveOrders Moves(M6World w) =>
            (IMoveOrders)typeof(SimpleAiSystem).GetField("moves", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());

        internal static StableId Id(M6World w) => w.Sim.Entities.StableOf(w.Unit(Unit));

        // The first thinking tick of the unit (SimpleAiSystem.cs: (tick + id % T) % T == 0), in 1..T.
        internal static long FirstThink(M6World w, int think)
        {
            long first = (think - (long)(Id(w).Value % (ulong)think)) % think;
            return first == 0 ? think : first;
        }

        internal static TerrainTables Terrain(WcMap map, string ai = null) => TerrainDerivation.Derive(map, Pack(ai));
        internal static bool Reachable(TerrainTables t, SimVec3 from, SimVec3 to) => new GridPathfinder(t).Find(from, to) == GridPathStatus.Found;

        internal static string Systems(M6World w) => AssemblyWorlds.Systems(w.Sim);

        // Every Order the AI gives, with the tick it was given at; Stops counted. IsCommanded is forwarded (squad standing).
        internal sealed class Calls : IMoveOrders
        {
            private readonly IMoveOrders inner;
            private readonly WarCore.Sim.Sim sim;
            internal readonly List<(long tick, SimVec3 target)> Orders = new List<(long, SimVec3)>();
            internal int Stops;
            internal Calls(IMoveOrders inner, WarCore.Sim.Sim sim) { this.inner = inner; this.sim = sim; }
            public void Order(StableId id, SimVec3 target) { Orders.Add((sim.Clock.Tick, target)); inner.Order(id, target); }
            public void Stop(StableId id) { Stops++; inner.Stop(id); }
            public bool TryGetOrder(StableId id, out SimVec3 target) => inner.TryGetOrder(id, out target);
            public bool CanStandAt(SimVec3 at) => inner.CanStandAt(at);
            public bool IsCommanded(StableId id) => inner.IsCommanded(id);
            internal List<long> TicksTo(SimVec3 target)
            {
                var list = new List<long>();
                foreach (var o in Orders) if (o.target.X.Raw == target.X.Raw && o.target.Z.Raw == target.Z.Raw) list.Add(o.tick);
                return list;
            }
        }

        // Folds the per-tick hashes of ticks 0..last into one value (FNV-1a over the 64-bit hashes).
        internal static ulong HashRun(M6World w, int last, List<ulong> each = null)
        {
            ulong fold = 14695981039346656037UL;
            for (int tick = 0; tick <= last; tick++)
            {
                if (tick > 0) w.Step();
                ulong h = w.Hash; each?.Add(h);
                fold = (fold ^ h) * 1099511628211UL;
            }
            return fold;
        }
    }
}
