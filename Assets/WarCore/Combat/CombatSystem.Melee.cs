using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    public enum StrikeKind : byte { Started = 1, Landed = 2, Whiffed = 3 }

    // What the melee code did this tick: a swing started, a swing landed on one target (one record per target), or landed on nobody.
    public readonly struct StrikeRecord
    {
        public readonly StrikeKind Kind;
        public readonly StableId Striker, Target;
        public readonly long Tick, DamageRaw;
        public readonly SimVec3 Origin;
        public StrikeRecord(StrikeKind kind, StableId striker, StableId target, long tick, long damageRaw, SimVec3 origin)
        { Kind = kind; Striker = striker; Target = target; Tick = tick; DamageRaw = damageRaw; Origin = origin; }
    }

    // Melee swings (WP-20). One swing is: lock origin and heading on the start tick, wait the windup, then on that one tick look up the
    // sector again, keep the visible enemies, order them by (distance, StableId), take the nearest MaxTargets and damage each once.
    // Looking up targets and applying the effect are separate steps so that healing or a delayed throw can reuse the first one.
    public sealed partial class CombatSystem
    {
        private ComponentType<MeleeState> meleeStates;
        private StrikeRecord[] strikes = Array.Empty<StrikeRecord>();
        private MeleeTarget[] meleeTargets = Array.Empty<MeleeTarget>();
        private int strikeCount;

        private struct MeleeTarget
        {
            internal EntityId Entity;
            internal StableId Id;
            internal SimWide Distance;
        }

        public ReadOnlySpan<StrikeRecord> Strikes => strikes.AsSpan(0, strikeCount);

        // True when the unit carries a melee spec. PlayerFire uses it to let a gunless unit attack.
        public bool CanMelee(EntityId unit) =>
            pack.HasMelee && Entities.TryGet(meleeStates, unit, out MeleeState state) && state.Spec >= 0;

        // Gives a unit its melee spec (an index into the pack's specs). Call after the unit exists; the component is registered by the constructor.
        public void SetMelee(EntityId unit, int spec)
        {
            if (!pack.HasMelee || (uint)spec >= (uint)pack.Melee.Length)
                throw new ArgumentException("The melee spec index does not name a melee spec of the combat pack.", nameof(spec));
            Entities.Set(meleeStates, unit, new MeleeState(spec));
        }

        private void InitMelee()
        {
            if (!pack.HasMelee) return;
            meleeStates = Entities.Register<MeleeState>(MeleeState.ComponentId, new MeleeStateSerializer());
            strikes = new StrikeRecord[checked(candidates.Length * (pack.MaxHitsPerAction + 2))];
            meleeTargets = new MeleeTarget[candidates.Length];
        }

        // A dead unit's pending swing never lands; the saved state goes back to the canonical idle form (no locked origin or heading).
        private void CancelMelee(EntityId shooter)
        {
            if (Entities.TryGet(meleeStates, shooter, out MeleeState state) && state.Pending)
                Entities.Set(meleeStates, shooter, new MeleeState(state.Spec, -1, state.NextStartTick));
        }

        // Returns true when the unit spent this tick on melee (waiting, landing, recovering or starting) and must not fire.
        private bool MeleeStep(SimContext c, EntityId shooter, in Combatant unit, in Position position, long tick, bool hasIntent)
        {
            if (!Entities.TryGet(meleeStates, shooter, out MeleeState state) || state.Spec < 0) return false;
            MeleeSpec spec = pack.Melee[state.Spec];
            if (state.Pending)
            {
                if (tick >= state.StartTick + spec.WindupTicks) Land(c, shooter, in state, in spec, tick);
                return true;
            }
            if (tick < state.NextStartTick) return true;
            SimVec3 origin = position.Value, direction;
            bool gunless = unit.Weapon < 0;
            if (hasIntent)
            {
                FireIntent intent = intents[shooter.Id];
                if (intent.Kind == FireIntentKind.Aimed)
                {
                    direction = Horizontal(intent.Point - origin);
                    if (direction.LengthSquared.Sign == 0) return false;
                    if (!gunless && direction.LengthSquared > Reach(spec.EngageRange)) return false;
                }
                else
                {
                    if (!Entities.TryResolve(intent.Target, out EntityRef resolved)) return false;
                    EntityId target = (EntityId)resolved;
                    if (!IsEnemy(shooter, target) || !Entities.TryGet(positions, target, out Position targetPosition)) return false;
                    direction = Horizontal(targetPosition.Value - origin);
                    if (direction.LengthSquared > Reach(gunless ? spec.Radius : spec.EngageRange)) return false;
                    // Same placeholder as the gun path: a target standing on the shooter gives no heading, so swing along +X.
                    if (direction.LengthSquared.Sign == 0) direction = new SimVec3(SimFix.FromInt(1), default, default);
                }
            }
            else
            {
                if (controlled != null && controlled.Current.Equals(shooter)) return false;
                if (!TryNearestEnemy(shooter, origin, spec.EngageRange, out SimVec3 toward)) return false;
                direction = toward;
                if (direction.LengthSquared.Sign == 0) direction = new SimVec3(SimFix.FromInt(1), default, default);
            }
            Entities.Set(meleeStates, shooter, new MeleeState(state.Spec, tick, tick + spec.BusyTicks, origin, direction.X.Raw, direction.Z.Raw));
            Record(new StrikeRecord(StrikeKind.Started, Entities.StableOf(shooter), default, tick, 0, origin));
            return true;
        }

        private static SimWide Reach(SimFix range) => new SimVec3(range, default, default).LengthSquared;

        // Nearest living enemy within range (horizontal, ties by StableId) that the shooter can see over the walls.
        private bool TryNearestEnemy(EntityId shooter, SimVec3 origin, SimFix range, out SimVec3 toward)
        {
            toward = default;
            var hits = Candidates(shooter, range);
            bool found = false; SimWide best = default; StableId bestId = default;
            for (int i = 0; i < hits.Length; i++)
            {
                EntityId entity = hits[i].Entity;
                if (!IsEnemy(shooter, entity) || !Entities.TryGet(positions, entity, out Position other)) continue;
                SimVec3 delta = Horizontal(other.Value - origin);
                SimWide distance = delta.LengthSquared;
                if (distance > Reach(range)) continue;
                StableId id = Entities.StableOf(entity);
                if (found && (distance > best || (distance == best && id.CompareTo(bestId) >= 0))) continue;
                if (!Visible(shooter, entity)) continue;
                found = true; best = distance; bestId = id; toward = delta;
            }
            return found;
        }

        private void Land(SimContext c, EntityId shooter, in MeleeState state, in MeleeSpec spec, long tick)
        {
            // The swing is spent whatever it hits; origin and heading are cleared so the saved bytes are canonical.
            Entities.Set(meleeStates, shooter, new MeleeState(state.Spec, -1, state.NextStartTick));
            SimVec3 origin = state.Origin;
            SimVec3 direction = new SimVec3(SimFix.FromRaw(state.DirX), default, SimFix.FromRaw(state.DirZ));
            var request = SpatialQueries.Sector(origin, spec.Radius, direction, spec.CosHalfAnglePermille, spec.HalfHeight, unitLayerMask);
            var result = sim.SpatialQuery.AllCapped(in request, candidates.Length, candidates);
            if (result.Truncated) throw new InvalidOperationException("Combat unit capacity exceeded; melee target selection would be incomplete.");
            int count = 0;
            for (int i = 0; i < result.Written; i++)
            {
                EntityId entity = candidates[i].Entity;
                if (!(IsEnemy(shooter, entity) || (pack.FriendlyFire && IsFriend(shooter, entity)))
                    || !Entities.TryGet(positions, entity, out Position other)) continue;
                var from = origin + new SimVec3(default, SimFix.Ratio(1, 2), default);
                var delta = AimPoint(other.Value) - from;
                if (Horizontal(delta).LengthSquared.Sign != 0 && sim.SpatialQuery.Any(SpatialQueries.Ray(from, delta, delta.Length, wallMask))) continue;
                var item = new MeleeTarget { Entity = entity, Id = Entities.StableOf(entity), Distance = Horizontal(other.Value - origin).LengthSquared };
                int at = count++;
                while (at > 0 && (meleeTargets[at - 1].Distance > item.Distance
                    || (meleeTargets[at - 1].Distance == item.Distance && meleeTargets[at - 1].Id.CompareTo(item.Id) > 0)))
                { meleeTargets[at] = meleeTargets[at - 1]; at--; }
                meleeTargets[at] = item;
            }
            StableId source = Entities.StableOf(shooter);
            int landed = 0;
            // The first MaxTargets by distance are fixed before any damage is tried; a target that is gone is skipped, never replaced.
            int take = count < spec.MaxTargets ? count : spec.MaxTargets;
            for (int i = 0; i < take; i++)
            {
                StableId victim = meleeTargets[i].Id;
                var key = new GameplayApplyKey(source, (ulong)state.StartTick, 0, source, (ulong)state.StartTick, 1, (uint)i);
                GameplayFailure failure = c.Damage(sim.GameplayTarget(victim), key, 1, spec.Damage.Raw);
                switch (failure)
                {
                    case GameplayFailure.None:
                        landed++;
                        Record(new StrikeRecord(StrikeKind.Landed, source, victim, tick, spec.Damage.Raw, origin));
                        break;
                    case GameplayFailure.TargetGone:
                    case GameplayFailure.TargetLifeMismatch:
                        break;
                    default:
                        // Today the Sim reports only the two cases above for damage; a new failure needs its own rule here, not a silent skip.
                        throw new InvalidOperationException("Melee damage was refused: " + failure + ".");
                }
            }
            if (landed == 0) Record(new StrikeRecord(StrikeKind.Whiffed, source, default, tick, 0, origin));
        }

        private void Record(in StrikeRecord record)
        {
            if (strikeCount == strikes.Length) throw new InvalidOperationException("Melee record capacity exceeded.");
            strikes[strikeCount++] = record;
        }
    }
}
