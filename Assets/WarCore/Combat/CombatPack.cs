using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    public sealed class CombatPack
    {
        private readonly WeaponDef[] weapons;
        private readonly MeleeSpec[] melee;
        private readonly BlockSpec[] block;
        // PROJ-1: projectile speed per weapon in mm/s (index = weapon), or empty when every weapon fires instantly.
        private readonly int[] projectileSpeeds;
        private readonly ProjectileSpec[] projectiles;
        public ReadOnlySpan<WeaponDef> Weapons => weapons;
        // Melee swings (WP-20). Empty for every pack that has none; such a pack hashes, sizes and registers exactly as before.
        public ReadOnlySpan<MeleeSpec> Melee => melee;
        public bool HasMelee => melee.Length > 0;
        // Block data (C3). Empty for every pack that has none; such a pack hashes, sizes and registers exactly as before.
        public ReadOnlySpan<BlockSpec> Block => block;
        public bool HasBlock => block.Length > 0;
        // The standing body (ML-3). Null for every pack that has none: legacy geometry, and the pack hashes exactly as before.
        // It is the only source of the body; the spatial index's foot-anchored layers and the unit colliders' height follow it.
        public CombatBody? Body { get; }
        // PROJ-1: one spec per weapon when at least one weapon fires projectiles, else empty. Such a pack hashes, sizes and
        // registers exactly as before. A projectile weapon needs the body (its shots start at the muzzle).
        public ReadOnlySpan<ProjectileSpec> Projectiles => projectiles;
        public bool HasProjectiles => projectiles.Length > 0;
        public bool IsProjectile(int weapon) => projectiles.Length > 0 && weapon >= 0 && !projectiles[weapon].IsInstant;
        // BLAST-1: the pack's explosions, or null. A pack without them hashes, sizes and registers exactly as before.
        public BlastRules Blasts { get; }
        public bool HasBlasts => Blasts != null;
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
        // AIM-1 combat.moveSpread: how far a shooter moved this tick widens its shot (null = off; the pack hashes as before).
        public MoveSpread MoveSpread { get; }
        // AIM-1 combat.manualReload: CombatSystem.TryReload may start a reload before the magazine is empty (false = off).
        public bool ManualReload { get; }

        public CombatPack(WeaponDef[] weapons, IHitResolver resolver, ITargetSelector selector, long maxHealthRaw,
            bool friendlyFire = false, bool friendlyFireLocked = false, int allyCheckMissRaw = 0, long allyHoldMilli = 30000, MeleeSpec[] melee = null, BlockSpec[] block = null,
            CombatBody? body = null, int[] projectileSpeeds = null, MoveSpread moveSpread = null, bool manualReload = false, BlastRules blasts = null)
        {
            if (moveSpread != null && moveSpread.WeaponSlots != 0 && moveSpread.WeaponSlots != weapons.Length)
                throw new ArgumentException("A move-spread table per weapon needs one slot per weapon.", nameof(moveSpread));
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
            Body = body;
            MoveSpread = moveSpread; ManualReload = manualReload;
            Blasts = blasts;
            projectiles = BuildProjectiles(this.weapons, projectileSpeeds, body);
            this.projectileSpeeds = projectiles.Length == 0 ? Array.Empty<int>() : (int[])projectileSpeeds.Clone();
            var hash = new StateHash();
            hash.Add("combat.pack.v2"); hash.Add(weapons.Length);
            foreach (var weapon in this.weapons) weapon.WriteState(hash);
            hash.Add(maxHealthRaw); hash.Add(resolver?.PolicyKey ?? 0UL); hash.Add(selector.PolicyKey);
            hash.Add(friendlyFire); hash.Add(friendlyFireLocked); hash.Add(allyCheckMissRaw); hash.Add(allyHoldMilli);
            // Appended only when present, so a pack without melee keeps the digest it had before WP-20.
            if (this.melee.Length > 0)
            { hash.Add("combat.pack.melee.v1"); hash.Add(this.melee.Length); foreach (var spec in this.melee) spec.WriteState(hash); }
            if (this.block.Length > 0)
            { hash.Add("combat.pack.block.v1"); hash.Add(this.block.Length); foreach (var spec in this.block) spec.WriteState(hash); }
            if (body.HasValue)
            { hash.Add("combat.pack.body.v1"); body.Value.WriteState(hash); }
            if (projectiles.Length > 0)
            { hash.Add("combat.pack.projectile.v1"); hash.Add(projectiles.Length); foreach (var spec in projectiles) hash.Add(spec.StepRaw); }
            if (moveSpread != null)
            { hash.Add("combat.pack.movespread.v1"); moveSpread.WriteState(hash); }
            if (manualReload)
                hash.Add("combat.pack.reload.v1");
            // KB4: later optional sections (stances) go before this one, every one only when present.
            if (blasts != null) blasts.WriteState(hash);
            PolicyDigest = hash.Value;
        }

        // Null or all zero: no projectile section. Otherwise one spec per weapon, every weapon checked, and the body required.
        private static ProjectileSpec[] BuildProjectiles(WeaponDef[] weapons, int[] speeds, CombatBody? body)
        {
            if (speeds == null) return Array.Empty<ProjectileSpec>();
            if (speeds.Length != weapons.Length) throw new ArgumentException("One projectile speed per weapon is required.", "projectileSpeed");
            bool any = false;
            foreach (int speed in speeds) any |= speed != 0;
            if (!any) return Array.Empty<ProjectileSpec>();
            var specs = new ProjectileSpec[weapons.Length];
            for (int i = 0; i < specs.Length; i++) specs[i] = new ProjectileSpec(speeds[i], weapons[i].FalloffEnd);
            if (!body.HasValue)
                for (int i = 0; i < specs.Length; i++)
                    if (!specs[i].IsInstant) throw new ArgumentException("Weapon " + i + " fires projectiles but the combat pack has no body.", "projectileSpeed");
            return specs;
        }

        // PROJ-1 pool size P for a world's units (each unit's index into Weapons, -1 = none; reserves included): the sum of each
        // projectile weapon's per-unit bound (ProjectileSpec.PerUnitCapacity); a unit's weapon never changes after it spawns.
        // 0 for a pack without projectiles. The one source of P: the world's limits (BuildSchema, BuildConfig) and the pool
        // all take this number. Refused when the damage settlement's staging (2 * commands + resources + owners + 1, with
        // commands = N * MaxHitsPerAction + P) would exceed GameplayMath.MaxPackets.
        public int ProjectileCapacity(ReadOnlySpan<int> unitWeapons)
        {
            long total = 0;
            foreach (int w in unitWeapons)
                if (IsProjectile(w)) total += projectiles[w].PerUnitCapacity(in weapons[w]);
            long units = unitWeapons.Length;
            long staging = 2 * (units * MaxHitsPerAction + total) + 2 * units + 1;
            if (staging > GameplayMath.MaxPackets)
                throw new ArgumentException("Projectile capacity too large: P = " + total + ", units = " + units + ", damage staging "
                    + staging + " exceeds " + GameplayMath.MaxPackets + ".", nameof(unitWeapons));
            return (int)total;
        }

        // The same pack with another target selector: every other constructor argument is passed on as this pack holds it
        // (no melee, block, body or projectile section stays absent), so only the selector's PolicyKey changes the digest.
        // Rule (ML-3): a new optional section of the pack is passed on by every With* copy, with a test.
        public CombatPack WithSelector(ITargetSelector selector) => new CombatPack(weapons, Resolver, selector, MaxHealthRaw,
            FriendlyFire, FriendlyFireLocked, AllyCheckMissRaw, AllyHoldMilli, melee.Length == 0 ? null : melee, block.Length == 0 ? null : block, Body,
            projectileSpeeds.Length == 0 ? null : projectileSpeeds, MoveSpread, ManualReload, Blasts);

        // BLAST-1 blast commands B for a world of these units: PerTick blasts a tick, each landing at most one damage command on
        // every unit, so B = PerTick * N; 0 for a pack without blasts. Refused when the damage settlement's staging
        // (2 * commands + 2 * N + 1, commands = N * MaxHitsPerAction + P + B) would exceed GameplayMath.MaxPackets.
        public int BlastCommands(int unitCount, int projectiles = 0)
        {
            if (Blasts == null) return 0;
            if (unitCount < 0) throw new ArgumentOutOfRangeException(nameof(unitCount));
            if (projectiles < 0) throw new ArgumentOutOfRangeException(nameof(projectiles));
            long units = unitCount, per = Blasts.PerTick;
            long blasts = per * units;
            long staging = 2 * (units * MaxHitsPerAction + projectiles + blasts) + 2 * units + 1;
            if (staging > GameplayMath.MaxPackets)
                throw new ArgumentException("Blast capacity too large: B = " + per + ", N = " + units + ", damage staging " + staging
                    + " exceeds " + GameplayMath.MaxPackets + ".", nameof(unitCount));
            return (int)blasts;
        }

        // projectiles: the projectile pool size (ProjectileCapacity); each projectile may land one damage command in a tick.
        // blastCommands: BlastCommands for the world's units; each adds one damage command and one notice.
        public GameplaySchema BuildSchema(int unitCount, int projectiles = 0, int blastCommands = 0) => new GameplaySchema(
            new[] { new GameplayResourceDef(1, "health", 0, MaxHealthRaw, MaxHealthRaw, isLife: true) },
            new[] { new GameplayDamageTypeDef(1, "hit", 1, 1) },
            new[] { new GameplayPipelineDef(1, new GameplayDamageSpec(0, GameplayDamageStages.None)) },
            new[] { new GameplayArchetypeDef(1, new[] { 1UL }) },
            new GameplayCapacities(unitCount, unitCount, checked(unitCount * MaxHitsPerAction + projectiles + blastCommands),
                checked(unitCount * (MaxHitsPerAction + 1) + projectiles + blastCommands)), "combat.critical",
            lifeRules: new[] { new GameplayLifeRuleDef(1, 1, false, 0) });

        // N units, each landing at most k = MaxHitsPerAction hits per tick (k = 1 without melee): N*k commands, N*(k+1) notices.
        // The two comment lines below describe the k = 1 case: N shots + at most N final-resource facts + N death notices per tick.
        // Block 2 separately stages 2*Commands + Resources + Owners + 1 = 4*N+1 notices.
        // Its legacy Enqueue reservation needs Notices >= Commands + Resources = 2*N.
        // PROJ-1: P projectiles add P commands, so P notices and P events; final-resource facts and deaths stay bounded by N.
        // BLAST-1: B blast commands add the same way.
        public SimConfig BuildConfig(int entityCount, int unitCount, int projectiles = 0, int blastCommands = 0)
        {
            int events = Math.Max(1, checked(unitCount * (MaxHitsPerAction + 2) + projectiles + blastCommands));
            return new SimConfig(entityCapacity: entityCount, eventInitialCapacity: events, eventHardLimit: events);
        }
    }
}
