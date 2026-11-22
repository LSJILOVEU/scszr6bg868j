using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;
using WarCore.Tests.Projectile;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Down1
{
    // DOWN-1 direct fixtures: a combat world built by hand (as Blast1World) with the casualty parts wired the way DownedFeature
    // wires them: the casualty component, BindCasualties, system 33, the action gate with the casualty row bound to combat.
    // Units fire only when a test submits an intent (no automatic fire); damage comes from a scripted resolver, raw.
    internal sealed class Down1World
    {
        internal const long M = SimFix.Scale;
        internal const uint UnitLayer = 1u, WallLayer = 2u;

        internal readonly struct Unit
        {
            internal readonly long X, Z;
            internal readonly int Team, Weapon, Melee, Block;
            internal Unit(long x, long z, int team, int weapon, int melee, int block) { X = x; Z = z; Team = team; Weapon = weapon; Melee = melee; Block = block; }
        }

        // A unit at (x, z) raw.
        internal static Unit U(long x, long z, int team, int weapon = 0, int melee = -1, int block = -1) => new Unit(x, z, team, weapon, melee, block);

        // An instant weapon that may fire every tick, 60 m of range, a full magazine of 1000.
        internal static WeaponDef Gun(long intervalMilli = 1000) => Proj1World.Weapon(intervalMilli: intervalMilli, magazine: 1000);

        internal static DownedRules Rules(DownedWhen when = DownedWhen.Lethal, int ticks = 1800, int finishAfter = 0, bool finish = true,
            bool targetable = true, int crawl = 150) => new DownedRules(when, ticks, crawl, 0, finish, finishAfter, 0, targetable);

        internal static CombatPack Pack(DownedRules downed, ScriptedDamage resolver, long maxHealthRaw = 100, BlastRules blasts = null,
            MeleeSpec[] melee = null, BlockSpec[] block = null, WeaponDef[] weapons = null, int[] projectileSpeeds = null) =>
            new CombatPack(weapons ?? new[] { Gun() }, resolver, new NearestVisibleEnemy(), maxHealthRaw, melee: melee, block: block,
                body: Ml3World.Rwr, projectileSpeeds: projectileSpeeds, blasts: blasts, downed: downed);

        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly BlastSystem Blasts;
        internal readonly ActionGate Gate;
        internal readonly int Count;
        private readonly EntityId[] units;
        private readonly StableId[] ids;
        private readonly ComponentType<Casualty> casualties;
        private readonly bool hasCasualties;
        private readonly List<Action<SimContext>>[] scripts = new List<Action<SimContext>>[64];

        // wire: bind the casualties when the pack keeps them (false leaves a casualty pack unbound).
        internal Down1World(CombatPack pack, Unit[] list, Ml3World.Wall[] walls = null, IDamageGuard guard = null, bool wire = true, ulong seed = 41, bool auto = false)
        {
            walls = walls ?? Array.Empty<Ml3World.Wall>();
            uint wallMask = walls.Length == 0 ? 0u : WallLayer;
            Count = list.Length;
            var weapons = new int[Count];
            for (int i = 0; i < Count; i++) weapons[i] = list[i].Weapon;
            int projectiles = pack.HasProjectiles ? pack.ProjectileCapacity(weapons) : 0;
            int blasts = pack.BlastCommands(Count, projectiles);
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            for (int i = 0; i < walls.Length; i++) entries.Add(new PlacedCatalogEntry(2, (ulong)i + 1));
            for (int i = 0; i < list.Length; i++) entries.Add(new PlacedCatalogEntry(1, (ulong)i + 1));
            Sim = Simulation.CreateRetainedWorld(seed, new PlacedCatalog(content, entries.ToArray()), pack.BuildConfig(entries.Count, Count, projectiles, blasts),
                content: content, gameplaySchema: pack.BuildSchema(Count, projectiles, blasts));
            var positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            var combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            if (pack.UsesCasualties) { casualties = Casualty.Register(Sim.Entities); hasCasualties = true; }
            Sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(2), footAnchoredLayers: UnitLayer);
            Combat = new CombatSystem(Sim, positions, combatants, pack, wallMask, UnitLayer, Count, null);
            if (!auto) Combat.UseAutoFire(new NoAutoFire());
            if (guard != null) Combat.SetDamageGuard(guard);
            Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
            if (pack.HasProjectiles) Sim.RegisterGameplay("combat.projectiles", new ProjectileSystem(Sim, Combat, Math.Max(1, projectiles)), SimSystemPhase.Queries);
            if (pack.HasBlasts)
            {
                Blasts = new BlastSystem(Sim, Combat, Count);
                Sim.RegisterGameplay("combat.blasts", Blasts, SimSystemPhase.Queries);
            }
            if (wire && pack.UsesCasualties)
            {
                Combat.BindCasualties(casualties);
                Sim.RegisterGameplay("combat.casualty", new CasualtySystem(Sim, Combat), SimSystemPhase.Queries);
                Gate = new ActionGate();
                Gate.Add(new CasualtyLimit(Combat));
                Combat.UseActionGate(Gate);
            }
            for (int i = 0; i < walls.Length; i++)
            {
                Ml3World.Wall w = walls[i];
                EntityId id = Sim.Entities.Create(StableEntityKey.Placed(2, (ulong)i + 1));
                long height = w.Top - w.Bottom;
                Sim.Entities.Set(positions, id, new Position(Raw((w.X0 + w.X1) / 2, w.Bottom + height / 2, (w.Z0 + w.Z1) / 2)));
                Sim.Entities.Set(colliders, id, Collider.Box(SimFix.FromRaw((w.X1 - w.X0) / 2), SimFix.FromRaw(height), SimFix.FromRaw((w.Z1 - w.Z0) / 2),
                    BoxYaw.Identity, layer: WallLayer, isStatic: true));
            }
            units = new EntityId[Count];
            ids = new StableId[Count];
            for (int i = 0; i < Count; i++)
            {
                Unit u = list[i];
                EntityId id = units[i] = Sim.Entities.Create(StableEntityKey.Placed(1, (ulong)i + 1));
                Sim.Entities.Set(positions, id, new Position(Raw(u.X, 0, u.Z)));
                Sim.Entities.Set(colliders, id, new Collider(SimFix.Ratio(1, 4), pack.Body.Value.Height));
                Sim.BindGameplay(id, 1);
                int ammo = u.Weapon < 0 ? 0 : pack.Weapons[u.Weapon].Magazine;
                Sim.Entities.Set(combatants, id, new Combatant(u.Team, u.Weapon, ammo, 0));
                ids[i] = Sim.Entities.StableOf(id);
            }
            for (int i = 0; i < Count; i++)
            {
                if (list[i].Melee >= 0) Combat.SetMelee(units[i], list[i].Melee);
                if (list[i].Block >= 0) Combat.SetBlock(units[i], list[i].Block);
            }
            Sim.CompleteSpatialBoundary();
        }

        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        internal EntityId Entity(int i) => units[i];
        internal StableId Id(int i) => ids[i];
        internal long Health(int i) => Sim.GameplayResource(Id(i), 1);
        internal bool Dead(int i) => Sim.GameplayLife(Id(i)).State == GameplayLifeState.Dead;
        internal bool Downed(int i) => Combat.IsDowned(Entity(i));
        internal bool Stunned(int i) => Combat.IsStunned(Entity(i));
        internal long Tick => Sim.Clock.Tick;
        internal void Step() => Sim.Step(default);
        internal void StepTo(long tick) { while (Tick < tick) Step(); }
        internal bool TryCasualty(int i, out Casualty value) { value = default; return hasCasualties && Sim.Entities.TryGet(casualties, Entity(i), out value); }

        // Runs action during the step of every tick, in a phase at an order (one system per order; register before the first step).
        internal void Every(SimSystemPhase phase, int order, Action<SimContext> action)
        {
            if (scripts[order] == null)
            {
                scripts[order] = new List<Action<SimContext>>();
                Sim.RegisterGameplay("down1.at-" + order, new Script(scripts[order], order), phase);
            }
            scripts[order].Add(action);
        }

        internal void At(long tick, SimSystemPhase phase, int order, Action<SimContext> action) =>
            Every(phase, order, c => { if (c.Clock.Tick == tick) action(c); });

        // Unit shooter fires at unit target on tick (an intent submitted in the Queries phase at order 27).
        internal void Shoot(long tick, int shooter, int target) =>
            At(tick, SimSystemPhase.Queries, 27, c => Combat.Submit(c, FireIntent.AtTarget(Id(shooter), Id(target))));

        // Downs unit i on tick (Queries, order 26, before any shot).
        internal void DownAt(long tick, int i) => At(tick, SimSystemPhase.Queries, 26, c => Combat.Down(c, Entity(i)));

        // This tick's fire records whose victim is unit i, as damage raw.
        internal List<long> FiresOn(int i)
        {
            var list = new List<long>();
            foreach (FireRecord f in Combat.Fires) if (f.Target.Equals(Id(i))) list.Add(f.DamageRaw);
            return list;
        }

        // A unit's melee state (only for a pack with melee).
        internal MeleeState MeleeOf(int i) => Sim.Entities.Get(Sim.Entities.Resolve<MeleeState>(), Entity(i));

        // This tick's strike records of a kind by unit i, as their victims' unit indices (-1: none).
        internal List<int> StrikesBy(int i, StrikeKind kind)
        {
            var list = new List<int>();
            foreach (StrikeRecord r in Combat.Strikes)
                if (r.Striker.Equals(Id(i)) && r.Kind == kind) list.Add(Array.IndexOf(ids, r.Target));
            return list;
        }

        // A melee swing: windup ticks, radius 2 m, a half angle of 60 degrees, damage raw, maxTargets, engaging within 2 m.
        internal static MeleeSpec Swing(int windup, long damageRaw, int maxTargets = 1, int recovery = 2) =>
            new MeleeSpec(windup, 1, recovery, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromRaw(damageRaw), maxTargets, SimFix.FromInt(2));

        // This tick's fire records shot by unit i.
        internal int FiresBy(int i)
        {
            int n = 0;
            foreach (FireRecord f in Combat.Fires) if (f.Shooter.Equals(Id(i))) n++;
            return n;
        }

        // Life-state changes (a death) the settlement published on this tick for unit i.
        internal int LifeNotices(int i)
        {
            int n = 0;
            long tick = Sim.Clock.Tick;
            int count = Sim.Events.Read(tick).Length;
            for (int e = 0; e < count; e++)
            {
                StableSimEvent fact = Sim.ReadStableEvent(tick, e);
                if (fact.Type.Equals(GameplayEvents.Life) && fact.Target.HasValue && fact.Target.Id.Equals(ids[i])) n++;
            }
            return n;
        }

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly List<Action<SimContext>> actions;
            internal Script(List<Action<SimContext>> actions, int order) { this.actions = actions; Order = order; }
            public int Order { get; }
            public string StateTypeId => "down1.at-" + Order;
            public int StateVersion => 1;
            public void Step(SimContext c) { foreach (Action<SimContext> a in actions) a(c); }
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { h.Add(StateTypeId); }
        }
    }

    // Damage in raw units, scripted: each hit takes the next amount of the queue, then Fallback.
    internal sealed class ScriptedDamage : IHitResolver
    {
        private readonly Queue<long> next = new Queue<long>();
        internal long Fallback = 10;
        public ulong PolicyKey => 0x444F574E31000001UL;
        internal ScriptedDamage Then(params long[] amounts) { foreach (long a in amounts) next.Enqueue(a); return this; }
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => next.Count > 0 ? next.Dequeue() : Fallback;
    }

    // Nobody fires unless ordered.
    internal sealed class NoAutoFire : IAutoFire
    {
        public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick) { }
        public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, in WeaponDef weapon, long tick, out SimVec3 direction)
        { direction = default; return AutoFireAction.Pass; }
    }

    // Shields the listed victims for every tick.
    internal sealed class Down1Shield : IDamageGuard
    {
        private readonly HashSet<StableId> shielded = new HashSet<StableId>();
        internal void Add(StableId id) => shielded.Add(id);
        public bool Shields(StableId victim, long tick) => shielded.Contains(victim);
    }
}
