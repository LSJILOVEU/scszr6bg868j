using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Combat
{
    // One source of a spread factor for a unit (AIM-1): a permille 1..4000 that StatLine multiplies into the shot's angle.
    // It reads only saved components, never a cache of its own, so a restored world answers the same.
    public interface ISpreadFactor
    {
        int SpreadPermille(EntityId unit);
    }

    public sealed partial class CombatSystem
    {
        private readonly List<ISpreadFactor> spreadGears = new List<ISpreadFactor>(), spreadDrags = new List<ISpreadFactor>();
        private readonly int[] gearScratch = new int[StatLine.MaxFactors], dragScratch = new int[StatLine.MaxFactors];
        private int dragFloorPermille, dragCeilPermille;
        private ActionGate gate;

        // AIM-1: the move mark for a pack with combat.moveSpread (the world registers it at Movement order 19), else null.
        public MoveMark MoveMark { get; private set; }

        private void InitAim()
        {
            if (pack.MoveSpread != null) MoveMark = new MoveMark(Entities, positions, combatants);
        }

        private int FactorCount => spreadGears.Count + spreadDrags.Count + (MoveMark != null ? 1 : 0);

        // A gear of the spread line (the stance row, for instance). Gears, drags and the move column are at most
        // StatLine.MaxFactors together; one more is refused when the world is assembled.
        public void AddSpreadGear(ISpreadFactor gear) => AddFactor(spreadGears, gear);

        // A drag of the spread line (load, stamina, pressure...): the drags' product is clamped by SetSpreadClamp.
        public void AddSpreadDrag(ISpreadFactor drag) => AddFactor(spreadDrags, drag);

        private void AddFactor(List<ISpreadFactor> list, ISpreadFactor factor)
        {
            if (factor == null) throw new ArgumentNullException(nameof(factor));
            if (FactorCount >= StatLine.MaxFactors)
                throw new InvalidOperationException("The spread line takes at most " + StatLine.MaxFactors + " factors.");
            list.Add(factor);
        }

        // The floor and ceiling of the drags' product, permille (0 = none), as StatLine.Apply takes them.
        public void SetSpreadClamp(int floorPermille, int ceilPermille)
        {
            StatLine.Apply(1, ReadOnlySpan<int>.Empty, ReadOnlySpan<int>.Empty, floorPermille, ceilPermille);
            dragFloorPermille = floorPermille; dragCeilPermille = ceilPermille;
        }

        // The table "may this unit do this now" (BASE-0). Null: everything is allowed.
        public void UseActionGate(ActionGate gate) => this.gate = gate;

        // The angle a shot is actually turned by: the weapon's angle through the spread line (gears, the move column, drags),
        // capped at SpreadSpec.MaxLegalMilliDegrees. With no source at all the weapon's angle comes back untouched. The
        // weapon's own spread state keeps the weapon's angle: only the turn of this shot uses this one.
        public int ShotAngle(EntityId shooter, in Combatant unit, int weaponAngle, long tick)
        {
            int gears = 0, drags = 0;
            foreach (ISpreadFactor gear in spreadGears) gearScratch[gears++] = gear.SpreadPermille(shooter);
            if (MoveMark != null) gearScratch[gears++] = MovePermille(shooter, unit.Weapon, tick);
            foreach (ISpreadFactor drag in spreadDrags) dragScratch[drags++] = drag.SpreadPermille(shooter);
            if (gears == 0 && drags == 0) return weaponAngle;
            long angle = StatLine.Apply(weaponAngle, new ReadOnlySpan<int>(gearScratch, 0, gears), new ReadOnlySpan<int>(dragScratch, 0, drags),
                dragFloorPermille, dragCeilPermille);
            return (int)Math.Min(SpreadSpec.MaxLegalMilliDegrees, angle);
        }

        // The move column: the band of how far the shooter moved this tick, from its weapon's table. A unit the mark did not
        // see this tick (it just entered) moved 0.
        private int MovePermille(EntityId shooter, int weapon, long tick)
        {
            long dx = 0, dz = 0;
            if (MoveMark.TryBefore(shooter, tick, out long x0, out long z0) && Entities.TryGet(positions, shooter, out Position now))
            { dx = now.Value.X.Raw - x0; dz = now.Value.Z.Raw - z0; }
            return pack.MoveSpread.For(weapon).PermilleFor(dx, dz);
        }

        // AIM-1 manual reload, asked between ticks: true when the reload starts. Refused, writing nothing, when the pack has no
        // combat.manualReload, the unit has no combat state or weapon, is dead, the action gate denies Reload, or the magazine
        // is full (a reload in progress counts as full: the ammo is refilled when it starts). Otherwise the ammo is refilled
        // and the next shot is due ReloadMilli after the same start an automatic reload uses, with "now" the tick about to run.
        public bool TryReload(EntityId unit)
        {
            if (!pack.ManualReload || !Entities.TryGet(combatants, unit, out Combatant state) || state.Weapon < 0 || IsDead(unit)) return false;
            if (gate != null && !gate.Allows(unit, UnitAction.Reload)) return false;
            WeaponDef weapon = pack.Weapons[state.Weapon];
            if (state.Ammo >= weapon.Magazine) return false;
            long tick = sim.Clock.Tick + 1;
            long start = state.DueMilli > (tick - 1) * 1000 ? state.DueMilli : tick * 1000;
            Entities.Set(combatants, unit, new Combatant(state.Team, state.Weapon, weapon.Magazine, start + weapon.ReloadMilli,
                state.SpreadStored, state.SpreadLastMilli));
            return true;
        }
    }
}
