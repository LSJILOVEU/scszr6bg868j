using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // Horizontal aim spread in thousandths of a degree. All zero = no spread (the weapon draws no random number).
    // The angle of the next shot is clamp(max(Base, stored - RecoverPerSec * elapsedMilli / 30000), <= Max); after a shot
    // stored = min(Max, angle + Growth). One second = 30000 thousandths of a tick.
    public readonly struct SpreadSpec : IStateHash
    {
        public const int MaxLegalMilliDegrees = 90000;
        public readonly int BaseMilli, GrowthMilli, RecoverPerSecMilli, MaxMilli;

        public SpreadSpec(int baseMilli, int growthMilli, int recoverPerSecMilli, int maxMilli)
        {
            if (baseMilli < 0 || baseMilli > MaxLegalMilliDegrees) throw new ArgumentOutOfRangeException(nameof(baseMilli));
            if (growthMilli < 0 || growthMilli > MaxLegalMilliDegrees) throw new ArgumentOutOfRangeException(nameof(growthMilli));
            if (recoverPerSecMilli < 0 || recoverPerSecMilli > MaxLegalMilliDegrees) throw new ArgumentOutOfRangeException(nameof(recoverPerSecMilli));
            if (maxMilli < baseMilli || maxMilli > MaxLegalMilliDegrees) throw new ArgumentOutOfRangeException(nameof(maxMilli));
            BaseMilli = baseMilli; GrowthMilli = growthMilli; RecoverPerSecMilli = recoverPerSecMilli; MaxMilli = maxMilli;
        }

        public bool IsNone => MaxMilli == 0;

        // Angle of a shot fired at timeMilli, given the state left by the previous shot.
        public int AngleAt(int stored, long lastMilli, long timeMilli)
        {
            if (IsNone) return 0;
            long recovered = RecoverPerSecMilli * Math.Max(0, timeMilli - lastMilli) / 30000;
            long angle = Math.Max(BaseMilli, stored - recovered);
            return (int)Math.Min(MaxMilli, angle);
        }

        public int StoredAfter(int angle) => (int)Math.Min(MaxMilli, (long)angle + GrowthMilli);

        public void WriteState(StateHash hash)
        { hash.Add(BaseMilli); hash.Add(GrowthMilli); hash.Add(RecoverPerSecMilli); hash.Add(MaxMilli); }
    }

    public readonly struct WeaponDef : IStateHash
    {
        public readonly long FireIntervalMilli, ReloadMilli, EffectRaw;
        public readonly int Magazine;
        public readonly SimFix FalloffStart, FalloffEnd;
        public readonly SpreadSpec Spread;

        // Time is in thousandths of a tick. EffectRaw uses Scale=65536; the resolver defines its meaning.
        public WeaponDef(long fireIntervalMilli, int magazine, long reloadMilli,
            SimFix falloffStart, SimFix falloffEnd, long effectRaw, SpreadSpec spread = default)
        {
            if (fireIntervalMilli < 1000) throw new ArgumentOutOfRangeException(nameof(fireIntervalMilli));
            if (magazine < 1) throw new ArgumentOutOfRangeException(nameof(magazine));
            if (reloadMilli < 1000) throw new ArgumentOutOfRangeException(nameof(reloadMilli));
            if (falloffStart.Raw < 0 || falloffStart >= falloffEnd) throw new ArgumentOutOfRangeException(nameof(falloffStart));
            if (effectRaw < 0) throw new ArgumentOutOfRangeException(nameof(effectRaw));
            FireIntervalMilli = fireIntervalMilli; Magazine = magazine; ReloadMilli = reloadMilli;
            FalloffStart = falloffStart; FalloffEnd = falloffEnd; EffectRaw = effectRaw; Spread = spread;
        }

        public long FalloffRaw(SimFix distance) => distance <= FalloffStart ? SimFix.Scale : distance >= FalloffEnd ? 0 :
            (FalloffEnd.Raw - distance.Raw) * SimFix.Scale / (FalloffEnd.Raw - FalloffStart.Raw);

        public void WriteState(StateHash hash)
        {
            hash.Add(FireIntervalMilli); hash.Add(Magazine); hash.Add(ReloadMilli);
            hash.Add(FalloffStart.Raw); hash.Add(FalloffEnd.Raw); hash.Add(EffectRaw);
            Spread.WriteState(hash);
        }
    }

    public interface IHitResolver
    {
        ulong PolicyKey { get; }
        long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw);
    }

    // Called only when the weapon is ready. M6 reaction delay needs an additional state contract.
    public interface ITargetSelector
    {
        ulong PolicyKey { get; }
        bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target);
    }
}
