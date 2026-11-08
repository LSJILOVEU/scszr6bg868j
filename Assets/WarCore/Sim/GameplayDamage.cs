using System;

namespace WarCore.Sim
{
    [Flags]
    public enum GameplayDamageStages
    {
        None = 0, PreFlat = 1, Rate = 2, Product = 4, Resistance = 8, Critical = 16, PostFlat = 32, Limits = 64, All = 127
    }
    public enum GameplayModifierStage
    {
        PreFlat = 1, Rate = 2, Product = 3, PostFlat = 6
    }

    public readonly struct GameplayModifierKey : IComparable<GameplayModifierKey>
    {
        public StableId Source
        {
            get;
        }
        public ulong CastToken
        {
            get;
        }
        public uint ActionOrdinal
        {
            get;
        }
        public StableId StatusTarget
        {
            get;
        }
        public ulong StatusDefKey
        {
            get;
        }
        public bool HasGroupSource
        {
            get;
        }
        public StableId GroupSource
        {
            get;
        }
        public ushort GroupSourceGeneration
        {
            get;
        }
        public ulong StatusToken
        {
            get;
        }
        public ulong ModifierKey
        {
            get;
        }
        public GameplayModifierKey(StableId source, ulong castToken, uint actionOrdinal, ulong modifierKey,
            StableId statusTarget = default, ulong statusDefKey = 0, bool hasGroupSource = false,
            StableId groupSource = default, ushort groupSourceGeneration = 0, ulong statusToken = 0)
        {
            Source = source;
            CastToken = castToken;
            ActionOrdinal = actionOrdinal;
            ModifierKey = modifierKey;
            StatusTarget = statusTarget;
            StatusDefKey = statusDefKey;
            HasGroupSource = hasGroupSource;
            GroupSource = groupSource;
            GroupSourceGeneration = groupSourceGeneration;
            StatusToken = statusToken;
        }
        public int CompareTo(GameplayModifierKey other)
        {
            int c = Source.CompareTo(other.Source);
            if (c != 0)
                return c;
            c = CastToken.CompareTo(other.CastToken);
            if (c != 0)
                return c;
            c = ActionOrdinal.CompareTo(other.ActionOrdinal);
            if (c != 0)
                return c;
            c = StatusTarget.CompareTo(other.StatusTarget);
            if (c != 0)
                return c;
            c = StatusDefKey.CompareTo(other.StatusDefKey);
            if (c != 0)
                return c;
            c = HasGroupSource.CompareTo(other.HasGroupSource);
            if (c != 0)
                return c;
            c = GroupSource.CompareTo(other.GroupSource);
            if (c != 0)
                return c;
            c = GroupSourceGeneration.CompareTo(other.GroupSourceGeneration);
            if (c != 0)
                return c;
            c = StatusToken.CompareTo(other.StatusToken);
            if (c != 0)
                return c;
            return ModifierKey.CompareTo(other.ModifierKey);
        }
    }

    public readonly struct GameplayModifier : IComparable<GameplayModifier>
    {
        public GameplayModifierStage Stage
        {
            get;
        }
        public long MagnitudeRaw
        {
            get;
        }
        public GameplayModifierKey Key
        {
            get;
        }
        public GameplayModifier(GameplayModifierStage stage, long magnitudeRaw, GameplayModifierKey key)
        {
            Stage = stage;
            MagnitudeRaw = magnitudeRaw;
            Key = key;
        }
        public int CompareTo(GameplayModifier other)
        {
            int order = ((int)Stage).CompareTo((int)other.Stage);
            return order != 0 ? order : Key.CompareTo(other.Key);
        }
    }

    public readonly struct GameplayDamageSpec
    {
        public long BaseRaw
        {
            get;
        }
        public GameplayDamageStages EnabledStages
        {
            get;
        }
        public long ResistanceRaw
        {
            get;
        }
        public long PenetrationRaw
        {
            get;
        }
        public long CriticalChanceRaw
        {
            get;
        }
        public long CriticalFactorRaw
        {
            get;
        }
        public long MinimumRaw
        {
            get;
        }
        public long MaximumRaw
        {
            get;
        }
        public GameplayDamageSpec(long baseRaw, GameplayDamageStages enabledStages = GameplayDamageStages.All,
            long resistanceRaw = 0, long penetrationRaw = 0, long criticalChanceRaw = 0,
            long criticalFactorRaw = GameplayMath.Scale, long minimumRaw = 0, long maximumRaw = GameplayMath.MaxMagnitude)
        {
            BaseRaw = baseRaw;
            EnabledStages = enabledStages;
            ResistanceRaw = resistanceRaw;
            PenetrationRaw = penetrationRaw;
            CriticalChanceRaw = criticalChanceRaw;
            CriticalFactorRaw = criticalFactorRaw;
            MinimumRaw = minimumRaw;
            MaximumRaw = maximumRaw;
        }
        public GameplayDamageSpec WithBaseRaw(long value) => new GameplayDamageSpec(value, EnabledStages, ResistanceRaw, PenetrationRaw, CriticalChanceRaw, CriticalFactorRaw, MinimumRaw, MaximumRaw);
    }

    public readonly struct GameplayDamageResult
    {
        public long AmountRaw
        {
            get;
        }
        public bool WasCritical
        {
            get;
        }
        internal GameplayDamageResult(long amountRaw, bool wasCritical)
        {
            AmountRaw = amountRaw;
            WasCritical = wasCritical;
        }
    }

    public static class GameplayDamage
    {
        public static GameplayDamageResult Evaluate(in GameplayDamageSpec spec, GameplayModifier[] modifiers,
            int count, long criticalRoll, ref long saturationCount)
        {
            Validate(spec, modifiers, count);
            if (criticalRoll < 0 || criticalRoll >= GameplayMath.Scale || saturationCount < 0)
                throw new SimBoundaryException("M0C_GAMEPLAY_DAMAGE: Invalid roll or diagnostic count.");
            int cursor = 0;
            long value = Flat(spec.BaseRaw, modifiers, count, ref cursor, GameplayModifierStage.PreFlat,
                Enabled(spec, GameplayDamageStages.PreFlat), ref saturationCount);
            // At most 65535 signed 31-bit rates: |sum| < 2^47, plus S still fits long.
            long factor = GameplayMath.Scale; // S1_MUTATION:rate_base
            while (cursor < count && modifiers[cursor].Stage == GameplayModifierStage.Rate)
                factor += modifiers[cursor++].MagnitudeRaw;
            if (Enabled(spec, GameplayDamageStages.Rate))
            {
                if (factor < 0)
                {
                    GameplayMath.CountSaturation(ref saturationCount);
                    factor = 0;
                }
                if (factor > GameplayMath.MaxFactor)
                {
                    GameplayMath.CountSaturation(ref saturationCount);
                    factor = GameplayMath.MaxFactor;
                }
                value = GameplayMath.MultiplyFactor(value, factor, ref saturationCount);
            }
            while (cursor < count && modifiers[cursor].Stage == GameplayModifierStage.Product)
            {
                if (Enabled(spec, GameplayDamageStages.Product))
                    value = GameplayMath.MultiplyFactor(value, modifiers[cursor].MagnitudeRaw, ref saturationCount);
                cursor++;
            }
            if (Enabled(spec, GameplayDamageStages.Resistance))
            {
                long reduction = spec.ResistanceRaw - spec.PenetrationRaw; // S1_MUTATION:penetration_sign
                if (reduction < 0)
                {
                    GameplayMath.CountSaturation(ref saturationCount);
                    reduction = 0;
                }
                // Valid ratios constrain the difference to [-S,S]; the upper clamp is unreachable.
                value = GameplayMath.MultiplyFactor(value, GameplayMath.Scale - reduction, ref saturationCount);
            }
            bool critical = Enabled(spec, GameplayDamageStages.Critical) && criticalRoll < spec.CriticalChanceRaw; // S1_MUTATION:critical_comparison
            if (critical)
                value = GameplayMath.MultiplyFactor(value, spec.CriticalFactorRaw, ref saturationCount);
            value = Flat(value, modifiers, count, ref cursor, GameplayModifierStage.PostFlat,
                Enabled(spec, GameplayDamageStages.PostFlat), ref saturationCount); // S1_MUTATION:stage_order
            if (Enabled(spec, GameplayDamageStages.Limits))
            {
                if (value < spec.MinimumRaw)
                {
                    GameplayMath.CountSaturation(ref saturationCount);
                    value = spec.MinimumRaw;
                }
                if (value > spec.MaximumRaw)
                {
                    GameplayMath.CountSaturation(ref saturationCount);
                    value = spec.MaximumRaw;
                }
            }
            return new GameplayDamageResult(value, critical);
        }
        private static bool Enabled(in GameplayDamageSpec spec, GameplayDamageStages stage) => (spec.EnabledStages & stage) != 0;
        private static long Flat(long value, GameplayModifier[] modifiers, int count, ref int cursor,
            GameplayModifierStage stage, bool enabled, ref long saturationCount)
        {
            // A complete phase is summed in signed magnitude form and clamped exactly once.
            var sum = new FixedInt128(new FixedUInt128((ulong)value), false);
            while (cursor < count && modifiers[cursor].Stage == stage)
            {
                long amount = modifiers[cursor++].MagnitudeRaw;
                if (enabled)
                    sum += new FixedInt128(new FixedUInt128(FixedInt128.Abs(amount)), amount < 0);
            }
            return enabled ? GameplayMath.ClampWide(sum, ref saturationCount) : value;
        }
        public static void Validate(in GameplayDamageSpec spec, GameplayModifier[] modifiers, int count)
        {
            GameplayMath.RequireMagnitude(spec.BaseRaw);
            GameplayMath.RequireRatio(spec.ResistanceRaw);
            GameplayMath.RequireRatio(spec.PenetrationRaw);
            GameplayMath.RequireRatio(spec.CriticalChanceRaw);
            GameplayMath.RequireFactor(spec.CriticalFactorRaw);
            GameplayMath.RequireMagnitude(spec.MinimumRaw);
            GameplayMath.RequireMagnitude(spec.MaximumRaw);
            if ((spec.EnabledStages & ~GameplayDamageStages.All) != 0 || spec.MinimumRaw > spec.MaximumRaw)
                throw new SimBoundaryException("M0C_GAMEPLAY_PIPELINE: Invalid stage flags or limits.");
            if (modifiers == null || count < 0 || count > GameplayMath.MaxModifiers || count > modifiers.Length) // S1_MUTATION:capacity_count
                throw new SimBoundaryException("M0C_GAMEPLAY_MODIFIERS: Invalid count.");
            for (int i = 0; i < count; i++)
            {
                GameplayModifier modifier = modifiers[i];
                switch (modifier.Stage)
                {
                    case GameplayModifierStage.PreFlat:
                    case GameplayModifierStage.PostFlat:
                        GameplayMath.RequireSignedMagnitude(modifier.MagnitudeRaw);
                        break;
                    case GameplayModifierStage.Rate:
                        GameplayMath.RequireRate(modifier.MagnitudeRaw);
                        break;
                    case GameplayModifierStage.Product:
                        GameplayMath.RequireFactor(modifier.MagnitudeRaw);
                        break;
                    default:
                        throw new SimBoundaryException("M0C_GAMEPLAY_MODIFIERS: Unknown stage.");
                }
                if (i > 0 && ((int)modifiers[i - 1].Stage > (int)modifier.Stage ||
                    (modifiers[i - 1].Stage == modifier.Stage && modifiers[i - 1].Key.CompareTo(modifier.Key) >= 0))) // S1_MUTATION:modifier_order
                    throw new SimBoundaryException("M0C_GAMEPLAY_MODIFIERS: Noncanonical or duplicate key.");
                if (!modifier.Key.HasGroupSource && (modifier.Key.GroupSource.Value != 0 || modifier.Key.GroupSourceGeneration != 0))
                    throw new SimBoundaryException("M0C_GAMEPLAY_MODIFIERS: Absent group source must be zero.");
            }
        }
    }
}
