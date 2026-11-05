using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class M5TestWorld
    {
        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly InterimLocomotion Locomotion;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Combatant> Combatants;
        internal readonly ComponentType<MoveIntent> Intents;
        internal readonly int Count;
        internal M5TestWorld(UnitSpawn[] units, CombatPack pack = null, bool reverse = false, WcMap map = null, ulong seed = 41, int? candidateCapacity = null)
        {
            pack = pack ?? NoSpread(RwrPack.Create()); Count = units.Length;
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            uint mask = 0;
            if (map != null) foreach (var w in map.Walls) { entries.Add(new PlacedCatalogEntry(w.Chunk, w.Placement)); mask |= map.LayerMask(w.LayerName); }
            foreach (var u in units) entries.Add(new PlacedCatalogEntry(1, u.Key));
            Sim = Simulation.CreateRetainedWorld(seed, new PlacedCatalog(content, entries.ToArray()), pack.BuildConfig(entries.Count, Count),
                content: content, gameplaySchema: pack.BuildSchema(Count));
            Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            Intents = Sim.Entities.Register<MoveIntent>(MoveIntent.ComponentId, new MoveIntentSerializer());
            ComponentType<MoveWaypoint> waypoints = default;
            if (map != null) waypoints = Sim.Entities.Register<MoveWaypoint>(MoveWaypoint.ComponentId, new MoveWaypointSerializer());
            Combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            Combat = new CombatSystem(Sim, Positions, Combatants, pack, mask, 1, candidateCapacity ?? Count);
            Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
            Sim.RegisterGameplay("bridge.dead-cleanup", (IRetainedGameplaySystem)Activator.CreateInstance(typeof(WorldBuilder).Assembly.GetType("WarCore.Presentation.DeadCleanup"),
                Sim, Combatants, Intents), SimSystemPhase.Requests);
            Sim.ConfigureSpatial(Positions, colliders, SimFix.FromInt(2));
            Locomotion = new InterimLocomotion(Sim.Entities, mask, map == null ? null : WcMapNavigation.Create(map), map != null);
            Sim.RegisterGameplay("interim.orders", new InterimOrders(Sim.Entities, Locomotion, map == null ? default : SimFix.FromInt(2)), SimSystemPhase.Requests);
            Sim.RegisterGameplay("interim.locomotion", Locomotion, SimSystemPhase.Movement);
            if (map != null) for (int i = 0; i < map.Walls.Count; i++)
            {
                var w = map.Walls[reverse ? map.Walls.Count - 1 - i : i];
                var id = Sim.Entities.Create(w.Key);
                Sim.Entities.Set(Positions, id, new Position(new SimVec3(map.Min.X, map.FlatHeight, map.Min.Z) + w.LocalCenter));
                Sim.Entities.Set(colliders, id, Collider.Box(w.HalfX, w.Height, w.HalfZ, BoxYaw.Identity, layer: map.LayerMask(w.LayerName), isStatic: true));
            }
            for (int i = 0; i < Count; i++)
            {
                var u = units[reverse ? Count - 1 - i : i];
                var id = Sim.Entities.Create(StableEntityKey.Placed(1, u.Key));
                var point = M3TestWorld.Point(u.XMm, u.ZMm);
                Sim.Entities.Set(Positions, id, new Position(point));
                Sim.Entities.Set(colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(1)));
                Sim.Entities.Set(Intents, id, new MoveIntent(point, SimFix.Ratio(u.SpeedMmPerSec, 30000), u.Squad, false));
                if (map != null) Sim.Entities.Set(waypoints, id, default);
                Sim.BindGameplay(id, 1);
                Sim.Entities.Set(Combatants, id, new Combatant(u.Team, u.Weapon, u.Weapon < 0 ? 0 : pack.Weapons[u.Weapon].Magazine, 0));
            }
            Sim.CompleteSpatialBoundary();
        }
        internal EntityId Unit(int i) => Sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, (ulong)i + 1).Id));
        internal StableId Id(int i) => Sim.Entities.StableOf(Unit(i));
        internal Combatant State(int i) => Sim.Entities.Read(Combatants, Unit(i));
        internal SimVec3 Point(int i) => Sim.Entities.Read(Positions, Unit(i)).Value;
        internal bool Dead(int i) => Combat.IsDead(Unit(i));
        internal void Step() => Sim.Step(default);
        internal void Order(int i, long x, long z) => Locomotion.Order(Unit(i), M3TestWorld.Point(x, z));
        internal void SetDue(int i, long due)
        { var u = State(i); Sim.Entities.Set(Combatants, Unit(i), new Combatant(u.Team, u.Weapon, u.Ammo, due)); }
        internal static UnitSpawn Spawn(int key, long x, long z = 0, int team = 1, int weapon = 0, int squad = 1) =>
            new UnitSpawn((ulong)key, 0, squad, x, z, 6000, team, weapon);
        internal static UnitSpawn[] Pair(int weapon = 0, long distanceMm = 10000, bool armedTarget = false) => new[] {
            Spawn(1, 0, weapon: weapon), Spawn(2, distanceMm, team: 2, weapon: armedTarget ? weapon : -1, squad: 2) };
        internal static CombatPack Pack(IHitResolver resolver, ITargetSelector selector = null, WeaponDef[] weapons = null) =>
            new CombatPack(weapons ?? NoSpread(RwrPack.Create()).Weapons.ToArray(), resolver, selector ?? new NearestVisibleEnemy(), 100 * SimFix.Scale);
        // Test fixtures that check ballistics, cadence or hit statistics need the aim line to be exact: same weapons without spread.
        internal static WeaponDef NoSpread(WeaponDef w) =>
            new WeaponDef(w.FireIntervalMilli, w.Magazine, w.ReloadMilli, w.FalloffStart, w.FalloffEnd, w.EffectRaw);
        // Same pack with the match-level combat rules replaced; everything else (weapons, policies) is kept.
        internal static CombatPack Rules(CombatPack pack, bool friendlyFire, int allyCheckMissRaw, bool locked = false, long holdMilli = 30000) =>
            new CombatPack(pack.Weapons.ToArray(), pack.Resolver, pack.Selector, pack.MaxHealthRaw, friendlyFire, locked, allyCheckMissRaw, holdMilli);
        internal static CombatPack NoSpread(CombatPack pack)
        {
            var weapons = new WeaponDef[pack.Weapons.Length];
            for (int i = 0; i < weapons.Length; i++) weapons[i] = NoSpread(pack.Weapons[i]);
            return new CombatPack(weapons, pack.Resolver, pack.Selector, pack.MaxHealthRaw, pack.FriendlyFire, pack.FriendlyFireLocked, pack.AllyCheckMissRaw, pack.AllyHoldMilli);
        }
    }

    internal sealed class RecordingResolver : IHitResolver
    {
        internal readonly List<(long falloff, int roll, long damage)> Samples = new List<(long, int, long)>();
        private readonly RwrHitResolver inner = new RwrHitResolver();
        public ulong PolicyKey => 0x5445535400000001;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw)
        {
            Samples.Add((falloffRaw, rollRaw, inner.DamageRaw(in weapon, falloffRaw, rollRaw, maxHealthRaw)));
            return 0;
        }
    }
    internal sealed class ZeroResolver : IHitResolver
    {
        public ulong PolicyKey => 0x5445535400000002;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => 0;
    }
    internal sealed class LethalResolver : IHitResolver
    {
        public ulong PolicyKey => 0x5445535400000003;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw;
    }
}
