using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    // STANCE-1: where a unit's stance stands. Level is the stance now; Target the level it is changing to (-1 when it is not
    // changing); Due the tick whose step completes the change (0 when it is not changing).
    public readonly struct StanceView : IEquatable<StanceView>
    {
        public readonly int Level, Target;
        public readonly long Due;
        public StanceView(int level, int target, long due) { Level = level; Target = target; Due = due; }
        public bool IsChanging => Target >= 0;
        public bool Equals(StanceView other) => Level == other.Level && Target == other.Target && Due == other.Due;
        public override bool Equals(object obj) => obj is StanceView other && Equals(other);
        public override int GetHashCode() => (Level * 31 + Target) * 31 + Due.GetHashCode();
        public override string ToString() => "(" + Level + ", " + Target + ", " + Due + ")";
    }

    // STANCE-1: every unit's stance (level 0 = the pack's standing body, level n = CombatPack.Stances[n - 1]) on the M0 posture
    // parts: the posture component (absent = standing), its timed change (a stable task that the step of its due tick completes,
    // before commands, AI, movement and combat) and the posture geometry that lowers the unit's capsule in the spatial index.
    // The posture id of level n is n (0 is the unchanged baseline). Registered only for a pack with stances, so every other world
    // keeps its systems, components, state bytes and hashes. Until the due tick everything follows the old stance: body, eye,
    // muzzle; speed takes the slower and spread the wider of the two. It is also the action limit of stances: a unit in level n
    // may not start that stance's Deny, and a changing unit may not start another change, a dash or a traversal.
    public sealed class StanceSystem : IRetainedGameplaySystem, ISimRestoreValidator, IActionLimit, ISpreadFactor
    {
        public const string ComponentId = "combat.stance";
        public const string TransitionEventName = "stance.transition";
        // The event id EventTypeRegistry gives the name, locked with it so a change of the hash cannot move it silently.
        public const int TransitionEventId = 909854908;
        public const int SystemOrder = 2;
        // The task kind of a stance change in the stable task keys.
        public const ulong TaskKind = 26;
        // What a changing unit may not start, whatever its stance.
        public const UnitAction ChangingDeny = UnitAction.Stance | UnitAction.Dash | UnitAction.Traverse;

        private readonly Simulation sim;
        private readonly EntityStore entities;
        private readonly ComponentType<Posture> postures;
        private readonly ComponentType<Position> positions;
        private readonly PostureSystem inner;
        private readonly CombatBody body;
        private readonly StanceDef[] stances;
        // Ticks to reach level n from level 0: the running sum of the stances' Ticks (index 0 = 0).
        private readonly long[] reach;

        public ActionGate Gate { get; }
        // Levels of the pack: the standing body plus its stances.
        public int Levels => stances.Length + 1;
        public int Order => SystemOrder;
        public string StateTypeId => inner.StateTypeId;
        public int StateVersion => inner.StateVersion;

        // The posture component, catalog and geometry of a pack with stances, registered on the store. Call it with the other
        // components, before the spatial index is configured; the index takes the geometry (ConfigureSpatial postures), so a
        // unit's capsule follows its stance.
        public static PostureGeometry RegisterPostures(EntityStore entities, CombatPack pack)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            if (pack == null || !pack.HasStances) throw new ArgumentException("The combat pack has no stances.", nameof(pack));
            ComponentType<Posture> component = entities.Register<Posture>(ComponentId, new PostureSerializer());
            var defs = new PostureDef[pack.Stances.Length];
            for (int i = 0; i < defs.Length; i++)
                defs[i] = new PostureDef(i + 1, pack.Stances[i].Body.Height, SimFix.FromInt(1), default,
                    SimFix.Ratio(pack.Stances[i].SpeedPermille, 1000));
            return new PostureGeometry(entities, component, new PostureCatalog(defs));
        }

        // The combat system reads every unit's eye and muzzle from here once this is made; the gate gets this limit.
        public StanceSystem(Simulation sim, PostureGeometry geometry, ComponentType<Position> positions, ComponentType<Collider> colliders,
            CombatSystem combat, ActionGate gate)
        {
            this.sim = sim ?? throw new ArgumentNullException(nameof(sim));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            Gate = gate ?? throw new ArgumentNullException(nameof(gate));
            CombatPack pack = combat.Pack;
            if (!pack.HasStances) throw new ArgumentException("The combat pack has no stances.", nameof(combat));
            entities = sim.Entities;
            postures = entities.Resolve<Posture>();
            this.positions = positions;
            body = pack.Body.Value;
            stances = pack.Stances.ToArray();
            reach = new long[stances.Length + 1];
            for (int i = 0; i < stances.Length; i++) reach[i + 1] = reach[i] + stances[i].Ticks;
            var registry = new EventTypeRegistry();
            registry.Register("events-dropped");
            registry.Register("world-change");
            EventTypeId type = registry.Register(TransitionEventName);
            if (type.Value != TransitionEventId)
                throw new InvalidOperationException("Stance event registration conflicts with the event contract.");
            inner = new PostureSystem(entities, postures, positions, colliders, geometry, type, SystemOrder);
            gate.Add(this);
            combat.AttachStances(this);
        }

        // Ticks a change from one level to another takes: the stances' Ticks between them added up.
        public long Cost(int from, int to) => Math.Abs(reach[Check(to)] - reach[Check(from)]);

        private int Check(int level)
        {
            if (level < 0 || level >= Levels) throw new ArgumentOutOfRangeException(nameof(level), "A level is 0 to " + (Levels - 1) + ".");
            return level;
        }

        // Asks a unit to change to level, between two steps (a session command, a test): the change counts from the tick about
        // to run, so its due tick is Tick + 1 + Cost. True when the change started. False, with nothing written, when the level is
        // the unit's own, the unit is changing, dead or not on the field, or the gate denies Stance. A level out of range throws.
        public bool TryStance(EntityId unit, int level) => Begin(unit, level, checked(sim.Clock.Tick + 1));

        // The same, inside a step (a system of the Requests phase): the change counts from this tick, so a call during the step
        // of tick T and a call between T - 1 and T give the same due tick.
        public bool TryStance(SimContext context, EntityId unit, int level)
        {
            if (context == null || !ReferenceEquals(context, sim.Context)) throw new ArgumentException("The stance change needs its own simulation context.", nameof(context));
            context.EmitStableBatch(Span<StableProducedEvent>.Empty);
            return Begin(unit, level, context.Clock.Tick);
        }

        private bool Begin(EntityId unit, int level, long start)
        {
            Check(level);
            if (!Present(unit)) return false;
            Posture posture = entities.TryGet(postures, unit, out Posture value) ? value : default;
            if (posture.IsTransitioning || posture.CurrentId == level || !Gate.Allows(unit, UnitAction.Stance)) return false;
            long due = checked(start + Cost(posture.CurrentId, level));
            inner.BeginStableTransition(sim.Context, entities.Link(unit), level, due - sim.Clock.Tick, TaskKind);
            return true;
        }

        // Puts a unit in level at once, dropping any change it is making, without asking the gate (for a unit knocked down).
        // False when the unit is dead or not on the field. It writes the posture, so it must run outside the sealed part of a
        // step (between steps or in the Requests, Movement or Attachment phase).
        public bool ForceStance(EntityId unit, int level)
        {
            Check(level);
            if (!Present(unit)) return false;
            inner.CancelTransition(sim.Context, unit);
            entities.Set(postures, unit, new Posture(level));
            return true;
        }

        // A living unit on the field (it has a position).
        private bool Present(EntityId unit) =>
            entities.TryGet(positions, unit, out _) && sim.GameplayLife(entities.StableOf(unit)).State != GameplayLifeState.Dead;

        public StanceView StanceOf(EntityId unit)
        {
            if (!entities.TryGet(postures, unit, out Posture posture)) return new StanceView(0, -1, 0);
            return posture.IsTransitioning ? new StanceView(posture.CurrentId, posture.TargetId, posture.DueTick) : new StanceView(posture.CurrentId, -1, 0);
        }

        private int Level(EntityId unit) => entities.TryGet(postures, unit, out Posture posture) ? posture.CurrentId : 0;
        private CombatBody BodyOf(int level) => level == 0 ? body : stances[level - 1].Body;
        private int SpeedOf(int level) => level == 0 ? 1000 : stances[level - 1].SpeedPermille;
        private int SpreadOf(int level) => level == 0 ? 1000 : stances[level - 1].SpreadPermille;

        // Eye and muzzle heights (raw, above the foot) of the unit's stance now.
        public long EyeRaw(EntityId unit) => BodyOf(Level(unit)).Eye.Raw;
        public long MuzzleRaw(EntityId unit) => BodyOf(Level(unit)).Muzzle.Raw;

        // The stance's speed factor (permille): while changing, the slower of the two levels.
        public int SpeedPermille(EntityId unit)
        {
            StanceView view = StanceOf(unit);
            int now = SpeedOf(view.Level);
            return view.IsChanging ? Math.Min(now, SpeedOf(view.Target)) : now;
        }

        // The stance's spread factor (permille): while changing, the wider of the two levels. A gear of the combat system's spread
        // line (ShotAngle).
        public int SpreadPermille(EntityId unit)
        {
            StanceView view = StanceOf(unit);
            int now = SpreadOf(view.Level);
            return view.IsChanging ? Math.Max(now, SpreadOf(view.Target)) : now;
        }

        public UnitAction Denied(EntityId unit)
        {
            if (!entities.TryGet(postures, unit, out Posture posture)) return UnitAction.None;
            UnitAction denied = posture.CurrentId == 0 ? UnitAction.None : stances[posture.CurrentId - 1].Deny;
            return posture.IsTransitioning ? denied | ChangingDeny : denied;
        }

        public void Step(SimContext context) => inner.Step(context);
        public void SaveState(SimStateWriter writer) => inner.SaveState(writer);
        public void LoadState(SimStateReader reader) => inner.LoadState(reader);
        public void ValidateRestoredState(SimContext context) => inner.ValidateRestoredState(context);
        public void WriteState(StateHash hash) => inner.WriteState(hash);
    }
}
