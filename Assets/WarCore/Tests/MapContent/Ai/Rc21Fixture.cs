using System;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RC2-1: a world with a led squad for the tests: a showcase stage, or a small flat field with units at whole metres.
    internal sealed class LedGame
    {
        public readonly M6World W;
        public readonly LedSquad Led;
        public readonly ulong Player;
        public readonly long SpacingRaw;
        private readonly Dictionary<ulong, ulong> keyOf = new Dictionary<ulong, ulong>();

        // fall: units that die at that tick (a test system, so it has to be there before the first tick).
        public LedGame(string id, long fallTick = 0, params ulong[] fall) : this(StageWorld(id), fallTick, fall) { }

        public LedGame(M6World world, long fallTick = 0, params ulong[] fall)
        {
            W = world;
            if (fall.Length > 0) W.Sim.RegisterGameplay("test.death-" + fallTick, new M6Damage(W, fallTick, fall), SimSystemPhase.Queries);
            Led = W.System<LedSquad>();
            Player = W.Session.ControlledKey.Value;
            SpacingRaw = SimFix.Ratio(W.Recipe.Pack.Section<AiRules>().SlotSpacingMm, 1000).Raw;
            foreach (UnitSpawn unit in W.Recipe.Units) keyOf[StableEntityKey.Placed(1, unit.Key).Id.Value] = unit.Key;
        }

        private static M6World StageWorld(string id)
        {
            ShowcaseStage stage = Stage(id);
            return new M6World(stage.Recipe(), stage.MapBytes());
        }

        public static ShowcaseStage Stage(string id)
        {
            foreach (ShowcaseStage stage in Showcase.Stages) if (stage.Id == id) return stage;
            throw new ArgumentException("no stage " + id);
        }

        // The base cartridge with a led squad of that size and radius and a 2 m formation spacing.
        public static ContentPack SmallPack(int size, int radiusMm) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["rc21-small"] = "{ \"id\": \"rc21-small\", \"extends\": \"rwr\", \"ai\": { \"slotSpacingMm\": 2000, \"ledSize\": " + size
                + ", \"ledRecruitRadiusMm\": " + radiusMm + " } }"
        }, FileContentSource.Rwr()), "rc21-small");

        // The recipe of a flat 80 m by 40 m field under SmallPack with these units, the first of them the player's. Nobody
        // is armed and the AI of this match thinks once in a blue moon, so a unit walks only when the squad walks it.
        public static WorldRecipe SmallRecipe(int size, int radiusMm, UnitSpawn[] units) =>
            M6World.RecipeOf(units).WithRuleSet(MatchMode.TestNeverEndsId).WithPack(SmallPack(size, radiusMm)).WithPlayer(units[0].Key);

        // walls: boxes of the field (x, z, width, depth in metres), two metres high.
        public static M6World SmallWorld(int size, int radiusMm, UnitSpawn[] units, params (int x, int z, int w, int d)[] walls) =>
            new M6World(SmallRecipe(size, radiusMm, units), WcMapWriter.Write(M3TestWorld.Boxes(80, 40, walls)));

        public static LedGame Small(int size, int radiusMm, UnitSpawn[] units, params (int x, int z, int w, int d)[] walls) =>
            new LedGame(SmallWorld(size, radiusMm, units, walls));

        // A soldier of the small field at whole metres, walking 6 m a second.
        public static UnitSpawn Soldier(ulong key, int x, int z, int team = 1) => M6World.U(key, team, x, z, speed: 6000);

        public StableId Id(ulong key) => W.Sim.Entities.StableOf(W.Unit(key));
        public SimVec3 At(ulong key) => W.Read<Position>(key).Value;
        public bool OnField(ulong key) => W.Has<Position>(key) && !W.Dead(key);
        public int Team(ulong key) => W.Combat.Unit(W.Unit(key)).Team;
        public bool Walking(ulong key) => W.Read<MoveIntent>(key).Active;
        public IEnumerable<ulong> Keys => keyOf.Values;
        public ulong[] Members()
        {
            var result = new ulong[Led.MemberCount];
            for (int i = 0; i < result.Length; i++) result[i] = keyOf[Led.MemberAt(i)];
            return result;
        }
        public void Step(InputFrame frame, int count = 1) { for (int i = 0; i < count; i++) W.Sim.Step(frame); }
        public void Wait(int count) => Step(default, count);
        public void Order(SquadCommandKind kind, SimVec3 point = default, int layer = 0) => W.Sim.Step(Command(kind, point, layer));
        // Members standing within half a metre of their place.
        public int InPlace()
        {
            int count = 0;
            foreach (ulong key in Members())
                if (Led.TryPlace(Id(key), out SimVec3 place) && Metres(At(key), place) <= 0.5) count++;
            return count;
        }

        // The squad's own way to the world's move orders (a private field): read to ask the world what the squad asks, set to
        // put a counting or refusing stand-in between them.
        public IMoveOrders Moves
        {
            get => (IMoveOrders)MovesField.GetValue(Led);
            set => MovesField.SetValue(Led, value);
        }
        private static readonly FieldInfo MovesField = typeof(LedSquad).GetField("moves", BindingFlags.NonPublic | BindingFlags.Instance);

        public static InputFrame Command(SquadCommandKind kind, SimVec3 point = default, int layer = 0) =>
            new InputFrame(default, default, squad: new SquadCommand(kind, SquadCommand.Led, point, default, layer));

        public static InputFrame Walk(int xMilli, int zMilli) =>
            new InputFrame(new SimVec3(SimFix.Ratio(xMilli, 1000), default, SimFix.Ratio(zMilli, 1000)), default);

        // The player's walk input straight towards the point.
        public InputFrame Towards(SimVec3 point)
        {
            SimVec3 at = At(Player);
            double x = (point.X.Raw - at.X.Raw) / (double)SimFix.Scale, z = (point.Z.Raw - at.Z.Raw) / (double)SimFix.Scale, far = Math.Sqrt(x * x + z * z);
            return far < 1e-6 ? default : Walk((int)Math.Round(x / far * 1000), (int)Math.Round(z / far * 1000));
        }

        public static double Metres(SimVec3 a, SimVec3 b)
        {
            double x = (a.X.Raw - b.X.Raw) / (double)SimFix.Scale, z = (a.Z.Raw - b.Z.Raw) / (double)SimFix.Scale;
            return Math.Sqrt(x * x + z * z);
        }
    }

    // The world's own move orders with a stand-in's ears and veto: it counts the walk orders and the stops one unit gets,
    // refuses that unit's walk orders the way the world does (a refused unit is left without an order) when told to, and can
    // call units commanded, or nobody.
    internal sealed class LedOrders : IMoveOrders
    {
        private readonly IMoveOrders inner;
        public ulong Unit;
        public bool Refuse, NobodyCommanded;
        public int Orders, Stops;
        // Units (stable identities) this stand-in calls commanded on top of what the world says.
        public readonly HashSet<ulong> Commanded = new HashSet<ulong>();
        public LedOrders(IMoveOrders inner) { this.inner = inner; }
        public bool CanStandAt(SimVec3 at) => inner.CanStandAt(at);
        public bool IsCommanded(StableId unit) => !NobodyCommanded && (Commanded.Contains(unit.Value) || inner.IsCommanded(unit));
        public void Order(StableId unit, SimVec3 target) => inner.Order(unit, target);
        public void Stop(StableId unit)
        {
            if (unit.Value == Unit) Stops++;
            inner.Stop(unit);
        }
        public bool TryGetOrder(StableId unit, out SimVec3 target) => inner.TryGetOrder(unit, out target);
        public bool HasFailed(StableId unit) => inner.HasFailed(unit);
        public bool TryRouteCost(SimVec3 from, SimVec3 to, long limitCost, out long cost) => inner.TryRouteCost(from, to, limitCost, out cost);
        public bool CanStandAt(SimVec3 at, int layer) => inner.CanStandAt(at, layer);
        public void Order(StableId unit, SimVec3 target, int layer)
        {
            if (unit.Value == Unit)
            {
                Orders++;
                if (Refuse) { inner.Stop(unit); return; }
            }
            inner.Order(unit, target, layer);
        }
        public bool TryGetOrder(StableId unit, out SimVec3 target, out int layer) => inner.TryGetOrder(unit, out target, out layer);
        public bool TryStandAt(SimVec3 at, int layer, out SimVec3 grounded) => inner.TryStandAt(at, layer, out grounded);
    }
}
