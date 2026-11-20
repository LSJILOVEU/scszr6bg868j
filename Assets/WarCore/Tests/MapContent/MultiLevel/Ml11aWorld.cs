using System;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-11a fixtures. The house (Ml9bMatchTests.Arena): 80 x 40 flat ground; the floor (layer 1) on cells x 40..43, z 30..33,
    // top 3 m, without cell (42, 31); the stair (layer 2) on cells x 36..39, z 31..32, rising 0 -> 3 m toward the floor. The
    // ground under the stair is blocked, under the floor walkable. Seams: ground (35, 31) and (35, 32) <-> the stair's foot,
    // the stair's top <-> the floor's (40, 31) and (40, 32). Every cell costs 1.
    internal static class Ml11aWorld
    {
        internal const long M = SimFix.Scale;
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        // Without the stair the floor has no seam: nothing walks up or down.
        internal static byte[] House(bool stair = true) => stair ? Ml9bMatchTests.Arena() : Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null,
            new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M) }, new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) }));

        // The house with a wall standing on the floor's cell (43, 33): the cell is solid, and nobody walks on it.
        internal static byte[] Walled() => Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null,
            new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M), Ml4Maps.Slope(2, 36, 31, 4, 2, "x", 0, 3 * M) }, new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) },
            new[] { Ml4Maps.Wall(1, 43, 33, 1, 1, 2 * M, 3 * M) }));

        internal static SimVec3 Up(long xMm, long zMm) => Ml9bMatchTests.Up(xMm, zMm);
        internal static SimVec3 Flat(long xMm, long zMm) => Ml9bMatchTests.Flat(xMm, zMm);

        // An unarmed soldier walking 3 m/s.
        internal static UnitSpawn Walker(ulong key, long xMm, long zMm, int layer = 0, int team = 1) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 3000, team, -1, false, layer: layer);

        // A soldier with the fixture's rifle (no damage), and one that stays where it is (1 mm/s, unarmed).
        internal static UnitSpawn Gunner(ulong key, long xMm, long zMm, int layer = 0, int team = 1) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 3000, team, 0, false, layer: layer);
        internal static UnitSpawn Still(ulong key, long xMm, long zMm, int layer = 0, int team = 2) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 1, team, -1, false, layer: layer);

        internal static ControlPointDef PointUp(int id, long xMm, long zMm, int radius = 1, int owner = 0) =>
            new ControlPointDef(id, Up(xMm, zMm), SimFix.FromInt(radius), owner, false, default, 0, 1);
        internal static ControlPointDef PointDown(int id, long xMm, long zMm, int radius = 1, int owner = 0) =>
            new ControlPointDef(id, Flat(xMm, zMm), SimFix.FromInt(radius), owner);

        // A match that never ends; the AI thinks every `think` ticks; capture takes `capture` ticks. Team 1's base is on the
        // ground at (5, 30) unless bases are given.
        internal static M6World World(IEnumerable<UnitSpawn> units, ControlPointDef[] points, int think = 10, bool stair = true, int capture = 3,
            SpawnAnchor[] bases = null, ContentPack ai = null, ContentPack terrain = null, byte[] map = null)
        {
            MatchPack pack = M6World.Pack(points, ticks: capture, interval: 1000000, think: think, bases: bases ?? Bases(new SpawnAnchor(1, P(5, 30))));
            WorldRecipe recipe = M6World.Endless(Recipe(units, pack, false)).WithPlatesPreview();
            if (ai != null) recipe = recipe.WithAiRules(ai);
            if (terrain != null) recipe = recipe.WithTerrainRules(terrain);
            return new M6World(recipe, map ?? House(stair));
        }

        internal static ContentPack Rules(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["ml11a-test"] = "{ \"id\": \"ml11a-test\", \"extends\": \"rwr\", \"ai\": " + ai + " }"
        }, FileContentSource.Rwr()), "ml11a-test");

        internal static SimpleAiSystem Ai(M6World w) => w.System<SimpleAiSystem>();
        internal static T Part<T>(M6World w, string field) => (T)typeof(SimpleAiSystem).GetField(field, Private).GetValue(Ai(w));
        internal static AiField Field(M6World w) => Part<AiField>(w, "field");
        internal static GuardSlots Slots(M6World w) => Part<GuardSlots>(w, "slots");
        internal static IMoveOrders Moves(M6World w) => Part<IMoveOrders>(w, "moves");
        internal static ulong Digest(M6World w) => Part<ulong>(w, "policyDigest");
        internal static void Install(M6World w, string field, object part) => typeof(SimpleAiSystem).GetField(field, Private).SetValue(Ai(w), part);

        internal static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));
        internal static int Layer(M6World w, ulong key) => w.Read<VerticalState>(key).Layer;
        internal static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        internal static AiUnit Thinker(M6World w, ulong key) => new AiUnit(Id(w, key), w.Combat.Unit(w.Unit(key)).Team, At(w, key), AiNerve.Calm, Layer(w, key));

        // Within the scheduler's arrival distance (0.25 m on the plane) of a spot.
        private static readonly SimWide Near = new SimVec3(SimFix.Ratio(1, 4), default, default).LengthSquared;
        internal static bool AtSpot(SimVec3 at, SimVec3 spot) => new SimVec3(at.X - spot.X, default, at.Z - spot.Z).LengthSquared <= Near;

        internal static IAiDispatch Dispatch(M6World w) => Part<IAiDispatch>(w, "dispatch");
        internal static int Pick(M6World w, ulong key) => Dispatch(w).Pick(Thinker(w, key), Field(w));
        // The walking cost to a point from a place, -1 when it cannot be walked to.
        internal static long Cost(M6World w, int point, SimVec3 from, int layer)
        {
            var reach = (IPointReach)typeof(AiField).GetField("reach", Private).GetValue(Field(w));
            return reach.TryCost(point, from, layer, out long cost) ? cost : -1;
        }
        internal static bool Ordered(M6World w, ulong key) => Moves(w).TryGetOrder(Id(w, key), out _);
        internal static bool Ordered(M6World w, ulong key, out SimVec3 target, out int layer) => Moves(w).TryGetOrder(Id(w, key), out target, out layer);
        internal static bool Failed(M6World w, ulong key) => Moves(w).HasFailed(Id(w, key));
        // NearestDispatch's stateless spread: the unit's rank among n points.
        internal static int Rank(M6World w, ulong key, int n) => (int)(AiParts.Mix(Id(w, key).Value ^ 0x3c6ef372fe94f82bUL) % (ulong)n);

        // A dispatch that sends each unit (by its spawn key's stable id) where the test says.
        internal sealed class Sent : IAiDispatch
        {
            internal readonly Dictionary<ulong, int> To = new Dictionary<ulong, int>();
            public ulong Fold(ulong digest) => digest;
            public int Pick(in AiUnit unit, AiField field) => To.TryGetValue(unit.Id.Value, out int point) ? point : -1;
        }

        // A placement that wants every unit at the point's centre.
        internal sealed class Centre : IAiPlacement
        {
            public ulong Fold(ulong digest) => digest;
            public SimVec3 Anchor(in AiUnit unit, int point, AiField field) => field.Center(point);
        }

        // An engage part that answers Fallback to a unit that is asked without an enemy in sight; installed with a pressure
        // level it is not asked at all, so tests call the scheduler's refuge through a unit that sees an enemy.
        internal sealed class Flee : IAiEngage
        {
            public ulong Fold(ulong digest) => digest;
            public AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field) => AiEngage.Fallback;
            public AiEngage Unseen(in AiUnit unit, AiField field) => AiEngage.Fallback;
        }
    }
}
