using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // NERVE-1: the pressure level of a unit, as the world's pressure system (or a script) says: 0 calm, 1 pinned, 2 broken.
    // It must read only saved state, never a cache of its own, so a restored world answers the same.
    public interface INerveSource
    {
        int NerveLevel(StableId unit);
    }

    public sealed partial class CombatSystem
    {
        public const int NerveCalm = 0, NervePinned = 1, NerveBroken = 2;
        private INerveSource nerveSource;

        // NERVE-1: the pack's spread clamp (stats.spread) and the status drags (combat.status). Two drags at most: one for the
        // pressure level (pinned or broken, whichever rows were written), one for wounds. Rows that were not written register
        // nothing, so a pack without combat.status keeps its spread line exactly.
        private void InitNerve()
        {
            if (pack.SpreadDragFloorPermille != 0 || pack.SpreadDragCeilPermille != 0)
                SetSpreadClamp(pack.SpreadDragFloorPermille, pack.SpreadDragCeilPermille);
            CombatStatus status = pack.Status;
            if (status == null) return;
            if (status.HasNerve) AddSpreadDrag(new NerveDrag(this));
            if (status.HasWounded) AddSpreadDrag(new WoundDrag(this));
        }

        // Binds the source of the pressure level, once. Refused when the pack has no pinned or broken row.
        public void BindNerve(INerveSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (pack.Status == null || !pack.Status.HasNerve)
                throw new InvalidOperationException("combat.status has no pinned or broken row: no nerve source to bind.");
            if (nerveSource != null) throw new InvalidOperationException("A nerve source is already bound.");
            nerveSource = source;
        }

        public bool HasNerveSource => nerveSource != null;

        // The maximum health a unit's wound line is measured against. HP-1 makes it per unit type; now the pack's one value.
        public long HealthMaxRaw(StableId unit) => pack.MaxHealthRaw;

        // The unit's pressure level (NerveCalm, NervePinned, NerveBroken) as the bound source says. Refused when the pack has a
        // pinned or broken row and no source is bound, or the source answers another number. NerveCalm without such rows.
        public int NerveLevelOf(EntityId unit)
        {
            if (pack.Status == null || !pack.Status.HasNerve) return NerveCalm;
            if (nerveSource == null)
                throw new InvalidOperationException("combat.status pinned or broken needs a nerve source: a world with the pressure system (pressure*) or a bound script.");
            int level = nerveSource.NerveLevel(Entities.StableOf(unit));
            if (level < NerveCalm || level > NerveBroken)
                throw new InvalidOperationException("A nerve source answered level " + level + "; allowed 0 (calm), 1 (pinned), 2 (broken).");
            return level;
        }

        // Whether the unit is below the wound line: health * 1000 < maximum * belowPermille (both at most 2^47 - 1 times 1000,
        // far inside a long). False without a wounded row.
        public bool IsWounded(EntityId unit)
        {
            CombatStatus status = pack.Status;
            if (status == null || !status.HasWounded) return false;
            StableId id = Entities.StableOf(unit);
            return HealthRaw(id) * 1000 < HealthMaxRaw(id) * status.WoundedBelowPermille;
        }

        // The two drags as ShotAngle multiplies them, for a receipt: the pressure drag (1000 calm or for a level whose row is
        // absent) and the wound drag (1000 when not wounded). Read-only, no random number.
        public int NerveSpreadPermille(EntityId unit)
        {
            switch (NerveLevelOf(unit))
            {
                case NervePinned: return pack.Status.HasPinned ? pack.Status.PinnedPermille : 1000;
                case NerveBroken: return pack.Status.HasBroken ? pack.Status.BrokenPermille : 1000;
                default: return 1000;
            }
        }

        public int WoundSpreadPermille(EntityId unit) => IsWounded(unit) ? pack.Status.WoundedPermille : 1000;

        private sealed class NerveDrag : ISpreadFactor
        {
            private readonly CombatSystem combat;
            internal NerveDrag(CombatSystem combat) { this.combat = combat; }
            public int SpreadPermille(EntityId unit) => combat.NerveSpreadPermille(unit);
        }

        private sealed class WoundDrag : ISpreadFactor
        {
            private readonly CombatSystem combat;
            internal WoundDrag(CombatSystem combat) { this.combat = combat; }
            public int SpreadPermille(EntityId unit) => combat.WoundSpreadPermille(unit);
        }
    }
}
