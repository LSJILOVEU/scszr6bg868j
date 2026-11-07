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
    // A3 fixtures. The content layer is A2's literal (A2Fixture.cs PackJson) with the ai section appended when given;
    // everything else that shapes behaviour comes from A2's fixture (map, weapon, match, seed) or is written here.
    internal static class A3
    {
        internal const string Grass500 = ", \"concealMilli\": 500";

        // memory == null leaves the ai key out; otherwise "ai": { "targetMemoryTicks": memory }.
        internal static string PackJson(string grass, int? memory) => "{ \"id\": \"a2-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"dirt\" }, { \"id\": \"grass\", \"speedMilli\": 800" + grass + " } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }"
            + (memory == null ? "" : ", \"ai\": { \"targetMemoryTicks\": " + memory.Value + " }") + " }";
        internal static string PackJson(string grass, string ai) => "{ \"id\": \"a2-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"dirt\" }, { \"id\": \"grass\", \"speedMilli\": 800" + grass + " } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }, \"ai\": " + ai + " }";
        internal static ContentPack Load(string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { ["a2-test"] = json }, FileContentSource.Rwr()), "a2-test");
        internal static ContentPack Pack(string grass, int? memory) => Load(PackJson(grass, memory));

        // ---- Memory fixture: A2's map with its wall (A2Fixture.cs DWalls), weapon effect 0, grass 500 (Lc = 10 m, L = 20 m). ----
        // Observer A (key 1, team 1, armed) stands on the only control point, which team 1 owns from the start: A has no point
        // to walk to, so its AI never moves it. Targets (team 2, unarmed) move only on squad commands (standing orders): B is
        // key 2 alone in squad 2, C is key 3 alone in squad 3.
        internal const ulong KeyA = 1, KeyB = 2, KeyC = 3;
        internal static readonly SimVec3 AAt = A2.Cell(22, 10);
        internal static MatchPack Match() =>
            new MatchPack(new[] { new ControlPointDef(1, AAt, SimFix.Ratio(1, 2), initialOwner: 1) },
                new[] { new SpawnAnchor(1, A2.Cell(1, 10)), new SpawnAnchor(2, A2.Cell(38, 10)) },
                2, 1, false, 100000, InterruptMode.Reset, 1, 90, A2.Think, 9000, A2.Sight, 4, 4, SimFix.Ratio(3, 2));
        internal static UnitSpawn Target(ulong key, int squad, SimVec3 at) =>
            new UnitSpawn(key, 0, squad, at.X.Raw * 1000 / A2.S, at.Z.Raw * 1000 / A2.S, 3000, 2, -1, false);
        internal static UnitSpawn Observer() => A2.Soldier(KeyA, 1, AAt, 0, 3000);
        internal static WorldRecipe Recipe(UnitSpawn[] units, ContentPack pack) =>
            new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithMatch(Match()).WithCombat(A2.Combat(0)).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId).WithTerrainRules(pack).WithStandingOrders();
        internal static M6World World(UnitSpawn[] units, int? memory, bool reverse = false) =>
            new M6World(Recipe(units, Pack(Grass500, memory)), WcMapWriter.Write(A2.Map(A2.DWalls)), reverse);

        internal static InputFrame Command(SquadCommandKind kind, int squad, SimVec3 at = default) =>
            new InputFrame(default, default, squad: new SquadCommand(kind, squad, at, default));

        internal static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));

        // The first thinking tick of a unit (SimpleAiSystem.cs: (tick + id % T) % T == 0), in 1..T.
        internal static long FirstThink(M6World w, ulong key)
        {
            long first = (A2.Think - (long)(Id(w, key).Value % A2.Think)) % A2.Think;
            return first == 0 ? A2.Think : first;
        }

        // Entry count of the registered memory (internal; the Battle assembly has no InternalsVisibleTo).
        internal static int Count(M6World w) =>
            (int)typeof(TargetMemory).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<TargetMemory>());

        internal static bool Recall(M6World w, ulong observer, long tick, out StableId target, out SimVec3 at, out long seen) =>
            w.System<TargetMemory>().TryRecall(Id(w, observer), tick, out target, out at, out seen);

        internal static SimWide Squared(int metres) => new SimVec3(SimFix.FromInt(metres), default, default).LengthSquared;
        internal static SimWide Flat(SimVec3 a, SimVec3 b) => CombatSystem.Horizontal(a - b).LengthSquared;

        // 64-bit FNV-1a over the bytes (constants as A2Fixture.cs HashRun).
        internal static ulong Digest(byte[] bytes)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in bytes) h = (h ^ b) * 1099511628211UL;
            return h;
        }
    }
}
