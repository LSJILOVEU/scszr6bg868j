using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    // DOWN-1: casualties, units that are alive but cannot fight (downed or stunned). Bound once by the world for a pack that
    // keeps them (CombatPack.UsesCasualties); in every other world nothing here is made, CannotAct is IsDead, IsDowned and
    // IsStunned are false, Targetable and Allows are true and the damage entry is the one BLAST-1 had. Death always wins: a
    // dead unit is neither downed nor stunned, whatever component it still carries.
    public sealed partial class CombatSystem
    {
        // The key's action ordinal of a bleed-out (a give-up shortens the same bleed): shots 0, melee 1, projectiles 2, blasts 3.
        internal const uint BleedActionOrdinal = 4;
        // Every action a unit can be denied.
        internal const UnitAction AllActions = UnitAction.Move | UnitAction.Dash | UnitAction.Traverse | UnitAction.Fire | UnitAction.Melee
            | UnitAction.Block | UnitAction.Throw | UnitAction.Reload | UnitAction.Stance | UnitAction.Interact | UnitAction.Capture;

        private ComponentType<Casualty> casualties;
        private bool casualtyBound;
        // This step's ledger: per entity slot, the damage already submitted to the unit (raw) and the tick it was submitted on;
        // a stamp other than the current tick reads as 0. Not saved and not hashed; LoadState voids every stamp, so a world
        // that loads an earlier snapshot never reads a stale sum.
        private long[] ledgerRaw, ledgerTicks;

        // True once the world bound the casualty component (a pack that keeps casualties only).
        public bool HasCasualties => casualtyBound;

        // Binds the casualty component (registered with Casualty.Register) and sizes the ledger by the entity capacity.
        public void BindCasualties(ComponentType<Casualty> component)
        {
            if (!pack.UsesCasualties) throw new InvalidOperationException("The combat pack keeps no casualties.");
            if (casualtyBound) throw new InvalidOperationException("Casualties are already bound.");
            casualties = component;
            casualtyBound = true;
            ledgerRaw = new long[Entities.Capacity];
            ledgerTicks = new long[Entities.Capacity];
            VoidLedger();
        }

        private void VoidLedger()
        {
            if (ledgerTicks == null) return;
            for (int i = 0; i < ledgerTicks.Length; i++) ledgerTicks[i] = -1;
        }

        private bool TryCasualty(EntityId unit, out Casualty value)
        {
            value = default;
            return casualtyBound && Entities.TryGet(casualties, unit, out value);
        }

        // Alive and downed.
        public bool IsDowned(EntityId unit) => TryCasualty(unit, out Casualty state) && state.Kind == Casualty.Downed && !IsDead(unit);

        // Alive, stunned and the clock (between steps: the tick that just ran) is before the tick it wakes.
        public bool IsStunned(EntityId unit) =>
            TryCasualty(unit, out Casualty state) && state.Kind == Casualty.Stunned && sim.Clock.Tick < state.Due && !IsDead(unit);

        // Dead, downed or stunned.
        public bool CannotAct(EntityId unit) => IsDead(unit) || IsDowned(unit) || IsStunned(unit);

        // Not a casualty, or the pack lets the AI pick casualties as targets.
        public bool Targetable(EntityId unit) => pack.Downed == null || pack.Downed.Targetable || !(IsDowned(unit) || IsStunned(unit));

        // The action gate bound to combat (UseActionGate) allows the action; true when none is bound.
        public bool Allows(EntityId unit, UnitAction action) => gate == null || gate.Allows(unit, action);

        // Downs a living unit, inside a step: it bleeds out pack.Downed.Ticks after this tick. Neither its health nor any command
        // changes. False for a unit already down, a dead one, or without combat.downed.
        public bool Down(SimContext c, EntityId unit)
        {
            RequireStepContext(c);
            return DownAt(unit, c.Clock.Tick);
        }

        // Stuns a living unit for ticks from this tick, inside a step. A unit still stunned keeps its start and wakes at the later
        // of the two ticks. False for a downed or dead unit, or in a world without casualties.
        public bool Stun(SimContext c, EntityId unit, int ticks)
        {
            if (ticks <= 0) throw new ArgumentOutOfRangeException(nameof(ticks), "A stun lasts at least one tick.");
            RequireStepContext(c);
            return StunAt(unit, ticks, c.Clock.Tick);
        }

        // A downed unit gives up, between two steps: it bleeds out in the step about to run (or earlier if it was due). False for
        // a unit that is not downed.
        public bool GiveUp(EntityId unit) => GiveUpAt(unit, checked(sim.Clock.Tick + 1));

        // The same inside a step, before this tick's combat step: it bleeds out in this step.
        public bool GiveUp(SimContext c, EntityId unit)
        {
            RequireStepContext(c);
            if (lastStepTick == c.Clock.Tick || inStep)
                throw new InvalidOperationException("A give-up must precede this tick's combat step.");
            return GiveUpAt(unit, c.Clock.Tick);
        }

        private void RequireStepContext(SimContext c)
        {
            if (c == null || !ReferenceEquals(c, sim.Context))
                throw new InvalidOperationException("A casualty change requires its owning simulation context.");
            c.EmitStableBatch(Span<StableProducedEvent>.Empty);
        }

        private bool DownAt(EntityId unit, long tick)
        {
            if (!casualtyBound || pack.Downed == null || !Entities.TryGet(combatants, unit, out _) || IsDead(unit) || IsDowned(unit)) return false;
            Entities.Set(casualties, unit, new Casualty(Casualty.Downed, tick, checked(tick + pack.Downed.Ticks)));
            return true;
        }

        private bool StunAt(EntityId unit, int ticks, long tick)
        {
            if (!casualtyBound || !Entities.TryGet(combatants, unit, out _) || IsDead(unit) || IsDowned(unit)) return false;
            long due = checked(tick + ticks);
            if (Entities.TryGet(casualties, unit, out Casualty now) && now.Kind == Casualty.Stunned && tick < now.Due)
            {
                if (due > now.Due) Entities.Set(casualties, unit, new Casualty(Casualty.Stunned, now.Start, due));
                return true;
            }
            Entities.Set(casualties, unit, new Casualty(Casualty.Stunned, tick, due));
            return true;
        }

        private bool GiveUpAt(EntityId unit, long now)
        {
            if (!IsDowned(unit)) return false;
            Casualty state = Entities.Get(casualties, unit);
            if (now < state.Due) Entities.Set(casualties, unit, new Casualty(Casualty.Downed, state.Start, now));
            return true;
        }

        // The unit's health less what this step already submitted to it, never below 0 (GEAR-1 reads it too).
        internal long EffectiveHealthRaw(EntityId unit)
        {
            long health = HealthRaw(Entities.StableOf(unit));
            if (!casualtyBound || (uint)unit.Id >= (uint)ledgerTicks.Length) return health;
            return Math.Max(0, health - Spent(unit.Id, sim.Clock.Tick));
        }

        private long Spent(int slot, long tick) => ledgerTicks[slot] == tick ? ledgerRaw[slot] : 0;

        private void Charge(StableId victim, long damageRaw, long tick)
        {
            if (!Entities.TryResolve(victim, out EntityRef found)) return;
            int slot = ((EntityId)found).Id;
            if ((uint)slot >= (uint)ledgerTicks.Length) throw new InvalidOperationException("Combat entity capacity exceeded.");
            ledgerRaw[slot] = Spent(slot, tick) + damageRaw;
            ledgerTicks[slot] = tick;
        }

        // The casualty rule of the damage entry (C3), after the guard. A downed victim: zero within FinishAfterTicks of going
        // down, then the damage as it is when Finish, else zero. Otherwise, under Lethal only: a hit at least as large as the
        // victim's effective health (at least 1) leaves 1 raw and downs it; any other hit is unchanged.
        private long CasualtyRule(StableId victim, long damageRaw, long tick)
        {
            DownedRules rules = pack.Downed;
            if (rules == null || !Entities.TryResolve(victim, out EntityRef found)) return damageRaw;
            var unit = (EntityId)found;
            if (IsDowned(unit))
            {
                Casualty state = Entities.Get(casualties, unit);
                if (tick <= state.Start + rules.FinishAfterTicks) return 0;
                return rules.Finish ? damageRaw : 0;
            }
            if (rules.When != DownedWhen.Lethal) return damageRaw;
            long effective = EffectiveHealthRaw(unit);
            if (effective < 1 || damageRaw < effective) return damageRaw;
            DownAt(unit, tick);
            return effective - 1;
        }

        // System 33 (CasualtySystem): a dead unit's component is removed; a stunned unit past its wake tick is removed; a downed
        // unit past its bleed tick takes its effective health as one damage command of its own, past the guard and the casualty
        // rule, kept out of the ledger and out of every record.
        internal void StepCasualties(SimContext c, EntityStage stage)
        {
            long tick = c.Clock.Tick;
            foreach (EntityId unit in stage.Capture())
            {
                if (!Entities.TryGet(casualties, unit, out Casualty state)) continue;
                if (IsDead(unit)) { Entities.Remove(casualties, unit); continue; }
                if (tick < state.Due) continue;
                if (state.Kind == Casualty.Stunned) { Entities.Remove(casualties, unit); continue; }
                long left = EffectiveHealthRaw(unit);
                if (left <= 0) continue;
                StableId id = Entities.StableOf(unit);
                c.Damage(sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, BleedActionOrdinal), 1, left);
            }
        }
    }

    // DOWN-1 system 33 (Queries, after combat 30 and the projectiles 31): bleed-outs and the end of stuns. Keeps no state.
    public sealed class CasualtySystem : IRetainedGameplaySystem
    {
        public const int SystemOrder = 33;
        private readonly CombatSystem combat;
        private readonly EntityStage stage;

        public CasualtySystem(Simulation sim, CombatSystem combat)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (!combat.HasCasualties) throw new ArgumentException("The combat system has no casualties bound.", nameof(combat));
            stage = new EntityStage(sim.Entities);
        }

        public int Order => SystemOrder;
        public string StateTypeId => "combat.casualty";
        public int StateVersion => 1;
        public void Step(SimContext context) => combat.StepCasualties(context, stage);
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }

    // DOWN-1 system 0 (Requests, first of all): a downed unit not lying in the pack's downed stance, or changing stance, is put in
    // it at once. A unit downed in a step's combat lies down at the start of the next step. Keeps no state.
    public sealed class CasualtyPoseSystem : IRetainedGameplaySystem
    {
        public const int SystemOrder = 0;
        private readonly CombatSystem combat;
        private readonly StanceSystem stances;
        private readonly EntityStage stage;
        private readonly int level;

        public CasualtyPoseSystem(Simulation sim, CombatSystem combat, StanceSystem stances)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.stances = stances ?? throw new ArgumentNullException(nameof(stances));
            if (!combat.HasCasualties || combat.Pack.Downed == null || combat.Pack.Downed.StanceLevel == 0)
                throw new ArgumentException("The combat pack forces no downed stance.", nameof(combat));
            level = combat.Pack.Downed.StanceLevel;
            stage = new EntityStage(sim.Entities);
        }

        public int Order => SystemOrder;
        public string StateTypeId => "combat.casualty-pose";
        public int StateVersion => 1;

        public void Step(SimContext context)
        {
            foreach (EntityId unit in stage.Capture())
            {
                if (!combat.IsDowned(unit)) continue;
                StanceView view = stances.StanceOf(unit);
                if (view.Level != level || view.IsChanging) stances.ForceStance(unit, level);
            }
        }

        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }

    // DOWN-1: the casualty row of the action gate. A downed unit may only move (crawl); a stunned one may do nothing.
    public sealed class CasualtyLimit : IActionLimit
    {
        private readonly CombatSystem combat;
        public CasualtyLimit(CombatSystem combat) { this.combat = combat ?? throw new ArgumentNullException(nameof(combat)); }

        public UnitAction Denied(EntityId unit)
        {
            if (combat.IsStunned(unit)) return CombatSystem.AllActions;
            if (combat.IsDowned(unit)) return CombatSystem.AllActions & ~UnitAction.Move;
            return UnitAction.None;
        }
    }
}
