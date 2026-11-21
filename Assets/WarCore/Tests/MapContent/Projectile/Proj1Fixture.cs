using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 fixtures: a bodied combat world built directly (as Ml3World) with the projectile system registered when the pack
    // has a projectile weapon. Units stand on feet given in raw units; walls are boxes; ground and plates are optional.
    internal sealed class Proj1World
    {
        internal const long M = SimFix.Scale;
        internal const uint UnitLayer = 1u, WallLayer = 2u;

        // A no-spread weapon: falloff start..end metres, 1 s between shots by default, 30 rounds.
        internal static WeaponDef Weapon(int falloffEnd = 60, long intervalMilli = 30000, long effectRaw = 10 * M, int magazine = 30,
            long reloadMilli = 60000, int falloffStart = 0) =>
            new WeaponDef(intervalMilli, magazine, reloadMilli, SimFix.FromInt(falloffStart), SimFix.FromInt(falloffEnd), effectRaw);

        // One weapon of the given speed (0 = instant), a body (the RWR placeholder by default) and a deterministic resolver.
        internal static CombatPack Pack(int speed, WeaponDef? weapon = null, IHitResolver resolver = null, bool friendlyFire = false,
            CombatBody? body = null, bool noBody = false) =>
            Pack(new[] { weapon ?? Weapon() }, new[] { speed }, resolver, friendlyFire, body, noBody);

        internal static CombatPack Pack(WeaponDef[] weapons, int[] speeds, IHitResolver resolver = null, bool friendlyFire = false,
            CombatBody? body = null, bool noBody = false) =>
            new CombatPack(weapons, resolver ?? new FalloffDamage(), new NearestVisibleEnemy(), 100 * M, friendlyFire,
                body: noBody ? (CombatBody?)null : body ?? Ml3World.Rwr, projectileSpeeds: speeds);

        internal readonly struct Unit
        {
            internal readonly long X, Y, Z;
            internal readonly int Team, Weapon;
            internal Unit(long x, long y, long z, int team, int weapon = 0) { X = x; Y = y; Z = z; Team = team; Weapon = weapon; }
        }
        internal static Unit U(long x, long z, int team, int weapon = 0, long y = 0) => new Unit(x, y, z, team, weapon);

        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly ProjectileSystem Projectiles;
        internal readonly CombatPack UsedPack;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Combatant> Combatants;
        internal readonly int Count, Pool;
        internal Action<SimContext> Produce;
        private readonly EntityId[] units;

        // pool: the projectile pool (default: the pack's bound for these units, at least 1); schemaPool: what the world's
        // damage limits are sized for (default: the pack's bound).
        internal Proj1World(CombatPack pack, Unit[] list, Ml3World.Wall[] walls = null, HeightField ground = null, PlateField plates = null,
            int? pool = null, int? schemaPool = null, ulong seed = 41, IDamageGuard guard = null)
        {
            walls = walls ?? Array.Empty<Ml3World.Wall>();
            uint wallMask = walls.Length == 0 ? 0u : WallLayer;
            UsedPack = pack; Count = list.Length;
            var weapons = new int[list.Length];
            for (int i = 0; i < list.Length; i++) weapons[i] = list[i].Weapon;
            int bound = pack.ProjectileCapacity(weapons);
            Pool = pool ?? Math.Max(1, bound);
            int sized = schemaPool ?? bound;
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            for (int i = 0; i < walls.Length; i++) entries.Add(new PlacedCatalogEntry(2, (ulong)i + 1));
            for (int i = 0; i < list.Length; i++) entries.Add(new PlacedCatalogEntry(1, (ulong)i + 1));
            Sim = Simulation.CreateRetainedWorld(seed, new PlacedCatalog(content, entries.ToArray()), pack.BuildConfig(entries.Count, Count, sized),
                content: content, gameplaySchema: pack.BuildSchema(Count, sized));
            Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            Combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            Sim.ConfigureSpatial(Positions, colliders, SimFix.FromInt(2), footAnchoredLayers: pack.Body.HasValue ? UnitLayer : 0u);
            Combat = new CombatSystem(Sim, Positions, Combatants, pack, wallMask, UnitLayer, Count, null, ground, plates);
            if (guard != null) Combat.SetDamageGuard(guard);
            Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
            if (pack.HasProjectiles)
            {
                Projectiles = new ProjectileSystem(Sim, Combat, Pool);
                Sim.RegisterGameplay("combat.projectiles", Projectiles, SimSystemPhase.Queries);
            }
            Sim.RegisterGameplay("proj1.producer", new Script(this), SimSystemPhase.Queries);
            for (int i = 0; i < walls.Length; i++)
            {
                Ml3World.Wall w = walls[i];
                EntityId id = Sim.Entities.Create(StableEntityKey.Placed(2, (ulong)i + 1));
                long height = w.Top - w.Bottom;
                Sim.Entities.Set(Positions, id, new Position(Raw((w.X0 + w.X1) / 2, w.Bottom + height / 2, (w.Z0 + w.Z1) / 2)));
                Sim.Entities.Set(colliders, id, Collider.Box(SimFix.FromRaw((w.X1 - w.X0) / 2), SimFix.FromRaw(height), SimFix.FromRaw((w.Z1 - w.Z0) / 2),
                    BoxYaw.Identity, layer: WallLayer, isStatic: true));
            }
            long unitHeight = pack.Body?.Height.Raw ?? M;
            units = new EntityId[Count];
            for (int i = 0; i < Count; i++)
            {
                Unit u = list[i];
                EntityId id = units[i] = Sim.Entities.Create(StableEntityKey.Placed(1, (ulong)i + 1));
                Sim.Entities.Set(Positions, id, new Position(Raw(u.X, u.Y, u.Z)));
                Sim.Entities.Set(colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromRaw(unitHeight)));
                Sim.BindGameplay(id, 1);
                Sim.Entities.Set(Combatants, id, new Combatant(u.Team, u.Weapon, u.Weapon < 0 ? 0 : pack.Weapons[u.Weapon].Magazine, 0));
            }
            Sim.CompleteSpatialBoundary();
        }

        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        internal EntityId Entity(int i) => units[i];
        internal StableId Id(int i) => Sim.Entities.StableOf(units[i]);
        internal SimVec3 Point(int i) => Sim.Entities.Read(Positions, units[i]).Value;
        internal Combatant State(int i) => Sim.Entities.Read(Combatants, units[i]);
        internal long Health(int i) => Sim.GameplayResource(Id(i), 1);
        internal bool Dead(int i) => Sim.GameplayLife(Id(i)).State == GameplayLifeState.Dead;
        internal void Move(int i, long x, long y, long z) => Sim.Entities.Set(Positions, units[i], new Position(Raw(x, y, z)));
        internal void SetDue(int i, long due) { var u = State(i); Sim.Entities.Set(Combatants, units[i], new Combatant(u.Team, u.Weapon, u.Ammo, due)); }
        internal long Tick => Sim.Clock.Tick;
        internal void Step() => Sim.Step(default);
        // Steps once and returns this tick's projectile landings.
        internal ProjectileImpact[] StepImpacts() { Step(); return Projectiles.Impacts.ToArray(); }

        // Steps until a projectile lands (at most limit ticks) and returns the tick it landed on and the landing.
        internal (long Tick, ProjectileImpact Impact) StepUntilImpact(int limit = 1000)
        {
            for (int t = 0; t < limit; t++)
            {
                Step();
                if (Projectiles.Impacts.Length > 0) return (Tick, Projectiles.Impacts[0]);
            }
            throw new InvalidOperationException("No projectile landed within " + limit + " ticks.");
        }

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly Proj1World world;
            internal Script(Proj1World world) { this.world = world; }
            public int Order => 29;
            public string StateTypeId => "proj1.producer";
            public int StateVersion => 1;
            public void Step(SimContext c) => world.Produce?.Invoke(c);
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { h.Add(StateTypeId); }
        }
    }

    // Damage = effect * falloff (no roll), so a landing's damage tells its falloff distance.
    internal sealed class FalloffDamage : IHitResolver
    {
        public ulong PolicyKey => 0x50524F4A00000001;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => weapon.EffectRaw * falloffRaw / SimFix.Scale;
    }

    // Damage = the roll + 1 (never 0), so the order rolls are drawn in shows in who took what.
    internal sealed class RollDamage : IHitResolver
    {
        internal readonly List<int> Rolls = new List<int>();
        public ulong PolicyKey => 0x50524F4A00000002;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) { Rolls.Add(rollRaw); return rollRaw + 1; }
    }

    // Shields every listed victim for every tick.
    internal sealed class ShieldAll : IDamageGuard
    {
        private readonly HashSet<StableId> shielded = new HashSet<StableId>();
        internal void Add(StableId id) => shielded.Add(id);
        public bool Shields(StableId victim, long tick) => shielded.Contains(victim);
    }
}
