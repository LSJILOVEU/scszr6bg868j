using System;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // ML-11b-1 fixtures: Ml11aWorld's house with AI rules that remember, search, suppress or chase. Nobody is sent to a point
    // unless the test says so (Ml11aWorld.Sent), so a unit moves only for what the test is about; the AI thinks every tick.
    internal static class Ml11b1World
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        internal const ulong A = 1, B = 10;
        // Far from the house on the ground: out of an 8 m rifle's reach from anywhere near it.
        internal const long FarX = 70500, FarZ = 5500;

        // One rifle (index 0) that reaches `range` metres: a shot every tick, 30 rounds, no spread, no damage; and the melee specs.
        internal static CombatPack Guns(int range, params MeleeSpec[] melee) =>
            new CombatPack(new[] { new WeaponDef(1000, 30, 60000, SimFix.Ratio(range, 2), SimFix.FromInt(range), 32768) }, new ZeroResolver(),
                new NearestVisibleEnemy(), 100 * SimFix.Scale, melee: melee.Length == 0 ? null : melee);

        // The house with a wall on the ground under the floor's cell (41, 32): on the floor the cell is walked on, under it not.
        internal static byte[] Pillar() => Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null,
            new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M), Ml4Maps.Slope(2, 36, 31, 4, 2, "x", 0, 3 * M) }, new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) },
            new[] { Ml4Maps.Wall(1, 41, 32, 1, 1, 2 * M) }));
        // The same wall on a map without plates.
        internal static byte[] WallOnly() => Ml7Worlds.Bytes(Ml4Maps.Make(0, 80, 40, null, new MapPlate[0], null, new[] { Ml4Maps.Wall(1, 41, 32, 1, 1, 2 * M) }));

        // A soldier without a gun that carries melee spec 0.
        internal static UnitSpawn Blade(ulong key, long xMm, long zMm, int layer = 0, int team = 1) =>
            new UnitSpawn(key, 0, team, xMm, zMm, 3000, team, -1, false, 0, layer: layer);

        // One point on the ground far from the house (index 0), unless points are given. fair: the rules come as the terrain
        // pack (the only place fair decisions are read from) and must then name fairDecisions themselves.
        internal static M6World World(IEnumerable<UnitSpawn> units, string ai, CombatPack combat, bool stair = true, byte[] map = null,
            bool plates = true, ControlPointDef[] points = null, bool fair = false)
        {
            MatchPack pack = M6World.Pack(points ?? new[] { PointDown(1, 60500, 20500) }, ticks: 100000, interval: 1000000, think: 1,
                bases: Bases(new SpawnAnchor(1, P(5, 30))));
            WorldRecipe recipe = M6World.Endless(Recipe(units, pack, false)).WithCombat(combat);
            recipe = fair ? recipe.WithPack(Rules(ai)) : recipe.WithPack(Rules(ai));
            if (plates) recipe = recipe.WithPlatesPreview();
            var w = new M6World(recipe, map ?? (plates ? House(stair) : Ml9bMatchTests.Arena(false)));
            Install(w, "dispatch", new Sent());
            return w;
        }

        // Sends the unit to a point (by index) from its next think on.
        internal static void Send(M6World w, ulong key, int point) => ((Sent)Dispatch(w)).To[Id(w, key).Value] = point;

        internal static TargetMemory Memory(M6World w) => w.System<TargetMemory>();

        // Writes the observer's memory: it saw the target at this place on this layer at this tick.
        internal static void Plant(M6World w, ulong observer, ulong target, SimVec3 at, int layer, long seen = 0) =>
            Plant(Memory(w), Id(w, observer), Id(w, target), at, layer, seen);

        internal static void Plant(TargetMemory memory, StableId observer, StableId target, SimVec3 at, int layer, long seen)
        {
            object entries = typeof(TargetMemory).GetField("entries", Private).GetValue(memory);
            Type type = typeof(TargetMemory).GetNestedType("Entry", BindingFlags.NonPublic);
            object entry = Activator.CreateInstance(type);
            type.GetField("Target", Private).SetValue(entry, target);
            type.GetField("At", Private).SetValue(entry, at);
            type.GetField("Seen", Private).SetValue(entry, seen);
            type.GetField("Layer", Private).SetValue(entry, layer);
            entries.GetType().GetProperty("Item").SetValue(entries, entry, new object[] { observer.Value });
        }

        internal static void Saw(TargetMemory memory, StableId observer, EntityId target, long tick) =>
            typeof(TargetMemory).GetMethod("Saw", Private).Invoke(memory, new object[] { observer, target, tick });

        // What the observer remembers now: (known, place, layer, seen tick).
        internal static (bool Known, SimVec3 At, int Layer, long Seen) Recall(M6World w, ulong observer) => Recall(Memory(w), Id(w, observer), w.Sim.Clock.Tick);

        internal static (bool Known, SimVec3 At, int Layer, long Seen) Recall(TargetMemory memory, StableId observer, long tick)
        {
            bool known = memory.TryRecall(observer, tick, out _, out SimVec3 at, out int layer, out long seen);
            return (known, at, layer, seen);
        }

        // The unit's order as (ordered, target, layer); the target's height is the order's own.
        internal static (bool Ordered, SimVec3 Target, int Layer) Order(M6World w, ulong key)
        {
            bool ordered = Ordered(w, key, out SimVec3 target, out int layer);
            return (ordered, target, layer);
        }

        // True when the unit has an order to this place (X and Z) on this layer.
        internal static bool GoesTo(M6World w, ulong key, SimVec3 place, int layer)
        {
            (bool ordered, SimVec3 target, int to) = Order(w, key);
            return ordered && to == layer && target.X.Raw == place.X.Raw && target.Z.Raw == place.Z.Raw;
        }

        // Every unit's layer comes from the world's own part, except the ones the test names.
        internal sealed class Layers : IUnitLayers
        {
            private readonly IUnitLayers inner;
            internal readonly Dictionary<EntityId, int> Of = new Dictionary<EntityId, int>();
            internal Layers(IUnitLayers inner) { this.inner = inner; }
            public int LayerOf(EntityId unit) => Of.TryGetValue(unit, out int layer) ? layer : inner?.LayerOf(unit) ?? 0;
        }
    }
}
