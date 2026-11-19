using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-3 fixtures: a combat world built directly (no WorldBuilder), with feet placed in 3D, box walls given by their
    // bottom and top, an optional ground and every body switch under the test's control.
    internal sealed class Ml3World
    {
        internal const long M = SimFix.Scale;
        internal const uint UnitLayer = 1u, WallLayer = 2u;
        // The RWR placeholder body: height 1.75 m, eye 1.5 m, muzzle 1.5 m.
        internal static readonly CombatBody Rwr = Body(114688, 98304, 98304);
        internal static CombatBody Body(long height, long eye, long muzzle) =>
            new CombatBody(SimFix.FromRaw(height), SimFix.FromRaw(eye), SimFix.FromRaw(muzzle));

        // One no-spread rifle: 1 s between shots, 30 rounds, falloff 30..60 m (full damage inside 30 m).
        internal static WeaponDef Rifle(long effectRaw = 10 * M) => new WeaponDef(30000, 30, 60000, SimFix.FromInt(30), SimFix.FromInt(60), effectRaw);
        internal static CombatPack Pack(CombatBody? body, IHitResolver resolver = null, MeleeSpec[] melee = null, bool friendlyFire = false) =>
            new CombatPack(new[] { Rifle() }, resolver ?? new ZeroResolver(), new NearestVisibleEnemy(), 100 * M, friendlyFire, melee: melee, body: body);

        internal readonly struct Unit
        {
            internal readonly long X, Y, Z;
            internal readonly int Team, Weapon;
            internal Unit(long x, long y, long z, int team, int weapon = 0) { X = x; Y = y; Z = z; Team = team; Weapon = weapon; }
        }
        // A wall box over [x0, x1] x [z0, z1] (raw) from bottom to top (raw Y).
        internal readonly struct Wall
        {
            internal readonly long X0, Z0, X1, Z1, Bottom, Top;
            internal Wall(long x0, long z0, long x1, long z1, long bottom, long top) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Bottom = bottom; Top = top; }
        }

        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly CombatPack UsedPack;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Collider> Colliders;
        internal readonly ComponentType<Combatant> Combatants;
        internal readonly int Count;
        internal Action<SimContext> Produce;
        private readonly EntityId[] units;

        // anchored: the spatial index's foot-anchored layers (default: the unit layer exactly when the pack has a body).
        // unitHeight: every unit's collider height (default: the body height, else 1 m). heights: per unit, overrides it.
        internal Ml3World(CombatPack pack, Unit[] list, Wall[] walls = null, HeightField ground = null, uint? anchored = null,
            long? unitHeight = null, long[] heights = null, ulong seed = 41, WcMap map = null)
        {
            walls = walls ?? Array.Empty<Wall>();
            int mapWalls = map?.Walls.Count ?? 0;
            uint wallMask = walls.Length == 0 ? 0u : WallLayer;
            if (map != null)
            {
                foreach (WcMapWall w in map.Walls) wallMask |= map.LayerMask(w.LayerName);
                ground = ground ?? TerrainDerivation.Derive(map, null, "ml3").Ground;
            }
            UsedPack = pack; Count = list.Length;
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            for (int i = 0; i < walls.Length; i++) entries.Add(new PlacedCatalogEntry(2, (ulong)i + 1));
            for (int i = 0; i < mapWalls; i++) entries.Add(new PlacedCatalogEntry(map.Walls[i].Chunk, map.Walls[i].Placement));
            for (int i = 0; i < list.Length; i++) entries.Add(new PlacedCatalogEntry(1, (ulong)i + 1));
            Sim = Simulation.CreateRetainedWorld(seed, new PlacedCatalog(content, entries.ToArray()), pack.BuildConfig(entries.Count, Count),
                content: content, gameplaySchema: pack.BuildSchema(Count));
            Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            Colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            Combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            Sim.ConfigureSpatial(Positions, Colliders, SimFix.FromInt(2), footAnchoredLayers: anchored ?? (pack.Body.HasValue ? UnitLayer : 0u));
            Combat = new CombatSystem(Sim, Positions, Combatants, pack, wallMask, UnitLayer, Count, null, ground);
            Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
            Sim.RegisterGameplay("ml3.producer", new Script(this), SimSystemPhase.Queries);
            for (int i = 0; i < walls.Length; i++)
            {
                Wall w = walls[i];
                EntityId id = Sim.Entities.Create(StableEntityKey.Placed(2, (ulong)i + 1));
                long height = w.Top - w.Bottom;
                Sim.Entities.Set(Positions, id, new Position(Raw((w.X0 + w.X1) / 2, w.Bottom + height / 2, (w.Z0 + w.Z1) / 2)));
                Sim.Entities.Set(Colliders, id, Collider.Box(SimFix.FromRaw((w.X1 - w.X0) / 2), SimFix.FromRaw(height), SimFix.FromRaw((w.Z1 - w.Z0) / 2),
                    BoxYaw.Identity, layer: WallLayer, isStatic: true));
            }
            for (int i = 0; i < mapWalls; i++)
            {
                WcMapWall w = map.Walls[i];
                EntityId id = Sim.Entities.Create(w.Key);
                Sim.Entities.Set(Positions, id, new Position(new SimVec3(map.Min.X, map.FlatHeight, map.Min.Z) + w.LocalCenter));
                Sim.Entities.Set(Colliders, id, Collider.Box(w.HalfX, w.Height, w.HalfZ, BoxYaw.Identity, layer: map.LayerMask(w.LayerName), isStatic: true));
            }
            long defaultHeight = unitHeight ?? pack.Body?.Height.Raw ?? M;
            units = new EntityId[Count];
            for (int i = 0; i < Count; i++)
            {
                Unit u = list[i];
                EntityId id = units[i] = Sim.Entities.Create(StableEntityKey.Placed(1, (ulong)i + 1));
                Sim.Entities.Set(Positions, id, new Position(Raw(u.X, u.Y, u.Z)));
                Sim.Entities.Set(Colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromRaw(heights?[i] ?? defaultHeight)));
                Sim.BindGameplay(id, 1);
                Sim.Entities.Set(Combatants, id, new Combatant(u.Team, u.Weapon, u.Weapon < 0 ? 0 : pack.Weapons[u.Weapon].Magazine, 0));
            }
            Sim.CompleteSpatialBoundary();
        }

        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        internal EntityId U(int i) => units[i];
        internal StableId Id(int i) => Sim.Entities.StableOf(units[i]);
        internal SimVec3 Point(int i) => Sim.Entities.Read(Positions, units[i]).Value;
        internal Combatant State(int i) => Sim.Entities.Read(Combatants, units[i]);
        internal void SetDue(int i, long due) { var u = State(i); Sim.Entities.Set(Combatants, units[i], new Combatant(u.Team, u.Weapon, u.Ammo, due)); }
        internal void Step() => Sim.Step(default);

        // Steps once and returns this tick's fire records.
        internal FireRecord[] StepFires() { Step(); return Combat.Fires.ToArray(); }

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly Ml3World world;
            internal Script(Ml3World world) { this.world = world; }
            public int Order => 29;
            public string StateTypeId => "ml3.producer";
            public int StateVersion => 1;
            public void Step(SimContext c) => world.Produce?.Invoke(c);
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { h.Add(StateTypeId); }
        }
    }

    // The new-mode test content pack: rwr plus a body, in memory (no JSON file is added to Content/Rwr/Data).
    internal static class Ml3Content
    {
        internal const string BodyJson = "\"body\": { \"height\": 1.75, \"eye\": 1.5, \"muzzle\": 1.5 }";

        internal static ContentPack Load(string id, params (string Id, string Json)[] layers)
        {
            var map = new Dictionary<string, string>();
            foreach (var (layer, json) in layers) map[layer] = json;
            return ContentLoader.Load(new MemoryContentSource(map, FileContentSource.Rwr()), id);
        }

        // A layer: { "id": id, "extends": parent, <body> }.
        internal static (string, string) Layer(string id, string parent, string body) =>
            (id, "{ \"id\": \"" + id + "\"" + (parent == null ? "" : ", \"extends\": \"" + parent + "\"") + (body.Length == 0 ? "" : ", " + body) + " }");

        // rwr with a combat body (height, eye, muzzle as JSON number text).
        internal static ContentPack Rwr(string height = "1.75", string eye = "1.5", string muzzle = "1.5") =>
            Load("ml3-body", Layer("ml3-body", "rwr", "\"combat\": { \"body\": { \"height\": " + height + ", \"eye\": " + eye + ", \"muzzle\": " + muzzle + " } }"));
    }
}
