using System;

namespace WarCore.Content
{
    // One unit type's dash, as written in its optional fields dashDistance (mm), dashTicks and dashCooldown (ticks).
    public readonly struct UnitDash
    {
        // A dash is at most 100 m long and 300 ticks (10 s) long, one tick of it at most 10 m (the movers' sweep bound), and
        // its cooldown, counted from the tick of the request, at least its own length and at most 18 000 ticks (10 min).
        public const long MaxDistanceMm = 100000, MaxStepMm = 10000;
        public const int MaxTicks = 300, MaxCooldownTicks = 18000;
        public long DistanceMm { get; }
        public int Ticks { get; }
        public int CooldownTicks { get; }
        public UnitDash(long distanceMm, int ticks, int cooldownTicks) { DistanceMm = distanceMm; Ticks = ticks; CooldownTicks = cooldownTicks; }
    }

    // The dashes of a pack's unit types. Every pack publishes one, so reading it never throws; most unit types have none.
    public sealed class UnitDashes
    {
        private readonly string[] ids;
        private readonly UnitDash?[] dashes;
        internal UnitDashes(string[] ids, UnitDash?[] dashes) { this.ids = ids; this.dashes = dashes; }

        // The unit type's dash, or null when it has none (or the pack has no such unit type).
        public UnitDash? Of(string unitTypeId)
        {
            int index = Array.IndexOf(ids, unitTypeId);
            return index < 0 ? null : dashes[index];
        }
    }
}
