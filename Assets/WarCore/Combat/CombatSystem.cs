using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    // What a shot ended on. Open = nothing on the line; FriendBlocked = a teammate stopped the bullet without damage.
    public enum FireOutcome : byte { Open = 0, Hit, WallBlocked, FriendBlocked, FriendHit }

    public readonly struct FireRecord
    {
        public readonly StableId Shooter, Target;
        public readonly SimVec3 Origin, End;
        public readonly SimFix Distance;
        public readonly long DamageRaw;
        public readonly int ShooterTeam;
        public readonly FireOutcome Outcome;
        public FireRecord(StableId shooter, StableId target, SimVec3 origin, SimVec3 end, SimFix distance, long damageRaw, int shooterTeam,
            FireOutcome outcome = FireOutcome.Hit)
        { Shooter = shooter; Target = target; Origin = origin; End = end; Distance = distance; DamageRaw = damageRaw; ShooterTeam = shooterTeam; Outcome = outcome; }
    }

    public sealed partial class CombatSystem : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly CombatPack pack;
        private readonly uint wallMask, unitLayerMask;
        private readonly SpatialHit[] candidates;
        private readonly FireRecord[] fires;
        private readonly RandomStreamId hitStream, spreadStream, allyStream;
        private int fireCount;
        private readonly IControlTarget controlled;
        // Entity slots include walls; only the number of pending shooters is capped by unitCount.
        private readonly FireIntent[] intents;
        private readonly long[] intentTicks;
        private readonly int[] intentSlots;
        private int intentCount;
        private long intentTick = -1, lastStepTick = -1;
        private readonly ComponentType<Collider> colliders;
        private readonly LiveUnit[] liveUnits;
        private int liveCount;
        private bool cacheValid, exclusionDisabled, inStep;
        public int QueriesThisStep { get; private set; }
        private struct LiveUnit
        {
            internal int Team;
            internal long X, Z;
        }

        public CombatSystem(Simulation sim, ComponentType<Position> positions, ComponentType<Combatant> combatants,
            CombatPack pack, uint wallMask, uint unitLayerMask, int unitCount, IControlTarget controlled = null)
        {
            this.sim = sim; this.positions = positions; this.combatants = combatants; this.pack = pack;
            this.wallMask = wallMask; this.unitLayerMask = unitLayerMask;
            stage = new EntityStage(sim.Entities);
            candidates = new SpatialHit[unitCount]; fires = new FireRecord[unitCount];
            hitStream = sim.Rng.Stream("combat.hit");
            spreadStream = sim.Rng.Stream("combat.spread");
            allyStream = sim.Rng.Stream("combat.ally-check");
            colliders = Entities.Resolve<Collider>();
            liveUnits = new LiveUnit[Entities.Capacity];
            this.controlled = controlled;
            intents = new FireIntent[Entities.Capacity];
            intentTicks = new long[Entities.Capacity];
            intentSlots = new int[candidates.Length];
            InitMelee();
            InitBlock();
        }

        public EntityStore Entities => sim.Entities;
        public int Order => 30;
        public string StateTypeId => "combat.system";
        public int StateVersion => 1;
        public ReadOnlySpan<FireRecord> Fires => fires.AsSpan(0, fireCount);
        public SimVec3 Point(EntityId entity) => Entities.Read(positions, entity).Value;
        public Combatant Unit(EntityId entity) => Entities.Read(combatants, entity);
        public bool IsDead(EntityId entity) => sim.GameplayLife(Entities.StableOf(entity)).State == GameplayLifeState.Dead;
        private static SimVec3 AimPoint(SimVec3 point) => point + new SimVec3(default, SimFix.Ratio(1, 2), default);
        public static SimVec3 Horizontal(SimVec3 delta) => new SimVec3(delta.X, default, delta.Z);

        public void Submit(SimContext c, in FireIntent intent)
        {
            if (c == null || !ReferenceEquals(c, sim.Context))
                throw new InvalidOperationException("Fire intent requires its owning simulation context.");
            // RequireActive is internal to Sim. An empty batch uses its public active-tick guard
            // without publishing events, changing sequence numbers or allocating memory.
            c.EmitStableBatch(Span<StableProducedEvent>.Empty);
            long tick = c.Clock.Tick;
            if (lastStepTick == tick || inStep)
                throw new InvalidOperationException("Fire intent must precede this tick's combat step.");
            if (intent.Kind != FireIntentKind.Aimed && intent.Kind != FireIntentKind.AtTarget)
                throw new ArgumentException("Unknown fire intent kind.", nameof(intent));
            if (!Entities.TryResolve(intent.Shooter, out EntityRef shooter) || IsDead((EntityId)shooter))
                throw new ArgumentException("Fire intent shooter must be alive.", nameof(intent));
            if (intent.Kind == FireIntentKind.AtTarget
                && (!Entities.TryResolve(intent.Target, out EntityRef target) || IsDead((EntityId)target)))
                throw new ArgumentException("Fire intent target must be alive.", nameof(intent));
            int slot = ((EntityId)shooter).Id;
            if ((uint)slot >= (uint)intents.Length)
                throw new InvalidOperationException("Combat entity capacity exceeded.");
            if (intentTick != tick) { ClearIntents(); intentTick = tick; }
            if (intents[slot].Kind == 0)
            {
                if (intentCount == candidates.Length)
                    throw new InvalidOperationException("Combat intent capacity exceeded.");
                intentSlots[intentCount++] = slot;
            }
            intents[slot] = intent;
            intentTicks[slot] = tick;
        }

        private void ClearIntents()
        {
            for (int i = 0; i < intentCount; i++)
            {
                int slot = intentSlots[i];
                intents[slot] = default;
                intentTicks[slot] = 0;
                intentSlots[i] = 0;
            }
            intentCount = 0;
            intentTick = -1;
        }

        public ReadOnlySpan<SpatialHit> Candidates(EntityId shooter, SimFix range)
        {
            if (inStep) QueriesThisStep++;
            var result = sim.SpatialQuery.AllCapped(SpatialQueries.Sphere(Point(shooter), range, unitLayerMask), candidates.Length, candidates);
            if (result.Truncated) throw new InvalidOperationException("Combat unit capacity exceeded; target selection would be incomplete.");
            return candidates.AsSpan(0, result.Written);
        }

        private void BuildLiveCache()
        {
            liveCount = 0; exclusionDisabled = false;
            int layeredCount = 0;
            foreach (EntityId entity in stage.Capture())
            {
                if (Entities.TryGet(colliders, entity, out Collider collider) && (collider.Layer & unitLayerMask) != 0)
                    layeredCount++;
                if (!Entities.TryGet(combatants, entity, out Combatant unit) || unit.Team == 0
                    || !Entities.TryGet(positions, entity, out Position position) || IsDead(entity)) continue;
                // If an external caller grows the world, keep the original path without growing this buffer.
                if (liveCount == liveUnits.Length) { exclusionDisabled = true; continue; }
                liveUnits[liveCount++] = new LiveUnit { Team = unit.Team, X = position.Value.X.Raw, Z = position.Value.Z.Raw };
            }
            exclusionDisabled |= layeredCount > candidates.Length;
            cacheValid = true;
        }

        // Ponytail: O(shooters * live units). Above five hundred units, replace this scan with spatial cells.
        // Derived data is valid only inside Step; AI Requests and external selectors retain the original path.
        public bool MayHaveEnemyWithin(EntityId shooter, SimFix range)
        {
            if (!cacheValid || exclusionDisabled || range.Raw <= 0 || range.Raw > 16384L * SimFix.Scale
                || !Entities.TryGet(combatants, shooter, out Combatant unit)
                || !Entities.TryGet(positions, shooter, out Position position)) return true;
            if (unit.Team == 0) return false;
            for (int i = 0; i < liveCount; i++)
            {
                var other = liveUnits[i];
                if (other.Team == unit.Team) continue;
                long dx = other.X - position.Value.X.Raw, dz = other.Z - position.Value.Z.Raw;
                if (Math.Abs(dx) <= range.Raw && Math.Abs(dz) <= range.Raw) return true;
            }
            return false;
        }

        public bool IsEnemy(EntityId shooter, EntityId candidate) =>
            Entities.TryGet(combatants, candidate, out Combatant other) && Unit(shooter).Team != 0 && other.Team != 0
            && Unit(shooter).Team != other.Team && !IsDead(candidate);

        // The single teammate test: same non-zero team, alive, and not the shooter itself.
        public bool IsFriend(EntityId shooter, EntityId candidate) =>
            !shooter.Equals(candidate) && Entities.TryGet(combatants, candidate, out Combatant other)
            && Unit(shooter).Team != 0 && other.Team == Unit(shooter).Team && !IsDead(candidate);

        public bool Visible(EntityId shooter, EntityId target)
        {
            var from = AimPoint(Point(shooter)); var to = AimPoint(Point(target));
            if (Horizontal(to - from).LengthSquared.Sign == 0) return true;
            var delta = to - from;
            // Explicit AtTarget intents keep the wall-only line of sight; the Aimed and Auto paths use Trace.
            return !sim.SpatialQuery.Any(SpatialQueries.Ray(from, delta, delta.Length, wallMask));
        }

        public void Step(SimContext c)
        {
            QueriesThisStep = 0; cacheValid = false; inStep = true;
            lastStepTick = c.Clock.Tick;
            try
            {
                BuildLiveCache();
                long tick = c.Clock.Tick;
                fireCount = 0; strikeCount = 0;
                foreach (EntityId shooter in stage.Capture())
                {
                    if (!Entities.TryGet(combatants, shooter, out Combatant unit)
                        || !Entities.TryGet(positions, shooter, out Position position)) continue;
                    if (IsDead(shooter))
                    {
                        if (pack.HasMelee) CancelMelee(shooter);
                        continue;
                    }
                    bool hasIntent = intentCount != 0 && (uint)shooter.Id < (uint)intents.Length && intents[shooter.Id].Kind != 0
                        && intentTicks[shooter.Id] == tick && intents[shooter.Id].Shooter.Equals(Entities.StableOf(shooter));
                    // A unit that carries melee may spend this tick on a swing; nothing below runs for it then.
                    if (pack.HasMelee && MeleeStep(c, shooter, in unit, in position, tick, hasIntent)) continue;
                    if (unit.Weapon < 0 || unit.DueMilli > tick * 1000) continue;
                    WeaponDef weapon = pack.Weapons[unit.Weapon];
                    if (hasIntent && intents[shooter.Id].Kind == FireIntentKind.AtTarget)
                    {
                        FireAtTarget(c, shooter, in unit, in position, in weapon, tick, in intents[shooter.Id]);
                        continue;
                    }
                    SimVec3 origin = position.Value, direction;
                    if (hasIntent)
                    {
                        direction = Horizontal(intents[shooter.Id].Point - origin);
                        if (direction.LengthSquared.Sign == 0) continue;
                    }
                    else
                    {
                        if (controlled != null && controlled.Current.Equals(shooter)) continue;
                        if (!pack.Selector.TrySelect(this, shooter, weapon.FalloffEnd, out EntityId target)) continue;
                        direction = Horizontal(Point(target) - origin);
                        // Zero-direction rule: a target standing on the shooter gives no heading, so the shot goes along +X
                        // and takes the same ally check, spread, wall and teammate handling as any other ray.
                        // PLACEHOLDER: +X is arbitrary; switch to the unit's facing once units have one (block 5 C1, T1b/H1).
                        if (direction.LengthSquared.Sign == 0) direction = new SimVec3(SimFix.FromInt(1), default, default);
                        if (AllyBlocks(shooter, origin, direction)
                            && sim.Rng.NextInt(allyStream, 65536) >= pack.AllyCheckMissRaw)
                        {
                            Entities.Set(combatants, shooter, new Combatant(unit.Team, unit.Weapon, unit.Ammo,
                                tick * 1000 + pack.AllyHoldMilli, unit.SpreadStored, unit.SpreadLastMilli));
                            continue;
                        }
                    }
                    int angle = weapon.Spread.AngleAt(unit.SpreadStored, unit.SpreadLastMilli, tick * 1000);
                    if (angle > 0) direction = Rotate(direction, angle, sim.Rng.NextInt(spreadStream, 65536));
                    FireRay(c, shooter, in unit, in position, in weapon, tick, origin, direction, angle);
                }
            }
            finally { cacheValid = false; inStep = false; ClearIntents(); }
        }

        private enum Struck : byte { Open, Wall, Enemy, Friend }

        // Rotates a horizontal direction by a uniform offset in [-angle, +angle] (thousandths of a degree) chosen by roll.
        private static SimVec3 Rotate(SimVec3 direction, int angleMilli, int roll)
        {
            long offset = angleMilli * (2L * roll - 65535) / 65535;
            SimFix turns = SimFix.Ratio(offset, 360000);
            SimFix sin = SimMath.Sin(turns), cos = SimMath.Cos(turns);
            // Fixed-point products would truncate a sub-millimetre vector to zero; its direction alone is what matters.
            if (Math.Abs(direction.X.Raw) < SimFix.Scale && Math.Abs(direction.Z.Raw) < SimFix.Scale) direction = direction.Normalized;
            return new SimVec3(direction.X * cos - direction.Z * sin, default, direction.X * sin + direction.Z * cos);
        }

        // First living unit (any team but 0, never the shooter) or wall along a ray; ties go to the wall, then the smaller StableId.
        private Struck Trace(EntityId shooter, SimVec3 origin, SimVec3 direction, SimFix range, out EntityId hit, out SimFix distance)
        {
            hit = default; distance = range;
            var result = sim.SpatialQuery.AllCapped(SpatialQueries.Ray(origin, direction, range, unitLayerMask), candidates.Length, candidates);
            if (result.Truncated) throw new InvalidOperationException("Combat unit capacity exceeded; target selection would be incomplete.");
            bool found = false;
            SimFix best = default; StableId bestId = default;
            for (int i = 0; i < result.Written; i++)
            {
                EntityId entity = candidates[i].Entity;
                if (!IsFriend(shooter, entity) && !IsEnemy(shooter, entity)) continue;
                StableId id = Entities.StableOf(entity);
                SimFix d = candidates[i].Distance;
                if (found && (d.Raw > best.Raw || (d.Raw == best.Raw && id.CompareTo(bestId) >= 0))) continue;
                hit = entity; best = d; bestId = id; found = true;
            }
            if (sim.SpatialQuery.Nearest(SpatialQueries.Ray(origin, direction, range, wallMask), out SpatialHit wall)
                && (!found || wall.Distance.Raw <= best.Raw))
            {
                hit = default; distance = wall.Distance;
                return Struck.Wall;
            }
            if (!found) return Struck.Open;
            distance = best;
            return IsFriend(shooter, hit) ? Struck.Friend : Struck.Enemy;
        }

        // AI check: is a living teammate the first thing on the straight line to the chosen target (no spread)?
        private bool AllyBlocks(EntityId shooter, SimVec3 origin, SimVec3 direction) =>
            Trace(shooter, origin, direction, direction.Length, out _, out _) == Struck.Friend;

        private void FireAtTarget(SimContext c, EntityId shooter, in Combatant unit, in Position position, in WeaponDef weapon,
            long tick, in FireIntent intent)
        {
            if (!Entities.TryResolve(intent.Target, out EntityRef resolved)) return;
            EntityId target = (EntityId)resolved;
            SimVec3 origin = position.Value;
            if (!IsEnemy(shooter, target) || !Entities.TryGet(positions, target, out Position targetPosition)
                || Horizontal(targetPosition.Value - origin).LengthSquared > new SimVec3(weapon.FalloffEnd, default, default).LengthSquared
                || !Visible(shooter, target)) return;
            Fire(c, shooter, target, in unit, in position, in weapon, tick, AimPoint(origin), AimPoint(targetPosition.Value),
                targetPosition.Value, FireOutcome.Hit, unit.SpreadStored, unit.SpreadLastMilli);
        }

        private void FireRay(SimContext c, EntityId shooter, in Combatant unit, in Position position, in WeaponDef weapon,
            long tick, SimVec3 origin, SimVec3 direction, int angle)
        {
            Struck struck = Trace(shooter, origin, direction, weapon.FalloffEnd, out EntityId hit, out SimFix distance);
            SimVec3 end = origin + direction.Normalized * distance;
            int stored = weapon.Spread.IsNone ? 0 : weapon.Spread.StoredAfter(angle);
            long last = weapon.Spread.IsNone ? 0 : tick * 1000;
            if (struck == Struck.Enemy || (struck == Struck.Friend && pack.FriendlyFire))
                Fire(c, shooter, hit, in unit, in position, in weapon, tick, origin, end, Point(hit),
                    struck == Struck.Enemy ? FireOutcome.Hit : FireOutcome.FriendHit, stored, last);
            else
                Miss(shooter, struck == Struck.Friend ? Entities.StableOf(hit) : default, in unit, in weapon, tick, origin, end,
                    struck == Struck.Wall ? FireOutcome.WallBlocked : struck == Struck.Friend ? FireOutcome.FriendBlocked : FireOutcome.Open,
                    stored, last);
        }

        private Combatant Settle(in Combatant unit, in WeaponDef weapon, long tick, int spreadStored, long spreadLast)
        {
            long start = unit.DueMilli > (tick - 1) * 1000 ? unit.DueMilli : tick * 1000;
            int ammo = unit.Ammo - 1;
            long due = start + (ammo == 0 ? weapon.ReloadMilli : weapon.FireIntervalMilli);
            return new Combatant(unit.Team, unit.Weapon, ammo == 0 ? weapon.Magazine : ammo, due, spreadStored, spreadLast);
        }

        private void Fire(SimContext c, EntityId shooter, EntityId target, in Combatant unit,
            in Position position, in WeaponDef weapon, long tick, SimVec3 origin, SimVec3 recordEnd, SimVec3 targetCenter,
            FireOutcome outcome, int spreadStored, long spreadLast)
        {
            SimFix distance = Horizontal(targetCenter - position.Value).Length;
            long falloff = weapon.FalloffRaw(distance);
            int roll = sim.Rng.NextInt(hitStream, 65536);
            long damage = pack.Resolver.DamageRaw(in weapon, falloff, roll, pack.MaxHealthRaw);
            StableId source = Entities.StableOf(shooter), victim = Entities.StableOf(target);
            c.Damage(sim.GameplayTarget(victim), new GameplayApplyKey(source, (ulong)tick, 0, source, (ulong)tick, 0), 1, damage);
            Entities.Set(combatants, shooter, Settle(in unit, in weapon, tick, spreadStored, spreadLast));
            fires[fireCount++] = new FireRecord(source, victim, origin, recordEnd, distance, damage, unit.Team, outcome);
        }

        private void Miss(EntityId shooter, StableId blocker, in Combatant unit, in WeaponDef weapon, long tick,
            SimVec3 origin, SimVec3 end, FireOutcome outcome, int spreadStored, long spreadLast)
        {
            Entities.Set(combatants, shooter, Settle(in unit, in weapon, tick, spreadStored, spreadLast));
            fires[fireCount++] = new FireRecord(Entities.StableOf(shooter), blocker, origin, end, Horizontal(end - origin).Length, 0, unit.Team, outcome);
        }

        public void SaveState(SimStateWriter writer)
        { writer.UInt64(pack.PolicyDigest); writer.UInt64(wallMask); writer.UInt64(unitLayerMask); if (controlled != null) writer.Bool(true); }
        public void LoadState(SimStateReader reader)
        {
            cacheValid = false; ClearIntents(); lastStepTick = sim.Clock.Tick;
            if (reader.UInt64() != pack.PolicyDigest || reader.UInt64() != wallMask || reader.UInt64() != unitLayerMask)
                throw new InvalidOperationException("Combat policy or layer masks differ from the saved configuration.");
            if (controlled != null && !reader.Bool())
                throw new InvalidOperationException("Combat control mode differs from the saved configuration.");
            fireCount = 0; strikeCount = 0;
        }
        public void WriteState(StateHash hash)
        { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add((ulong)wallMask); hash.Add((ulong)unitLayerMask); if (controlled != null) hash.Add(true); }
    }
}
