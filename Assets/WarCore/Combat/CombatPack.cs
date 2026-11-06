using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    public sealed class CombatPack
    {
        private readonly WeaponDef[] weapons;
        private readonly MeleeSpec[] melee;
        private readonly BlockSpec[] block;
        public ReadOnlySpan<WeaponDef> Weapons => weapons;
        // Melee swings (WP-20). Empty for every pack that has none; such a pack hashes, sizes and registers exactly as before.
        public ReadOnlySpan<MeleeSpec> Melee => melee;
        public bool HasMelee => melee.Length > 0;
        // Block data (C3). Empty for every pack that has none; such a pack hashes, sizes and registers exactly as before.
        public ReadOnlySpan<BlockSpec> Block => block;
        public bool HasBlock => block.Length > 0;
        // Most damage commands one unit can issue in a tick: one shot, or one swing landing on up to this many targets.
        public int MaxHitsPerAction { get; }
        public IHitResolver Resolver { get; }
        public ITargetSelector Selector { get; }
        public long MaxHealthRaw { get; }
        public ulong PolicyDigest { get; }
        // Match-level combat rules. FriendlyFire: bullets that reach a teammate first hurt them (true) or are stopped (false).
        // AllyCheckMissRaw (0..65536, Scale 65536): chance an AI shooter fires anyway when a teammate stands on its line.
        // AllyHoldMilli: thousandths of a tick an AI waits after declining to fire (30000 = one second).
        public bool FriendlyFire { get; }
        public bool FriendlyFireLocked { get; }
        public int AllyCheckMissRaw { get; }
        public long AllyHoldMilli { get; }

        public CombatPack(WeaponDef[] weapons, IHitResolver resolver, ITargetSelector selector, long maxHealthRaw,
            bool friendlyFire = false, bool friendlyFireLocked = false, int allyCheckMissRaw = 0, long allyHoldMilli = 30000, MeleeSpec[] melee = null, BlockSpec[] block = null)
        {
            if (allyCheckMissRaw < 0 || allyCheckMissRaw > SimFix.Scale) throw new ArgumentOutOfRangeException(nameof(allyCheckMissRaw));
            if (allyHoldMilli < 1000 || allyHoldMilli > 1_000_000_000_000L) throw new ArgumentOutOfRangeException(nameof(allyHoldMilli));
            this.weapons = (WeaponDef[])weapons.Clone();
            this.melee = melee == null ? Array.Empty<MeleeSpec>() : (MeleeSpec[])melee.Clone();
            this.block = block == null ? Array.Empty<BlockSpec>() : (BlockSpec[])block.Clone();
            int most = 1;
            foreach (var spec in this.melee) most = Math.Max(most, spec.MaxTargets);
            MaxHitsPerAction = most;
            Resolver = resolver; Selector = selector; MaxHealthRaw = maxHealthRaw;
            FriendlyFire = friendlyFire; FriendlyFireLocked = friendlyFireLocked;
            AllyCheckMissRaw = allyCheckMissRaw; AllyHoldMilli = allyHoldMilli;
            var hash = new StateHash();
            hash.Add("combat.pack.v2"); hash.Add(weapons.Length);
            foreach (var weapon in this.weapons) weapon.WriteState(hash);
            hash.Add(maxHealthRaw); hash.Add(resolver.PolicyKey); hash.Add(selector.PolicyKey);
            hash.Add(friendlyFire); hash.Add(friendlyFireLocked); hash.Add(allyCheckMissRaw); hash.Add(allyHoldMilli);
            // Appended only when present, so a pack without melee keeps the digest it had before WP-20.
            if (this.melee.Length > 0)
            { hash.Add("combat.pack.melee.v1"); hash.Add(this.melee.Length); foreach (var spec in this.melee) spec.WriteState(hash); }
            if (this.block.Length > 0)
            { hash.Add("combat.pack.block.v1"); hash.Add(this.block.Length); foreach (var spec in this.block) spec.WriteState(hash); }
            PolicyDigest = hash.Value;
        }

        public GameplaySchema BuildSchema(int unitCount) => new GameplaySchema(
            new[] { new GameplayResourceDef(1, "health", 0, MaxHealthRaw, MaxHealthRaw, isLife: true) },
            new[] { new GameplayDamageTypeDef(1, "hit", 1, 1) },
            new[] { new GameplayPipelineDef(1, new GameplayDamageSpec(0, GameplayDamageStages.None)) },
            new[] { new GameplayArchetypeDef(1, new[] { 1UL }) },
            new GameplayCapacities(unitCount, unitCount, checked(unitCount * MaxHitsPerAction), checked(unitCount * (MaxHitsPerAction + 1))), "combat.critical",
            lifeRules: new[] { new GameplayLifeRuleDef(1, 1, false, 0) });

        // N units, each landing at most k = MaxHitsPerAction hits per tick (k = 1 without melee): N*k commands, N*(k+1) notices.
        // The two comment lines below describe the k = 1 case: N shots + at most N final-resource facts + N death notices per tick.
        // Block 2 separately stages 2*Commands + Resources + Owners + 1 = 4*N+1 notices.
        // Its legacy Enqueue reservation needs Notices >= Commands + Resources = 2*N.
        public SimConfig BuildConfig(int entityCount, int unitCount)
        {
            int events = Math.Max(1, checked(unitCount * (MaxHitsPerAction + 2)));
            return new SimConfig(entityCapacity: entityCount, eventInitialCapacity: events, eventHardLimit: events);
        }
    }
}
