using System;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A3a fixtures, written here (A3's files are not touched). A2's map size, cells, weapon, think interval (10) and sight
    // (L = 20 m); the grass covers every cell with z >= 20 and conceals to a tenth (Lc = 2 m), so a target that walks into
    // it is lost at once and stays lost from anywhere more than 2 m away. Observer A (key 1, team 1, armed, effect 0)
    // stands on point 1, owned by team 1 from the start; point 2 (neutral) is where A's AI goes when it has nothing else
    // to do. Target B (key 2, team 2, unarmed) is alone in squad 2 and moves only on squad commands (standing orders).
    internal static class A3a
    {
        internal const ulong KeyA = 1, KeyB = 2;
        internal const int Think = 10;
        internal static readonly SimVec3 AAt = A2.Cell(22, 10), Point2 = A2.Cell(34, 4);
        internal static bool Grass(int x, int z) => z >= 20;

        // The content layer: A2's surfaces with the grass concealing to 100/1000, and the ai section when given.
        internal static string PackJson(string ai) => "{ \"id\": \"a3a-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"dirt\" }, { \"id\": \"grass\", \"speedMilli\": 800, \"concealMilli\": 100 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }" + (ai == null ? "" : ", \"ai\": " + ai) + " }";
        internal static string Ai(int memory, int? search = null, int failed = 0) => "{ \"targetMemoryTicks\": " + memory
            + (search == null ? "" : ", \"searchTicks\": " + search.Value) + (failed == 0 ? "" : ", \"failedSlotTicks\": " + failed) + " }";
        internal static ContentPack Load(string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["a3a-test"] = json }, FileContentSource.Rwr()), "a3a-test");

        internal static MatchPack Match() =>
            new MatchPack(new[] { new ControlPointDef(1, AAt, SimFix.Ratio(1, 2), initialOwner: 1), new ControlPointDef(2, Point2, SimFix.Ratio(1, 2)) },
                new[] { new SpawnAnchor(1, A2.Cell(1, 10)), new SpawnAnchor(2, A2.Cell(38, 10)) },
                2, 1, false, 100000, InterruptMode.Reset, 1, 90, Think, 9000, A2.Sight, 4, 4, SimFix.Ratio(3, 2));
        internal static UnitSpawn Observer() => A2.Soldier(KeyA, 1, AAt, 0, 3000);
        internal static UnitSpawn Target(SimVec3 at) => new UnitSpawn(KeyB, 0, 2, at.X.Raw * 1000 / A2.S, at.Z.Raw * 1000 / A2.S, 3000, 2, -1, false);
        internal static WorldRecipe Recipe(UnitSpawn[] units, ContentPack pack) =>
            new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithMatch(Match()).WithCombat(A2.Combat(0)).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId).WithTerrainRules(pack).WithStandingOrders();
        internal static WcMap Map((int, int, int, int)[] walls = null) => A2.Map(walls ?? A2.DWalls, Grass);
        internal static M6World World(SimVec3 bStart, string ai, (int, int, int, int)[] walls = null, bool reverse = false) =>
            new M6World(Recipe(new[] { Observer(), Target(bStart) }, Load(PackJson(ai))), WcMapWriter.Write(Map(walls)), reverse);

        internal static InputFrame Command(SquadCommandKind kind, int squad, SimVec3 at = default) =>
            new InputFrame(default, default, squad: new SquadCommand(kind, squad, at, default));
        // A scripted command when there is one, otherwise a hold for B once its move ended (InterimOrders ends a move
        // standing on arrival, after which B's own AI would take it back).
        internal static InputFrame Drive(M6World w, InputFrame scripted)
        {
            if (scripted.Squad.HasValue) return scripted;
            return w.Has<SquadStanding>(KeyB) && w.Read<SquadStanding>(KeyB).Kind == StandingKind.Move && !w.Read<MoveIntent>(KeyB).Active
                ? Command(SquadCommandKind.Hold, 2) : default;
        }

        // B holds from tick 1 and gets its only move command on tick MoveTick.
        internal const long MoveTick = 20;
        internal static readonly SimVec3 BStart = A2.Cell(22, 17), BHide = A2.Cell(22, 26);
        internal static InputFrame Script(long tick, SimVec3 to, long moveTick = MoveTick) =>
            tick == 1 ? Command(SquadCommandKind.Hold, 2) : tick == moveTick ? Command(SquadCommandKind.MoveTo, 2, to) : default;

        internal static int SearchCount(M6World w) =>
            (int)typeof(TargetSearch).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<TargetSearch>());
        // The search entry (Seen, Since) of A, read from the private dictionary; false without one.
        internal static bool Entry(M6World w, out long seen, out long since)
        {
            var search = w.System<TargetSearch>();
            object dict = typeof(TargetSearch).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(search);
            var args = new object[] { Id(w, KeyA).Value, null };
            bool found = (bool)dict.GetType().GetMethod("TryGetValue").Invoke(dict, args);
            seen = since = 0;
            if (!found) return false;
            Type entry = args[1].GetType();
            seen = (long)entry.GetField("Seen", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(args[1]);
            since = (long)entry.GetField("Since", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(args[1]);
            return true;
        }

        internal static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));
        internal static long FirstThink(M6World w, ulong key)
        {
            long first = (Think - (long)(Id(w, key).Value % Think)) % Think;
            return first == 0 ? Think : first;
        }
        internal static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        internal static bool Order(M6World w, out SimVec3 target) => L4.Moves(w).TryGetOrder(Id(w, KeyA), out target);
        internal static SimWide Flat(SimVec3 a, SimVec3 b) => CombatSystem.Horizontal(a - b).LengthSquared;
        internal static bool Same(SimVec3 a, SimVec3 b) => a.X.Raw == b.X.Raw && a.Z.Raw == b.Z.Raw;
        // SimpleAiSystem's arrival test: horizontal distance squared <= (1/4 m)^2.
        internal static bool Arrived(SimVec3 a, SimVec3 b) =>
            CombatSystem.Horizontal(a - b).LengthSquared <= new SimVec3(SimFix.Ratio(1, 4), default, default).LengthSquared;

        internal static bool Recall(M6World w, long tick, out StableId target, out SimVec3 at, out long seen) =>
            w.System<TargetMemory>().TryRecall(Id(w, KeyA), tick, out target, out at, out seen);
        internal static int MemoryCount(M6World w) =>
            (int)typeof(TargetMemory).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<TargetMemory>());

        internal static ulong Digest(byte[] bytes)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in bytes) h = (h ^ b) * 1099511628211UL;
            return h;
        }
    }
}
