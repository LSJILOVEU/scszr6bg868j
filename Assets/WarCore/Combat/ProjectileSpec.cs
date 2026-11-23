using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // PROJ-1: how a weapon's shot travels. Speed 0 = the shot lands on the tick it is fired (every weapon before PROJ-1).
    // Otherwise the shot is a projectile that moves StepRaw along its line each tick and lives at most Flights ticks, the
    // last one cut at the weapon's FalloffEnd.
    public readonly struct ProjectileSpec
    {
        // Millimetres per second: 1 m/s to 1000 m/s. One second is SimClock.MilliTicksPerSecond thousandths of a tick.
        public const int MinSpeed = 1000, MaxSpeed = 1_000_000, MaxFlights = 900;
        private const long MilliPerSecond = SimClock.MilliTicksPerSecond;

        public readonly int Speed;
        public readonly long StepRaw;
        public readonly int Flights;

        public bool IsInstant => Speed == 0;

        // speed 0 gives the instant spec; otherwise MinSpeed..MaxSpeed and a flight of at most MaxFlights ticks to range.
        public ProjectileSpec(int speed, SimFix range)
        {
            if (speed == 0) { Speed = 0; StepRaw = 0; Flights = 0; return; }
            if (speed < MinSpeed || speed > MaxSpeed) throw new ArgumentOutOfRangeException("projectileSpeed");
            if (range.Raw <= 0) throw new ArgumentOutOfRangeException(nameof(range));
            long step = StepOf(speed);
            long flights = FlightsOf(range, step);
            if (flights > MaxFlights) throw new ArgumentOutOfRangeException("projectileSpeed");
            Speed = speed; StepRaw = step; Flights = (int)flights;
        }

        // Raw units moved per tick: speed * Scale / MilliPerSecond, truncated (1000 mm/s gives 2184 raw, 1 000 000 mm/s 2 184 533 raw).
        public static long StepOf(int speed) => speed * (long)SimFix.Scale / MilliPerSecond;

        // Ticks to cover range at step raw per tick, rounded up.
        public static long FlightsOf(SimFix range, long step) => (range.Raw + step - 1) / step;

        // Most projectiles of this weapon one unit can have in the pool at once (PROJ-1 P5, review 1). A unit fires at most once a
        // tick. A shot's start s (Settle: the due time when it carries a remainder, else the tick) lies in ((t - 1) * 1000, t * 1000]
        // of the tick t it is fired on, and the next start is at least g = min(FireIntervalMilli, ReloadMilli) later (a hold only
        // delays). A projectile fired on tick T lands by tick T + Flights, and combat (Order 30) fires before projectiles (Order
        // 31) land in the same phase, so at a firing the pool holds the shots fired on ticks [T, T + Flights]: starts in a half-open
        // window of L = (Flights + 1) * 1000 thousandths. Points at least g apart in it number at most (L - 1) / g + 1; g >= 1000
        // (WeaponDef), so this never exceeds Flights + 1.
        public long PerUnitCapacity(in WeaponDef weapon)
        {
            if (IsInstant) return 0;
            long gap = Math.Min(weapon.FireIntervalMilli, weapon.ReloadMilli);
            return ((Flights + 1L) * 1000 - 1) / gap + 1;
        }
    }
}
