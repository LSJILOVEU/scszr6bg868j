using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // L5 fixture G: a fire-off garrison on terrain. Everything that shapes behaviour is written here: seed 41, the map, the
    // content layer, the combat pack, the match pack (think every 10 ticks), the unit table, the speed and T = 900 ticks.
    // Nothing here may be tuned to pass a gate.
    //
    // Definitions used by the L5 tests:
    //   Arrived (at the end, or at any sampled tick): the unit is alive, the AI holds no order for it (observer TryGetOrder is
    //     false), the canonical slot s = GuardSlots.Nearest(position) exists and belongs to the unit's target point INDEX
    //     (team 1 -> 0, team 2 -> 1), and the horizontal distance to At(s) squared is <= (S/4)^2 (the AI's own 0.25 m).
    //   Stuck: a stall window, not a movement-state check. At tick t >= 300 a unit that is not arrived and whose horizontal
    //     displacement between t - 300 and t is strictly under 1 m is stuck. Active, orders and enemies are not looked at, so
    //     a unit that gave up (stopped) or re-ordered still counts, and so does a unit queueing for 300 ticks.
    //   End: after the 900th Step.
    internal static class L5
    {
        internal const long S = SimFix.Scale;
        internal const int Ticks = 900;
        internal const int Width = 48, Depth = 32;
        internal const long Speed = 3000;

        // Height in half-metre units, copied from the demo terrain formula (not called: the demo map may change).
        internal static int H(int i, int j)
        {
            int b = i <= 10 ? 0 : i <= 22 ? i - 10 : i <= 34 ? 34 - i : 0;
            return b + (j >= 23 && i >= 14 && i <= 30 ? 2 : 0);
        }

        internal static byte Surface(int x, int z) => (byte)(z >= 14 && z < 18 ? 2 : x >= 36 && x < 40 && z >= 4 && z < 12 ? 1 : 0);

        internal static readonly string[] Names = { "grass", "mud", "road" };

        internal static WcMap Map() =>
            Wp23.Make(Width, Depth, 0, 10 * S, new[] { (6, 8, 1, 5) }, 3 * S, (i, j) => H(i, j) * 32768, Names, Surface);

        internal static byte[] MapBytes() => WcMapWriter.Write(Map());

        // Every field of every section this package relies on is written out (sections merge field by field).
        internal const string PackJson = "{ \"id\": \"l5-test\", \"extends\": \"rwr\", \"surfaces\": [ "
            + "{ \"id\": \"grass\", \"passable\": true, \"speedMilli\": 1000, \"cost\": 1, \"concealMilli\": 1000 }, "
            + "{ \"id\": \"mud\", \"passable\": true, \"speedMilli\": 500, \"cost\": 3, \"concealMilli\": 1000 }, "
            + "{ \"id\": \"road\", \"passable\": true, \"speedMilli\": 1250, \"cost\": 1, \"concealMilli\": 1000 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }, "
            + "\"ai\": { \"failedSlotTicks\": 0, \"targetMemoryTicks\": 0, \"searchTicks\": 0 } }";

        internal static ContentPack Load(string id, string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [id] = json }, FileContentSource.Rwr()), id);

        internal static ContentPack Pack() => Load("l5-test", PackJson);

        // No weapons: nobody can fire. No melee, no block.
        internal static CombatPack Combat() => new CombatPack(Array.Empty<WeaponDef>(), new L5Hit(), new NearestVisibleEnemy(), 100 * S);

        private sealed class L5Hit : IHitResolver
        {
            public ulong PolicyKey => 0x4C35484954000001;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 20;
        }

        internal static SimVec3 At(long xHalf, long zHalf) => new SimVec3(SimFix.Ratio(xHalf, 2), default, SimFix.Ratio(zHalf, 2));

        // Index 0: the ridge crest; index 1: the mud patch. Radius 3 m: 37 hexagonal slots each.
        internal static readonly ControlPointDef[] Points =
        {
            new ControlPointDef(1, At(45, 33), SimFix.FromInt(3)),
            new ControlPointDef(2, At(77, 17), SimFix.FromInt(3)),
        };

        internal static MatchPack Match() => new MatchPack(Points,
            new[] { new SpawnAnchor(1, At(7, 33)), new SpawnAnchor(2, At(89, 33)) },
            2, 1, false, 100000, InterruptMode.Reset, 1, 90, 10, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2));

        internal static readonly (ulong key, int team, long x, long z)[] Table =
        {
            (1, 1, 2500, 13000), (2, 1, 2500, 14500), (3, 1, 2500, 16000), (4, 1, 2500, 17500), (5, 1, 2500, 19000),
            (6, 1, 4000, 13000), (7, 1, 4000, 14500), (8, 1, 4000, 16000), (9, 1, 4000, 17500), (10, 1, 4000, 19000),
            (11, 2, 44000, 13000), (12, 2, 44000, 14500), (13, 2, 44000, 16000), (14, 2, 44000, 17500), (15, 2, 44000, 19000),
            (16, 2, 45500, 13000), (17, 2, 45500, 14500), (18, 2, 45500, 16000), (19, 2, 45500, 17500), (20, 2, 45500, 19000),
        };

        internal static UnitSpawn[] Units()
        {
            var units = new UnitSpawn[Table.Length];
            for (int i = 0; i < Table.Length; i++)
                units[i] = new UnitSpawn(Table[i].key, 0, Table[i].team, Table[i].x, Table[i].z, Speed, Table[i].team, -1, false);
            return units;
        }

        internal static WorldRecipe Recipe() =>
            new WorldRecipe(41, MoverKind.InterimCrowd, Units(), Array.Empty<BridgeCommand>())
                .WithMatch(Match()).WithCombat(Combat()).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId).WithTerrainRules(Pack());

        internal static M6World World(bool reverse = false) => new M6World(Recipe(), MapBytes(), reverse);

        internal static int Target(int team) => team == 1 ? 0 : 1;

        internal static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));

        // Replaces the AI's order bridge with an observer that forwards every member and logs each Order.
        internal static Orders Install(M6World w)
        {
            var ai = w.System<SimpleAiSystem>();
            FieldInfo field = typeof(SimpleAiSystem).GetField("moves", BindingFlags.NonPublic | BindingFlags.Instance);
            var orders = new Orders((IMoveOrders)field.GetValue(ai), w.Sim);
            field.SetValue(ai, orders);
            return orders;
        }

        internal sealed class Orders : IMoveOrders
        {
            private readonly IMoveOrders inner;
            private readonly Simulation sim;
            internal readonly List<(long tick, ulong id, SimVec3 target)> Log = new List<(long, ulong, SimVec3)>();
            internal Orders(IMoveOrders inner, Simulation sim) { this.inner = inner; this.sim = sim; }
            public bool CanStandAt(SimVec3 at) => inner.CanStandAt(at);
            public bool IsCommanded(StableId unit) => inner.IsCommanded(unit);
            public void Order(StableId unit, SimVec3 target) { Log.Add((sim.Clock.Tick, unit.Value, target)); inner.Order(unit, target); }
            public void Stop(StableId unit) => inner.Stop(unit);
            public bool TryGetOrder(StableId unit, out SimVec3 target) => inner.TryGetOrder(unit, out target);
            public bool HasFailed(StableId unit) => inner.HasFailed(unit);
            internal int CountFor(ulong id)
            {
                int n = 0;
                foreach (var o in Log) if (o.id == id) n++;
                return n;
            }
        }

        internal static readonly long ArrivalSquared = (S / 4) * (S / 4);

        internal static long Horizontal2(SimVec3 a, SimVec3 b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return dx * dx + dz * dz;
        }

        // The arrival test for a given position and order state; slot is the canonical slot found, or -1.
        internal static bool ArrivedAt(GuardSlots slots, int target, bool hasOrder, SimVec3 position, out int slot)
        {
            slot = -1;
            if (hasOrder) return false;
            int s = slots.Nearest(position);
            if (s < 0 || slots.PointOf(s) != target) return false;
            if (Horizontal2(position, slots.At(s)) > ArrivalSquared) return false;
            slot = s;
            return true;
        }

        internal static bool Arrived(M6World w, Orders orders, GuardSlots slots, int index, out int slot)
        {
            var (key, team, _, _) = Table[index];
            slot = -1;
            if (!w.Has<Position>(key) || w.Dead(key)) return false;
            bool hasOrder = orders.TryGetOrder(Id(w, key), out _);
            return ArrivedAt(slots, Target(team), hasOrder, w.Read<Position>(key).Value, out slot);
        }

        internal static SimVec3[] Positions(M6World w)
        {
            var p = new SimVec3[Table.Length];
            for (int i = 0; i < p.Length; i++) p[i] = w.Read<Position>(Table[i].key).Value;
            return p;
        }

        // Folds a hash into a run fingerprint (FNV-1a over the 64-bit hashes).
        internal static ulong Fold(ulong fold, ulong h) => (fold ^ h) * 1099511628211UL;
        internal const ulong FoldSeed = 14695981039346656037UL;
    }

    // The stall-window meter: a pure function of (tick, positions, arrived), sampled every tick from tick 0.
    internal sealed class L5StuckMeter
    {
        internal const int Window = 300;
        private readonly int n;
        private readonly SimVec3[,] history;
        private readonly bool[] ever, now;
        private long next;
        internal L5StuckMeter(int count) { n = count; history = new SimVec3[count, Window + 1]; ever = new bool[count]; now = new bool[count]; }

        internal void Sample(long tick, SimVec3[] positions, bool[] arrived)
        {
            Assert.That(tick, Is.EqualTo(next), "the meter samples every tick from 0");
            next = tick + 1;
            int slot = (int)(tick % (Window + 1)), back = (int)((tick + 1) % (Window + 1));
            for (int i = 0; i < n; i++)
            {
                history[i, slot] = positions[i];
                now[i] = tick >= Window && !arrived[i] && L5.Horizontal2(positions[i], history[i, back]) < L5.S * L5.S;
                if (now[i]) ever[i] = true;
            }
        }

        internal bool Now(int i) => now[i];
        internal bool Ever(int i) => ever[i];
        internal int CurrentCount { get { int c = 0; foreach (bool b in now) if (b) c++; return c; } }
        internal int EverCount { get { int c = 0; foreach (bool b in ever) if (b) c++; return c; } }
    }
}
