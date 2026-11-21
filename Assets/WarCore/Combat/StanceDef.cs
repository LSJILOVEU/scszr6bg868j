using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // STANCE-1: one lower stance of a combat pack (level 1, 2, ... below the standing body, level 0). Every height is measured up
    // from the foot, as the body's. SpeedPermille scales the unit's speed (1..1000), SpreadPermille its weapon spread (1..4000),
    // Ticks is the time to change between this stance and the one above it (1..300). Deny: what a unit in this stance may not
    // start; Move and Stance can never be denied (a unit must not be pinned).
    public readonly struct StanceDef
    {
        public const int MinSpeedPermille = 1, MaxSpeedPermille = 1000, MinSpreadPermille = 1, MaxSpreadPermille = 4000;
        public const int MinTicks = 1, MaxTicks = 300;
        // The actions a content pack may name: every UnitAction bit but Move and Stance.
        public const UnitAction Deniable = UnitAction.Dash | UnitAction.Traverse | UnitAction.Fire | UnitAction.Melee | UnitAction.Block
            | UnitAction.Throw | UnitAction.Reload | UnitAction.Interact | UnitAction.Capture;
        // What a stance denies when the content pack does not say.
        public const UnitAction DefaultDeny = UnitAction.Dash | UnitAction.Traverse;

        public readonly string Id;
        public readonly CombatBody Body;
        public readonly int SpeedPermille, SpreadPermille, Ticks;
        public readonly UnitAction Deny;

        public StanceDef(string id, CombatBody body, int speedPermille, int spreadPermille, int ticks, UnitAction deny = DefaultDeny)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A stance needs an id.", nameof(id));
            if (speedPermille < MinSpeedPermille || speedPermille > MaxSpeedPermille) throw new ArgumentOutOfRangeException(nameof(speedPermille));
            if (spreadPermille < MinSpreadPermille || spreadPermille > MaxSpreadPermille) throw new ArgumentOutOfRangeException(nameof(spreadPermille));
            if (ticks < MinTicks || ticks > MaxTicks) throw new ArgumentOutOfRangeException(nameof(ticks));
            if ((deny & ~Deniable) != UnitAction.None) throw new ArgumentOutOfRangeException(nameof(deny), "A stance may not deny Move or Stance.");
            Id = id; Body = body; SpeedPermille = speedPermille; SpreadPermille = spreadPermille; Ticks = ticks; Deny = deny;
        }

        internal void WriteState(StateHash hash)
        {
            hash.Add(Id); Body.WriteState(hash);
            hash.Add(SpeedPermille); hash.Add(SpreadPermille); hash.Add(Ticks); hash.Add((int)Deny);
        }
    }
}
