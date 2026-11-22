using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // NERVE-1 combat.status: how a unit's state widens its shot. Each row is optional (0 = the row is absent), at least one is
    // present. Pinned and Broken are the spread of a unit at that pressure level (one drag of the spread line between them: a
    // unit is at one level at a time); Wounded is the spread of a unit whose health is below WoundedBelowPermille of its
    // maximum (a second drag). A level whose row is absent counts 1000: a Broken unit never falls back to the Pinned row.
    public sealed class CombatStatus
    {
        public const int MinSpreadPermille = StatLine.MinPermille, MaxSpreadPermille = StatLine.MaxPermille;
        public const int MinBelowPermille = 1, MaxBelowPermille = 1000;

        public int PinnedPermille { get; }
        public int BrokenPermille { get; }
        public int WoundedBelowPermille { get; }
        public int WoundedPermille { get; }
        public bool HasPinned => PinnedPermille != 0;
        public bool HasBroken => BrokenPermille != 0;
        public bool HasWounded => WoundedPermille != 0;
        // The pressure drag exists: a pinned or a broken row was written.
        public bool HasNerve => HasPinned || HasBroken;

        // pinned, broken: 0 or 1..4000. woundedBelow and wounded: both 0, or 1..1000 and 1..4000. At least one row.
        public CombatStatus(int pinnedPermille, int brokenPermille, int woundedBelowPermille, int woundedPermille)
        {
            CheckSpread(pinnedPermille, nameof(pinnedPermille));
            CheckSpread(brokenPermille, nameof(brokenPermille));
            CheckSpread(woundedPermille, nameof(woundedPermille));
            if (woundedPermille == 0 ? woundedBelowPermille != 0 : woundedBelowPermille < MinBelowPermille || woundedBelowPermille > MaxBelowPermille)
                throw new ArgumentOutOfRangeException(nameof(woundedBelowPermille));
            if (pinnedPermille == 0 && brokenPermille == 0 && woundedPermille == 0)
                throw new ArgumentException("A status needs at least one of pinned, broken, wounded.", nameof(pinnedPermille));
            PinnedPermille = pinnedPermille; BrokenPermille = brokenPermille;
            WoundedBelowPermille = woundedBelowPermille; WoundedPermille = woundedPermille;
        }

        private static void CheckSpread(int permille, string name)
        {
            if (permille != 0 && (permille < MinSpreadPermille || permille > MaxSpreadPermille)) throw new ArgumentOutOfRangeException(name);
        }

        internal void WriteState(StateHash hash)
        {
            hash.Add("combat.pack.status.v1");
            hash.Add(PinnedPermille); hash.Add(BrokenPermille); hash.Add(WoundedBelowPermille); hash.Add(WoundedPermille);
        }
    }
}
