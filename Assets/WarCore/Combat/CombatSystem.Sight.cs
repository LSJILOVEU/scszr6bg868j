using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    public sealed partial class CombatSystem
    {
        private struct LiveUnit
        {
            internal int Team;
            internal long X, Z;
        }

        public SimVec3 Point(EntityId entity) => Entities.Read(positions, entity).Value;
        public Combatant Unit(EntityId entity) => Entities.Read(combatants, entity);
        public bool IsDead(EntityId entity) => sim.GameplayLife(Entities.StableOf(entity)).State == GameplayLifeState.Dead;
        private static SimVec3 AimPoint(SimVec3 point) => point + new SimVec3(default, SimFix.Ratio(1, 2), default);
        public static SimVec3 Horizontal(SimVec3 delta) => new SimVec3(delta.X, default, delta.Z);

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

        public bool Visible(EntityId shooter, EntityId target) => LineClear(AimPoint(Point(shooter)), AimPoint(Point(target)));

        // Line of sight from the shooter to a point on the ground (both lifted to the aim height), walls only.
        public bool Visible(EntityId shooter, SimVec3 point) => LineClear(AimPoint(Point(shooter)), AimPoint(point));

        // The one line-of-sight judgement: a horizontal zero length is clear, otherwise nothing of the wall layer on the segment.
        // Explicit AtTarget intents keep the wall-only line of sight; the Aimed and Auto paths use Trace.
        private bool LineClear(SimVec3 from, SimVec3 to)
        {
            if (Horizontal(to - from).LengthSquared.Sign == 0) return true;
            var delta = to - from;
            return !sim.SpatialQuery.Any(SpatialQueries.Ray(from, delta, delta.Length, wallMask));
        }

        private enum Struck : byte { Open, Wall, Enemy, Friend }

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
        internal bool AllyBlocks(EntityId shooter, SimVec3 origin, SimVec3 direction) =>
            AllyBlocks(shooter, origin, direction, direction.Length);
        internal bool AllyBlocks(EntityId shooter, SimVec3 origin, SimVec3 direction, SimFix range) =>
            Trace(shooter, origin, direction, range, out _, out _) == Struck.Friend;

        // AI read port: is a living teammate the first thing on the horizontal straight line from the shooter towards a point,
        // traced out to range (the whole flight of a shot, not only to the point)? A point on the shooter gives no line.
        public bool AllyBlocks(EntityId shooter, SimVec3 point, SimFix range)
        {
            SimVec3 origin = Point(shooter);
            SimVec3 direction = Horizontal(point - origin);
            return direction.LengthSquared.Sign != 0 && AllyBlocks(shooter, origin, direction, range);
        }
    }
}
