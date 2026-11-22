using System;
using System.Collections.Generic;
using System.Linq;
using WarCore.Combat;
using WarCore.Sim;
using WarCore.Tests.Projectile;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Blast1
{
    // BLAST-1 fixtures: a bodied combat world built directly (as Proj1World) with the blast system registered. Units carry no
    // weapon (they never fire) and 100 health; walls are boxes; ground and plates are optional. Early runs at Order 27 (before
    // the blast system), Late at Order 33 (after it).
    internal sealed class Blast1World
    {
        internal const long M = SimFix.Scale;
        internal const uint UnitLayer = 1u, WallLayer = 2u;
        // The RWR body: 1.75 m, so the body centre is 0.875 m over the foot.
        internal const long Centre = 57344;

        internal static BlastSpec Frag(bool friendly = true, bool walls = true, int inner = 2000, int outer = 6000, int damage = 100, string id = "t.frag") =>
            new BlastSpec(id, inner, outer, damage, friendly, walls);

        internal static BlastRules Rules(params BlastSpec[] kinds) => new BlastRules(kinds.Length == 0 ? new[] { Frag() } : kinds);

        internal static CombatPack Pack(BlastRules blasts, bool body = true, IHitResolver resolver = null) =>
            new CombatPack(new[] { Proj1World.Weapon() }, resolver ?? new FalloffDamage(), new NearestVisibleEnemy(), 100 * M,
                body: body ? Ml3World.Rwr : (CombatBody?)null, blasts: blasts);

        internal readonly struct Unit
        {
            internal readonly long X, Y, Z;
            internal readonly int Team;
            internal Unit(long x, long y, long z, int team) { X = x; Y = y; Z = z; Team = team; }
        }
        internal static Unit U(long x, long z, int team = 1, long y = 0) => new Unit(x, y, z, team);

        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly BlastSystem Blasts;
        internal readonly CombatPack UsedPack;
        internal readonly int Count;
        internal Action<SimContext> Early, Late;
        private readonly EntityId[] units;
        private readonly StableId[] ids;

        // reverse: create the units in reverse list order (their StableIds stay those of the list order).
        internal Blast1World(CombatPack pack, Unit[] list, Ml3World.Wall[] walls = null, HeightField ground = null, PlateField plates = null,
            ulong seed = 41, IDamageGuard guard = null, bool reverse = false)
        {
            walls = walls ?? Array.Empty<Ml3World.Wall>();
            uint wallMask = walls.Length == 0 ? 0u : WallLayer;
            UsedPack = pack; Count = list.Length;
            int blasts = pack.BlastCommands(Count);
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            for (int i = 0; i < walls.Length; i++) entries.Add(new PlacedCatalogEntry(2, (ulong)i + 1));
            for (int i = 0; i < list.Length; i++) entries.Add(new PlacedCatalogEntry(1, (ulong)i + 1));
            Sim = Simulation.CreateRetainedWorld(seed, new PlacedCatalog(content, entries.ToArray()), pack.BuildConfig(entries.Count, Count, 0, blasts),
                content: content, gameplaySchema: pack.BuildSchema(Count, 0, blasts));
            var positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            var combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            Positions = positions;
            Sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(2), footAnchoredLayers: pack.Body.HasValue ? UnitLayer : 0u);
            Combat = new CombatSystem(Sim, positions, combatants, pack, wallMask, UnitLayer, Count, null, ground, plates);
            if (guard != null) Combat.SetDamageGuard(guard);
            Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
            if (pack.HasBlasts)
            {
                Blasts = new BlastSystem(Sim, Combat, Count);
                Sim.RegisterGameplay("combat.blasts", Blasts, SimSystemPhase.Queries);
            }
            Sim.RegisterGameplay("blast1.early", new Script(this, 27, true), SimSystemPhase.Queries);
            Sim.RegisterGameplay("blast1.late", new Script(this, 33, false), SimSystemPhase.Queries);
            for (int i = 0; i < walls.Length; i++)
            {
                Ml3World.Wall w = walls[i];
                EntityId id = Sim.Entities.Create(StableEntityKey.Placed(2, (ulong)i + 1));
                long height = w.Top - w.Bottom;
                Sim.Entities.Set(positions, id, new Position(Raw((w.X0 + w.X1) / 2, w.Bottom + height / 2, (w.Z0 + w.Z1) / 2)));
                Sim.Entities.Set(colliders, id, Collider.Box(SimFix.FromRaw((w.X1 - w.X0) / 2), SimFix.FromRaw(height), SimFix.FromRaw((w.Z1 - w.Z0) / 2),
                    BoxYaw.Identity, layer: WallLayer, isStatic: true));
            }
            long unitHeight = pack.Body?.Height.Raw ?? M;
            units = new EntityId[Count];
            ids = new StableId[Count];
            for (int n = 0; n < Count; n++)
            {
                int i = reverse ? Count - 1 - n : n;
                Unit u = list[i];
                EntityId id = units[i] = Sim.Entities.Create(StableEntityKey.Placed(1, (ulong)i + 1));
                Sim.Entities.Set(positions, id, new Position(Raw(u.X, u.Y, u.Z)));
                Sim.Entities.Set(colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromRaw(unitHeight)));
                Sim.BindGameplay(id, 1);
                Sim.Entities.Set(combatants, id, new Combatant(u.Team, -1, 0, 0));
                ids[i] = Sim.Entities.StableOf(id);
            }
            Sim.CompleteSpatialBoundary();
        }

        internal readonly ComponentType<Position> Positions;
        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        internal EntityId Entity(int i) => units[i];
        internal StableId Id(int i) => ids[i];
        internal long Health(int i) => Sim.GameplayResource(Id(i), 1);
        internal bool Dead(int i) => Sim.GameplayLife(Id(i)).State == GameplayLifeState.Dead;
        internal long Tick => Sim.Clock.Tick;
        internal void Step() => Sim.Step(default);

        // Queues a blast of kind 0 at a raw point with no source.
        internal long Blast(long x, long y, long z, int kind = 0, StableId source = default, int team = 0) => Blasts.Enqueue(Raw(x, y, z), kind, source, team);

        // This tick's hits as (unit index, damage raw, distance raw), in the order they were issued (by StableId).
        internal int[] HitUnits() => Hits().Select(h => h.Unit).OrderBy(u => u).ToArray();

        internal (int Unit, long Damage, long Distance)[] Hits()
        {
            var list = new List<(int, long, long)>();
            for (int h = 0; h < Blasts.HitCount; h++)
            {
                BlastHit hit = Blasts.Hit(h);
                int index = Array.IndexOf(ids, hit.Victim);
                list.Add((index, hit.DamageRaw, hit.DistanceRaw));
            }
            return list.ToArray();
        }

        // The damage facts the settlement published on this tick for unit i (their amounts, raw): one per damage command.
        internal long[] DamageFacts(int i)
        {
            var amounts = new List<long>();
            long tick = Sim.Clock.Tick;
            int count = Sim.Events.Read(tick).Length;
            for (int e = 0; e < count; e++)
            {
                StableSimEvent fact = Sim.ReadStableEvent(tick, e);
                if (fact.Type.Equals(GameplayEvents.Damage) && fact.Target.HasValue && fact.Target.Id.Equals(ids[i])) amounts.Add(fact.Payload.Magnitude.Raw);
            }
            return amounts.ToArray();
        }

        // The damage DamageAt gives for a unit standing on the flat ground at horizontal distance h (raw) from a blast point
        // 0.875 m up: the 3D distance is exactly h, so the expected value is the card's integer formula on h.
        internal static long Expected(long h, long inner = 2 * M, long outer = 6 * M, long damage = 100 * M) =>
            h >= outer ? 0 : h <= inner ? damage : damage * (outer - h) / (outer - inner);

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly Blast1World world;
            private readonly bool early;
            internal Script(Blast1World world, int order, bool early) { this.world = world; Order = order; this.early = early; }
            public int Order { get; }
            public string StateTypeId => early ? "blast1.early" : "blast1.late";
            public int StateVersion => 1;
            public void Step(SimContext c) { if (early) world.Early?.Invoke(c); else world.Late?.Invoke(c); }
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { h.Add(StateTypeId); }
        }
    }

    // Shields the listed victims for every tick.
    internal sealed class ShieldSome : IDamageGuard
    {
        private readonly HashSet<StableId> shielded = new HashSet<StableId>();
        internal void Add(StableId id) => shielded.Add(id);
        public bool Shields(StableId victim, long tick) => shielded.Contains(victim);
    }
}
