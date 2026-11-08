using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private GameplayStamp[] selectedAbilityTargets;
        private long[] selectedAbilityDistances;
        private int selectedAbilityCount, selectedAbilityTotal;
        private static bool AbilityVectorValid(SimVec3 value)
        {
            const long limit = (long)SimVec3.WorldLimitMetres * SimFix.Scale;
            return value.X.Raw >= -limit && value.X.Raw <= limit && value.Y.Raw >= -limit && value.Y.Raw <= limit && value.Z.Raw >= -limit && value.Z.Raw <= limit;
        }
        private GameplayAbilityFailure AbilityTargetLife(GameplayStamp stamp)
        {
            int owner = OwnerIndex(stamp.Id);
            if (owner < 0 || IsDead(owner) || !sim.Entities.TryResolve(stamp.Id, out _))
                return GameplayAbilityFailure.TargetGone;
            if (generations[owner] != stamp.Generation || Generation(stamp.Id) != stamp.Generation)
                return GameplayAbilityFailure.TargetLifeMismatch; // S3_MUTATION:target_generation
            return GameplayAbilityFailure.None;
        }
        private bool AbilityRelation(GameplayStamp actor, GameplayStamp target, GameplayTargetSpec spec)
        {
            int owner = OwnerIndex(target.Id), source = OwnerIndex(actor.Id);
            if (owner < 0 || source < 0)
                return false;
            var a = schema.ArchetypeAt(schema.FindArchetype(archetypes[source]));
            var b = schema.ArchetypeAt(schema.FindArchetype(archetypes[owner]));
            ulong tags = b.Tags | StatusTags(target.Id);
            return (!spec.ExcludeSelf || !actor.Id.Equals(target.Id)) && (tags & spec.RequiredTags) == spec.RequiredTags && (tags & spec.RejectedTags) == 0
                && (spec.Relation == GameplayRelation.Any || (a.FactionKey == b.FactionKey) == (spec.Relation == GameplayRelation.SameFaction));
        }
        private bool AbilityOccluded(SimVec3 source, SimVec3 target, uint mask)
        {
            if (mask == 0)
                return false;
            SimVec3 delta = target - source;
            long range = delta.Length.Raw;
            if (range == 0)
                return false;
            // Public ray directions have the world-coordinate bound. Scale wide differences by two, preserving direction.
            const long limit = (long)SimVec3.WorldLimitMetres * SimFix.Scale;
            if (range > limit)
                return true;
            if (!AbilityVectorValid(delta))
                delta = new SimVec3(SimFix.FromRaw(delta.X.Raw / 2), SimFix.FromRaw(delta.Y.Raw / 2), SimFix.FromRaw(delta.Z.Raw / 2));
            SpatialRequest ray = SpatialQueries.Ray(source, delta, SimFix.FromRaw(range), mask);
            return sim.SpatialQuery.Any(in ray); // S3_MUTATION:occlusion_any
        }
        private GameplayAbilityFailure ValidateExplicitAbilityTarget(GameplayStamp actor, GameplayStamp target, GameplayTargetSpec spec, out long distance)
        {
            distance = 0;
            var failure = AbilityTargetLife(target);
            if (failure != GameplayAbilityFailure.None)
                return failure;
            if (!sim.TryGameplaySpatial(actor.Id, out SimVec3 origin, out _) || !sim.TryGameplaySpatial(target.Id, out SimVec3 point, out uint layer, out ShapeKind shape)
                || shape != ShapeKind.VerticalCapsule)
                return GameplayAbilityFailure.InvalidTarget;
            if ((layer & spec.LayerMask) == 0)
                return GameplayAbilityFailure.LayerRejected;
            if (!AbilityRelation(actor, target, spec))
                return GameplayAbilityFailure.RelationRejected;
            SimVec3 delta = point - origin;
            if (delta.LengthSquared.Raw.Magnitude.CompareTo(FixedUInt128.Multiply((ulong)spec.RangeRaw, (ulong)spec.RangeRaw)) > 0)
                return GameplayAbilityFailure.OutOfRange;
            if (AbilityOccluded(origin, point, spec.OcclusionMask))
                return GameplayAbilityFailure.Occluded;
            distance = delta.Length.Raw;
            return GameplayAbilityFailure.None;
        }
        private GameplayAbilityFailure SelectAbilityTargets(GameplayStamp actor, GameplayAbilityDef definition, GameplayInputCommand input)
        {
            selectedAbilityCount = selectedAbilityTotal = 0;
            var spec = definition.Target;
            if (input.TargetMode != spec.Mode || spec.Mode == GameplayTargetMode.Explicit != input.HasExplicitTarget)
                return GameplayAbilityFailure.InvalidTarget;
            if (spec.Mode == GameplayTargetMode.Self)
            {
                selectedAbilityTargets[0] = actor;
                selectedAbilityDistances[0] = 0;
                selectedAbilityCount = selectedAbilityTotal = 1;
                return GameplayAbilityFailure.None;
            }
            if (spec.Mode == GameplayTargetMode.Explicit)
            {
                var failure = ValidateExplicitAbilityTarget(actor, input.ExplicitTarget, spec, out long distance);
                if (failure != GameplayAbilityFailure.None)
                    return failure;
                selectedAbilityTargets[0] = input.ExplicitTarget;
                selectedAbilityDistances[0] = distance;
                selectedAbilityCount = selectedAbilityTotal = 1;
                return GameplayAbilityFailure.None;
            }
            if (sim.SpatialQuery == null || !sim.TryGameplaySpatial(actor.Id, out SimVec3 origin, out _))
                return GameplayAbilityFailure.InvalidTarget;
            SimVec3 center = spec.UseTargetPoint ? input.TargetPoint : origin;
            if (spec.UseTargetPoint && (center - origin).LengthSquared.Raw.Magnitude.CompareTo(FixedUInt128.Multiply((ulong)spec.RangeRaw, (ulong)spec.RangeRaw)) > 0)
                return GameplayAbilityFailure.OutOfRange;
            SpatialRequest request;
            switch (spec.Shape)
            {
                case GameplayTargetShape.Ray:
                    if (input.AimDirection.LengthSquared.Sign == 0)
                        return GameplayAbilityFailure.InvalidTarget;
                    request = SpatialQueries.Ray(center, input.AimDirection, SimFix.FromRaw(spec.RangeRaw), spec.LayerMask);
                    break;
                case GameplayTargetShape.Sphere:
                    request = SpatialQueries.Sphere(center, SimFix.FromRaw(spec.RadiusRaw), spec.LayerMask);
                    break;
                case GameplayTargetShape.Sector:
                    if (input.AimDirection.Y.Raw != 0 || input.AimDirection.X.Raw == 0 && input.AimDirection.Z.Raw == 0)
                        return GameplayAbilityFailure.InvalidTarget;
                    request = SpatialQueries.Sector(center, SimFix.FromRaw(spec.RangeRaw), input.AimDirection, spec.CosineHalfAnglePermille, SimFix.FromRaw(spec.HalfHeightRaw), spec.LayerMask);
                    break;
                default:
                    if ((input.TargetPoint - origin).LengthSquared.Raw.Magnitude.CompareTo(FixedUInt128.Multiply((ulong)spec.RangeRaw, (ulong)spec.RangeRaw)) > 0)
                        return GameplayAbilityFailure.OutOfRange;
                    request = SpatialQueries.Capsule(origin, input.TargetPoint, SimFix.FromRaw(spec.RadiusRaw), spec.LayerMask);
                    break;
            }
            bool filtered = spec.ExcludeSelf || spec.Relation != GameplayRelation.Any || spec.RequiredTags != 0 || spec.RejectedTags != 0 || spec.OcclusionMask != 0;
            if (spec.Selection == GameplayTargetSelection.Nearest && !filtered)
            {
                if (!sim.SpatialQuery.Nearest(in request, out SpatialHit hit))
                    return GameplayAbilityFailure.NoTarget; // S3_MUTATION:target_nearest
                StableId id = sim.Entities.StableOf(hit.Entity);
                if (OwnerIndex(id) >= 0 && AbilityTargetLife(new GameplayStamp(id, Generation(id))) == GameplayAbilityFailure.None)
                {
                    selectedAbilityTargets[0] = new GameplayStamp(id, Generation(id));
                    selectedAbilityDistances[0] = hit.Distance.Raw;
                    selectedAbilityCount = selectedAbilityTotal = 1;
                    return GameplayAbilityFailure.None;
                }
                // An unbound or dead nearest hit must fall back to the complete ordered hit set.
            }
            // Filter the complete distance/StableId ordered set, never a truncated prefix.
            SpatialQueryResult result = sim.SpatialQuery.AllCapped(in request, abilityHits.Length, abilityHits);
            if (result.Truncated)
                throw new SimBoundaryException("M0C_ABILITY_QUERY_SCRATCH");
            int k = spec.Selection == GameplayTargetSelection.Nearest ? 1 : spec.MaximumTargets;
            for (int i = 0; i < result.Written; i++)
            {
                StableId id = sim.Entities.StableOf(abilityHits[i].Entity);
                if (OwnerIndex(id) < 0)
                    continue;
                GameplayStamp target = new GameplayStamp(id, Generation(id));
                if (AbilityTargetLife(target) != GameplayAbilityFailure.None)
                    continue; // TASK_B:living_target
                if (!AbilityRelation(actor, target, spec))
                    continue;
                // An occlusion check aims at the target centre; a box target is not admitted there in block 3a.
                if (spec.OcclusionMask != 0 && sim.TryGameplaySpatial(id, out SimVec3 targetPoint, out _, out ShapeKind shape)
                    && (shape != ShapeKind.VerticalCapsule || AbilityOccluded(origin, targetPoint, spec.OcclusionMask)))
                    continue;
                selectedAbilityTotal++; // S3_MUTATION:target_total_hits
                if (selectedAbilityCount < k)
                {
                    selectedAbilityTargets[selectedAbilityCount] = target;
                    selectedAbilityDistances[selectedAbilityCount] = abilityHits[i].Distance.Raw;
                    selectedAbilityCount++;
                }
            }
            if (selectedAbilityTotal == 0)
                return GameplayAbilityFailure.NoTarget;
            if (spec.RequiresAll && selectedAbilityTotal > selectedAbilityCount)
                return GameplayAbilityFailure.TargetOverflow; // S3_MUTATION:target_overflow
            return GameplayAbilityFailure.None;
        }
        private GameplayAbilityFailure RefreshAbilityTargets(int index)
        {
            GameplayCastRow row = abilityCasts[index];
            var definition = schema.AbilityAt(schema.FindAbility(row.AbilityKey));
            if (definition.Target.Timing == GameplayTargetTiming.RequeryAtRelease || row.TargetMode == GameplayTargetMode.Explicit)
            {
                var command = new GameplayInputCommand(row.Owner, row.InputOrdinal, GameplayInputKind.Activate, definition.SlotKey, row.TargetMode,
                    row.HasExplicitTarget ? (GameplayStamp?)row.ExplicitTarget : null, row.AimDirection, row.TargetPoint);
                var failure = SelectAbilityTargets(row.Owner, definition, command);
                if (failure != GameplayAbilityFailure.None)
                    return failure;
                StoreAbilityTargets(index);
                return GameplayAbilityFailure.None;
            }
            for (int i = 0; i < row.TargetCount; i++)
            {
                var failure = AbilityTargetLife(abilityTargets[index * schema.Capacities.TargetsPerCast + i]);
                if (failure != GameplayAbilityFailure.None)
                    return failure;
            }
            return GameplayAbilityFailure.None;
        }
        private void StoreAbilityTargets(int index)
        {
            BeginAbilityCastChange(index);
            int start = index * schema.Capacities.TargetsPerCast;
            for (int i = 0; i < selectedAbilityCount; i++)
            {
                abilityTargets[start + i] = selectedAbilityTargets[i];
                abilityDistances[start + i] = selectedAbilityDistances[i];
            }
            var row = abilityCasts[index];
            row.TargetCount = selectedAbilityCount;
            row.TotalHits = selectedAbilityTotal;
            abilityCasts[index] = row;
            EndAbilityCastChange(index);
        }
    }
}
