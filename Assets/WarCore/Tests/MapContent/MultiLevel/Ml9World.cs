using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-9 fixtures: the ML-3 direct combat world (no WorldBuilder) with floor plates handed to the combat system. Feet are
    // placed in 3D; walls are boxes from bottom to top; ground and plates are optional.
    internal sealed class Ml9World
    {
        internal const long M = SimFix.Scale;
        internal const uint UnitLayer = 1u, WallLayer = 2u;
        // The first floor of the fixture plate: top 3 m, 0.5 m thick (bottom 2.5 m), over x, z in [8, 16) m with a cutout at
        // cell (12, 12) (x, z in [12, 13) m).
        internal const long Top = 3 * M, Thick = M / 2, Bottom = Top - Thick;

        internal static PlateField Floor(int width = 32, int depth = 32) =>
            Ml5Plates.Field(width, depth, 0, 0, Ml5Plates.Flat(1, 8, 8, 8, 8, Top, Thick, (4, 4)));

        internal static CombatPack Pack(CombatBody? body, MeleeSpec[] melee = null, bool friendlyFire = false, IHitResolver resolver = null,
            ITargetSelector selector = null, BlockSpec[] block = null) =>
            new CombatPack(new[] { Ml3World.Rifle() }, resolver ?? new ZeroResolver(), selector ?? new NearestVisibleEnemy(), 100 * M, friendlyFire,
                melee: melee, block: block, body: body);

        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Combatant> Combatants;
        internal Action<SimContext> Produce;
        private readonly EntityId[] units;

        internal Ml9World(CombatPack pack, Ml3World.Unit[] list, PlateField plates, Ml3World.Wall[] walls = null, HeightField ground = null,
            ulong seed = 41)
        {
            walls = walls ?? Array.Empty<Ml3World.Wall>();
            uint wallMask = walls.Length == 0 ? 0u : WallLayer;
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            for (int i = 0; i < walls.Length; i++) entries.Add(new PlacedCatalogEntry(2, (ulong)i + 1));
            for (int i = 0; i < list.Length; i++) entries.Add(new PlacedCatalogEntry(1, (ulong)i + 1));
            Sim = Simulation.CreateRetainedWorld(seed, new PlacedCatalog(content, entries.ToArray()), pack.BuildConfig(entries.Count, list.Length),
                content: content, gameplaySchema: pack.BuildSchema(list.Length));
            Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            Combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            Sim.ConfigureSpatial(Positions, colliders, SimFix.FromInt(2), footAnchoredLayers: pack.Body.HasValue ? UnitLayer : 0u);
            Combat = new CombatSystem(Sim, Positions, Combatants, pack, wallMask, UnitLayer, list.Length, null, ground, plates);
            Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
            Sim.RegisterGameplay("ml9.producer", new Script(this), SimSystemPhase.Queries);
            for (int i = 0; i < walls.Length; i++)
            {
                Ml3World.Wall w = walls[i];
                EntityId id = Sim.Entities.Create(StableEntityKey.Placed(2, (ulong)i + 1));
                long height = w.Top - w.Bottom;
                Sim.Entities.Set(Positions, id, new Position(Raw((w.X0 + w.X1) / 2, w.Bottom + height / 2, (w.Z0 + w.Z1) / 2)));
                Sim.Entities.Set(colliders, id, Collider.Box(SimFix.FromRaw((w.X1 - w.X0) / 2), SimFix.FromRaw(height), SimFix.FromRaw((w.Z1 - w.Z0) / 2),
                    BoxYaw.Identity, layer: WallLayer, isStatic: true));
            }
            long height0 = pack.Body?.Height.Raw ?? M;
            units = new EntityId[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                Ml3World.Unit u = list[i];
                EntityId id = units[i] = Sim.Entities.Create(StableEntityKey.Placed(1, (ulong)i + 1));
                Sim.Entities.Set(Positions, id, new Position(Raw(u.X, u.Y, u.Z)));
                Sim.Entities.Set(colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromRaw(height0)));
                Sim.BindGameplay(id, 1);
                Sim.Entities.Set(Combatants, id, new Combatant(u.Team, u.Weapon, u.Weapon < 0 ? 0 : pack.Weapons[u.Weapon].Magazine, 0));
            }
            Sim.CompleteSpatialBoundary();
        }

        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        // A unit at (x, z) in millimetres with its foot at y (raw).
        internal static Ml3World.Unit U(long xMm, long y, long zMm, int team, int weapon = 0) =>
            new Ml3World.Unit(xMm * M / 1000, y, zMm * M / 1000, team, weapon);
        internal EntityId Unit(int i) => units[i];
        internal StableId Id(int i) => Sim.Entities.StableOf(units[i]);
        internal SimVec3 Point(int i) => Sim.Entities.Read(Positions, units[i]).Value;
        internal void Step() => Sim.Step(default);
        internal FireRecord[] StepFires() { Step(); return Combat.Fires.ToArray(); }
        // The fires of one shooter this tick (others may fire at what they see).
        internal FireRecord[] StepFires(int shooter) => Array.FindAll(StepFires(), f => f.Shooter.Equals(Id(shooter)));

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly Ml9World world;
            internal Script(Ml9World world) { this.world = world; }
            public int Order => 29;
            public string StateTypeId => "ml9.producer";
            public int StateVersion => 1;
            public void Step(SimContext c) => world.Produce?.Invoke(c);
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { h.Add(StateTypeId); }
        }
    }
}
