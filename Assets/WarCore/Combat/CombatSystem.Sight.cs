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
        // The unit's health as stored now (resource 1 of CombatPack.BuildSchema): during a step, before this tick's damage lands.
        public long HealthRaw(StableId unit) => sim.GameplayResource(unit, 1);
        // The legacy eye: a point on the ground lifted 0.5 m. Kept with its meaning; the body-aware points are below.
        public static SimVec3 AimPoint(SimVec3 point) => point + new SimVec3(default, SimFix.Ratio(1, 2), default);
        public static SimVec3 Horizontal(SimVec3 delta) => new SimVec3(delta.X, default, delta.Z);

        // A point lifted by a raw height; a zero lift returns the point itself.
        private static SimVec3 Lift(SimVec3 point, long raw) => raw == 0 ? point : point + new SimVec3(default, SimFix.FromRaw(raw), default);

        // ML-3 viewpoint of a unit: where it looks from and where others look and aim at it (new mode: foot + eye height;
        // a pack without a body: foot + 0.5 m, as AimPoint).
        public SimVec3 EyePoint(EntityId unit) => Lift(Point(unit), EyeRaw(unit));

        // ML-3 muzzle of a unit: where its aimed and automatic shots start (new mode: foot + muzzle height; without a body:
        // the foot).
        public SimVec3 MuzzlePoint(EntityId unit) => Lift(Point(unit), MuzzleOf(unit));
        internal SimVec3 MuzzleOver(SimVec3 foot, EntityId unit) => Lift(foot, MuzzleOf(unit));

        // ML-3 where the built-in aimed and automatic lines at a unit end (new mode: its viewpoint; without a body: its foot).
        // VSPREAD-1: the built-in decision aims along ShotEnd(shooter, target) - MuzzlePoint(shooter), which is this point unless
        // the pack's vertical spread aims at the middle of the body; a game-layer IAutoFire should do the same.
        public SimVec3 ShotEnd(EntityId target) => Lift(Point(target), AimEndRaw(target));

        // ML-3: a fire record's origin turned back into the point on the ground under the shooter. New mode: Origin minus the
        // muzzle height (all three shot paths record the muzzle). Without a body: Origin as recorded, including the 0.5 m an
        // explicit target shot has always recorded.
        // STANCE-1: with stances the record carries the muzzle height the shot left at (FireRecord.MuzzleRaw), so a shooter who
        // changed stance since is still traced back to the foot it fired from.
        public SimVec3 GroundOfFireOrigin(in FireRecord fire) => bodied ? Lift(fire.Origin, -(stances != null ? fire.MuzzleRaw : muzzleRaw)) : fire.Origin;

        // One body per pack (v1): a unit's eye and aim end are the pack's. A line that ends on a unit uses that unit's own
        // value, a line that ends on a ground point the pack's standing default (D3).
        // STANCE-1: with stances a unit's eye, aim end (its eye) and muzzle are those of its stance now; ground points keep the
        // standing default.
        private long EyeRaw(EntityId unit) => stances != null ? stances.EyeRaw(unit) : eyeRaw;
        private long AimEndRaw(EntityId unit) => stances != null ? stances.EyeRaw(unit) : aimEndRaw;
        private long MuzzleOf(EntityId unit) => stances != null ? stances.MuzzleRaw(unit) : muzzleRaw;
        // STANCE-1 (C-e): the spread factor of the unit's stance in permille: the stance now, the wider of the two while changing,
        // 1000 without stances. Read only; the shot paths' spread is AIM-1's (ShotAngle takes it as its stance row).
        public int StanceSpreadPermille(EntityId unit) => stances != null ? stances.SpreadPermille(unit) : 1000;
        private SimVec3 Eye(SimVec3 ground) => Lift(ground, eyeRaw);
        private SimVec3 Eye(SimVec3 foot, EntityId unit) => Lift(foot, EyeRaw(unit));

        // New mode: the unit-layer query centre over a foot (the body's centre); without a body the foot itself.
        private SimVec3 Center(SimVec3 foot) => Lift(foot, centerRaw);

        // D2a-3: in new mode every shooter's collider must be as tall as the pack's body. Read-only, no random numbers.
        private void RequireBodyHeight(EntityId unit)
        {
            if (Entities.TryGet(colliders, unit, out Collider collider) && collider.Height.Raw != bodyHeightRaw)
                throw new InvalidOperationException("Unit collider height " + collider.Height.Raw + " raw differs from the combat pack's body height "
                    + bodyHeightRaw + " raw.");
        }

        public ReadOnlySpan<SpatialHit> Candidates(EntityId shooter, SimFix range)
        {
            if (inStep) QueriesThisStep++;
            var result = sim.SpatialQuery.AllCapped(SpatialQueries.Sphere(Center(Point(shooter)), range, unitLayerMask), candidates.Length, candidates);
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

        // Viewpoint to viewpoint (ML-3: each unit's own eye height; without a body both 0.5 m up).
        public bool Visible(EntityId shooter, EntityId target) => LineClear(EyePoint(shooter), EyePoint(target));

        // Line of sight from the shooter to a point on the ground (both lifted to the eye height; for the point the pack's
        // standing default), walls and (L9) the ground.
        public bool Visible(EntityId shooter, SimVec3 point) => LineClear(EyePoint(shooter), Eye(point));

        // Line of sight between two points on the ground (both lifted to the eye height): what a unit standing at one
        // would see of a unit standing at the other. No unit has to stand at either.
        public bool Sees(SimVec3 from, SimVec3 to) => LineClear(Eye(from), Eye(to));

        // The unit's body (its collider radius), moved in a straight line from one point on the ground to another, would
        // touch a living teammate (never the unit itself): somebody stands in the way, or where it would end up.
        public bool FriendOnWalk(EntityId unit, SimVec3 from, SimVec3 to)
        {
            var result = sim.SpatialQuery.AllCapped(SpatialQueries.Capsule(Center(from), Center(to), Entities.Get(colliders, unit).Radius, unitLayerMask), candidates.Length, candidates);
            if (result.Truncated) throw new InvalidOperationException("Combat unit capacity exceeded; the teammate check would be incomplete.");
            for (int i = 0; i < result.Written; i++)
                if (IsFriend(unit, candidates[i].Entity)) return true;
            return false;
        }

        // A wall stands on the sight line from one point on the ground toward another (both lifted to the eye height),
        // within reach of the first. Walls only: the ground is not tested. Two points above one another have no line.
        public bool WallWithin(SimVec3 from, SimVec3 toward, SimFix reach)
        {
            SimVec3 origin = Eye(from), delta = Eye(toward) - origin;
            if (Horizontal(delta).LengthSquared.Sign == 0) return false;
            SimFix length = delta.Length;
            return sim.SpatialQuery.Any(SpatialQueries.Ray(origin, delta, length < reach ? length : reach, wallMask));
        }

        // The one line-of-sight judgement: a horizontal zero length is clear, otherwise nothing of the wall layer on the segment
        // and (L9) the segment nowhere under the ground. ML-9: on a map with floor plates the segment must also pass through no
        // plate, and a segment straight up or down is tested against the plates alone.
        // Explicit AtTarget intents use this; the Aimed and Auto paths use Trace.
        private bool LineClear(SimVec3 from, SimVec3 to)
        {
            if (Horizontal(to - from).LengthSquared.Sign == 0) return plates == null || !plates.Blocks(from, to);
            var delta = to - from;
            if (sim.SpatialQuery.Any(SpatialQueries.Ray(from, delta, delta.Length, wallMask))) return false;
            return !LineStop.TryFirst(ground, plates, from, to, out _);
        }

        private enum Struck : byte { Open, Wall, Enemy, Friend }

        // First living unit (any team but 0, never the shooter) or wall along a ray; ties go to the wall, then the smaller StableId.
        // end is where the ray stops. L9: the ray lifted by 0.5 m (the eye height of AimPoint) is then tested against the ground
        // up to that end; if it goes under, the shot is a wall hit at the first blocked sample lowered back by 0.5 m, recorded
        // as is (no second rounding). The ground wins a tie with the unit or wall at the end.
        // ML-3 new mode: the ray already starts at the muzzle, so it is tested against the ground as it flies (no lift, the
        // blocked sample is the end as is).
        // ML-9: the floor plates are tested with the ground on the same (lifted) segment, after the nearest unit or wall has cut it
        // to end: the first of ground and plates wins (LineStop), and a plate touching the unit or wall at the end wins too. The
        // end's rounding (a few raw) is accepted, as for the ground.
        private Struck Trace(EntityId shooter, SimVec3 origin, SimVec3 direction, SimFix range, out EntityId hit, out SimFix distance, out SimVec3 end)
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
            Struck struck;
            if (sim.SpatialQuery.Nearest(SpatialQueries.Ray(origin, direction, range, wallMask), out SpatialHit wall)
                && (!found || wall.Distance.Raw <= best.Raw))
            { hit = default; distance = wall.Distance; struck = Struck.Wall; }
            else if (!found) struck = Struck.Open;
            else { distance = best; struck = IsFriend(shooter, hit) ? Struck.Friend : Struck.Enemy; }
            end = origin + direction.Normalized * distance;
            if (ground != null || plates != null)
            {
                SimVec3 lift = new SimVec3(default, SimFix.FromRaw(groundLiftRaw), default);
                if (LineStop.TryFirst(ground, plates, origin + lift, end + lift, out SimVec3 at))
                { hit = default; end = at - lift; distance = (end - origin).Length; return Struck.Wall; }
            }
            return struck;
        }

        // AI check: is a living teammate the first thing on the straight line to the chosen target (no spread)?
        internal bool AllyBlocks(EntityId shooter, SimVec3 origin, SimVec3 direction) =>
            AllyBlocks(shooter, origin, direction, direction.Length);
        internal bool AllyBlocks(EntityId shooter, SimVec3 origin, SimVec3 direction, SimFix range) =>
            Trace(shooter, origin, direction, range, out _, out _, out _) == Struck.Friend;

        // AI read port: is a living teammate the first thing on the straight (3D) line from the shooter towards a point, traced
        // out to range (the whole flight of a shot, not only to the point)? It is the line an aimed intent at the point flies
        // along (ML-3: from the muzzle to AimedEnd: the point lifted to the standing eye, or with a vertical spread aimed at the
        // middle of the body an enemy at the player's point, VSPREAD-1). A point straight above or below the shooter
        // gives no line, except on a map with plates when the point's height differs from the shooter's foot (ML-9).
        public bool AllyBlocks(EntityId shooter, SimVec3 point, SimFix range)
        {
            SimVec3 origin = MuzzlePoint(shooter);
            SimVec3 direction = AimedEnd(shooter, point) - origin;
            bool line = Horizontal(direction).LengthSquared.Sign != 0 || Overhead(Point(shooter), point) && direction.LengthSquared.Sign != 0;
            return line && AllyBlocks(shooter, origin, direction, range);
        }
    }
}
