using System;

namespace WarCore.Sim
{
    public readonly struct GameplayResourceDef : IComparable<GameplayResourceDef>
    {
        public ulong Key
        {
            get;
        }
        public string Name
        {
            get;
        }
        public long LowerRaw
        {
            get;
        }
        public long UpperRaw
        {
            get;
        }
        public long InitialRaw
        {
            get;
        }
        public long RegenAmountRaw
        {
            get;
        }
        public int RegenPeriodTicks
        {
            get;
        }
        public bool AllowSpend
        {
            get;
        }
        public bool IsLife
        {
            get;
        }
        public long LifeThresholdRaw
        {
            get;
        }
        public GameplayResourceDef(ulong key, string name, long lowerRaw, long upperRaw, long initialRaw,
            long regenAmountRaw = 0, int regenPeriodTicks = 0, bool allowSpend = true, bool isLife = false, long lifeThresholdRaw = 0)
        {
            Key = key;
            Name = name;
            LowerRaw = lowerRaw;
            UpperRaw = upperRaw;
            InitialRaw = initialRaw;
            RegenAmountRaw = regenAmountRaw;
            RegenPeriodTicks = regenPeriodTicks;
            AllowSpend = allowSpend;
            IsLife = isLife;
            LifeThresholdRaw = lifeThresholdRaw;
        }
        public int CompareTo(GameplayResourceDef other) => Key.CompareTo(other.Key);
    }

    public readonly struct GameplayDamageTypeDef : IComparable<GameplayDamageTypeDef>
    {
        public ulong Key
        {
            get;
        }
        public string Name
        {
            get;
        }
        public ulong ResourceKey
        {
            get;
        }
        public ulong PipelineKey
        {
            get;
        }
        public GameplayDamageTypeDef(ulong key, string name, ulong resourceKey, ulong pipelineKey)
        {
            Key = key;
            Name = name;
            ResourceKey = resourceKey;
            PipelineKey = pipelineKey;
        }
        public int CompareTo(GameplayDamageTypeDef other) => Key.CompareTo(other.Key);
    }

    public sealed class GameplayPipelineDef : IComparable<GameplayPipelineDef>
    {
        private readonly GameplayModifier[] modifiers;
        public ulong Key
        {
            get;
        }
        public GameplayDamageSpec Spec
        {
            get;
        }
        public int ModifierCount => modifiers.Length;
        public GameplayModifier ModifierAt(int index) => modifiers[index];
        public GameplayPipelineDef(ulong key, GameplayDamageSpec spec, GameplayModifier[] modifiers = null)
        {
            if (modifiers != null && modifiers.Length > GameplayMath.MaxModifiers)
                throw new SimBoundaryException("M0C_MODIFIER_CAPACITY: Too many modifiers.");
            Key = key;
            Spec = spec;
            this.modifiers = modifiers == null ? new GameplayModifier[0] : (GameplayModifier[])modifiers.Clone();
            Array.Sort(this.modifiers, new ModifierOrder());
        }
        private sealed class ModifierOrder : System.Collections.Generic.IComparer<GameplayModifier>
        {
            public int Compare(GameplayModifier left, GameplayModifier right)
            {
                int order = ((int)left.Stage).CompareTo((int)right.Stage);
                return order == 0 ? left.Key.CompareTo(right.Key) : order;
            }
        }
        public int CompareTo(GameplayPipelineDef other) => Key.CompareTo(other.Key);
        internal void Validate()
        {
            GameplayDamageSpec spec = Spec;
            GameplayDamage.Validate(in spec, modifiers, modifiers.Length);
        }
        internal GameplayDamageResult Evaluate(long amountRaw, long roll, ref long saturationCount)
        {
            GameplayDamageSpec captured = Spec.WithBaseRaw(amountRaw);
            return GameplayDamage.Evaluate(in captured, modifiers, modifiers.Length, roll, ref saturationCount);
        }
        internal GameplayDamageResult Evaluate(long amountRaw, long roll, long resistanceRaw, ref long saturationCount)
        {
            GameplayDamageSpec captured = new GameplayDamageSpec(amountRaw, Spec.EnabledStages, resistanceRaw,
                Spec.PenetrationRaw, Spec.CriticalChanceRaw, Spec.CriticalFactorRaw, Spec.MinimumRaw, Spec.MaximumRaw);
            return GameplayDamage.Evaluate(in captured, modifiers, modifiers.Length, roll, ref saturationCount);
        }
    }

    public readonly struct GameplayDefenseDef : IComparable<GameplayDefenseDef>
    {
        public ulong DamageTypeKey
        {
            get;
        }
        public long ResistanceRaw
        {
            get;
        }
        public GameplayDefenseDef(ulong damageTypeKey, long resistanceRaw)
        {
            DamageTypeKey = damageTypeKey;
            ResistanceRaw = resistanceRaw;
        }
        public int CompareTo(GameplayDefenseDef other) => DamageTypeKey.CompareTo(other.DamageTypeKey);
    }

    public sealed class GameplayArchetypeDef : IComparable<GameplayArchetypeDef>
    {
        private readonly ulong[] resourceKeys;
        private readonly GameplayDefenseDef[] defenses;
        public ulong Key
        {
            get;
        }
        public ulong FactionKey
        {
            get;
        }
        public ulong Tags
        {
            get;
        }
        public int ResourceCount => resourceKeys.Length;
        public ulong ResourceKeyAt(int index) => resourceKeys[index];
        public int DefenseCount => defenses.Length;
        public GameplayDefenseDef DefenseAt(int index) => defenses[index];
        public GameplayArchetypeDef(ulong key, ulong[] resourceKeys, GameplayDefenseDef[] defenses = null, ulong factionKey = 0, ulong tags = 0)
        {
            if (resourceKeys == null || resourceKeys.Length > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_ARCHETYPE_RESOURCES: Missing or excessive resource keys.");
            if (defenses != null && defenses.Length > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_ARCHETYPE_DEFENSES: Excessive defense definitions.");
            Key = key;
            FactionKey = factionKey;
            Tags = tags;
            this.resourceKeys = (ulong[])resourceKeys.Clone();
            Array.Sort(this.resourceKeys);
            this.defenses = defenses == null ? new GameplayDefenseDef[0] : (GameplayDefenseDef[])defenses.Clone();
            Array.Sort(this.defenses);
        }
        public int FindDefense(ulong damageType)
        {
            int low = 0, high = defenses.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                ulong value = defenses[middle].DamageTypeKey;
                if (value == damageType)
                    return middle;
                if (value < damageType)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return -1;
        }
        public int CompareTo(GameplayArchetypeDef other) => Key.CompareTo(other.Key);
    }

    public readonly struct GameplayCapacities
    {
        public int Owners
        {
            get;
        }
        public int Resources
        {
            get;
        }
        public int Commands
        {
            get;
        }
        public int Notices
        {
            get;
        }
        public int Statuses
        {
            get;
        }
        public int Cooldowns
        {
            get;
        }
        public int Casts
        {
            get;
        }
        public int InputCommands
        {
            get;
        }
        public int TargetsPerCast
        {
            get;
        }
        public GameplayCapacities(int owners, int resources, int commands, int notices, int statuses = 0,
            int cooldowns = 0, int casts = 0, int inputCommands = 0, int targetsPerCast = 0)
        {
            Owners = owners;
            Resources = resources;
            Commands = commands;
            Notices = notices;
            Statuses = statuses;
            Cooldowns = cooldowns;
            Casts = casts;
            InputCommands = inputCommands;
            TargetsPerCast = targetsPerCast;
        }
    }
}
