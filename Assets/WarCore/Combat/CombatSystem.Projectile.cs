using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // PROJ-1: the combat side of projectiles. A projectile weapon spends the shot as before (Settle: ammo, cooldown, spread)
    // but draws no hit roll, issues no damage and writes no FireRecord on the tick it fires; ProjectileSystem flies it.
    public sealed partial class CombatSystem
    {
        private ProjectileSystem projectileSystem;

        // Bound once by the ProjectileSystem constructor; a restored world binds a fresh one when it is assembled.
        internal void AttachProjectiles(ProjectileSystem system)
        {
            if (!pack.HasProjectiles) throw new InvalidOperationException("The combat pack has no projectile weapon.");
            if (projectileSystem != null) throw new InvalidOperationException("A projectile system is already attached.");
            projectileSystem = system;
        }

        public ProjectileSystem Projectiles => projectileSystem;

        // The pack's muzzle height (raw) over the foot; with a projectile weapon the pack always has a body.
        internal long MuzzleRaw => muzzleRaw;

        // Fires a projectile from origin along direction. The pool is filled first, so a full pool (a broken capacity bound)
        // throws before the shooter's state changes.
        // muzzleRaw: how far above the shooter's foot origin lies (kept with the projectile in a world with stances).
        private void Launch(EntityId shooter, in Combatant unit, in WeaponDef weapon, long tick, SimVec3 origin, long muzzleRaw, SimVec3 direction,
            int spreadStored, long spreadLast)
        {
            if (projectileSystem == null) throw new InvalidOperationException("A projectile weapon fired but no projectile system is attached.");
            projectileSystem.Add(Entities.StableOf(shooter), unit.Team, unit.Weapon, tick, origin, direction, muzzleRaw);
            Entities.Set(combatants, shooter, Settle(in unit, in weapon, tick, spreadStored, spreadLast));
        }

        // The automatic and aimed path: as FireRay, spread already turned into the direction and stored after the shot.
        private void LaunchRay(EntityId shooter, in Combatant unit, in WeaponDef weapon, long tick, SimVec3 origin, SimVec3 direction, int angle)
        {
            int stored = weapon.Spread.IsNone ? 0 : weapon.Spread.StoredAfter(angle);
            long last = weapon.Spread.IsNone ? 0 : tick * 1000;
            Launch(shooter, in unit, in weapon, tick, origin, MuzzleOf(shooter), direction, stored, last);
        }

        internal enum Landing : byte { Flying, Wall, Enemy, Friend }

        // One flight segment of a projectile: from origin along direction for range. The same judgement as Trace, with the
        // team recorded at launch and the launcher excluded by StableId (it may be dead by now): first living unit of a non-zero
        // team or wall, ties to the wall, then the smaller StableId; then ground and floor plates on the cut segment, first wins.
        internal Landing TraceSegment(StableId launcher, int team, SimVec3 origin, SimVec3 direction, SimFix range,
            out EntityId hit, out SimVec3 end)
        {
            hit = default;
            SimFix distance = range;
            var result = sim.SpatialQuery.AllCapped(SpatialQueries.Ray(origin, direction, range, unitLayerMask), candidates.Length, candidates);
            if (result.Truncated) throw new InvalidOperationException("Combat unit capacity exceeded; a projectile's target would be incomplete.");
            bool found = false;
            SimFix best = default; StableId bestId = default;
            for (int i = 0; i < result.Written; i++)
            {
                EntityId entity = candidates[i].Entity;
                if (!Struckable(launcher, team, entity)) continue;
                StableId id = Entities.StableOf(entity);
                SimFix d = candidates[i].Distance;
                if (found && (d.Raw > best.Raw || (d.Raw == best.Raw && id.CompareTo(bestId) >= 0))) continue;
                hit = entity; best = d; bestId = id; found = true;
            }
            Landing landing;
            if (sim.SpatialQuery.Nearest(SpatialQueries.Ray(origin, direction, range, wallMask), out SpatialHit wall)
                && (!found || wall.Distance.Raw <= best.Raw))
            { hit = default; distance = wall.Distance; landing = Landing.Wall; }
            else if (!found) landing = Landing.Flying;
            else { distance = best; landing = Entities.Read(combatants, hit).Team == team ? Landing.Friend : Landing.Enemy; }
            end = origin + direction.Normalized * distance;
            if ((ground != null || plates != null) && LineStop.TryFirst(ground, plates, origin, end, out SimVec3 at))
            { hit = default; end = at; return Landing.Wall; }
            return landing;
        }

        // A unit a projectile of this team can stop on: a combatant of a non-zero team, alive, not the launcher; nothing for a
        // team-0 launcher (as IsFriend and IsEnemy).
        private bool Struckable(StableId launcher, int team, EntityId entity) =>
            team != 0 && Entities.TryGet(combatants, entity, out Combatant other) && other.Team != 0
            && !Entities.StableOf(entity).Equals(launcher) && !IsDead(entity);

        // Damage of a projectile landing on a unit: distance from the launch foot to the unit's foot now (Span), the pack's
        // resolver, one roll from the projectile stream, zero while shielded. The key's action ordinal 2 is used by nothing else
        // (shots 0, melee 1); a unit fires at most once a tick, so (launcher, launch tick) names one projectile.
        internal long LandDamage(SimContext c, RandomStreamId stream, StableId launcher, long launchTick, SimVec3 launchFoot,
            EntityId target, in WeaponDef weapon, long tick, out SimFix distance)
        {
            distance = Span(launchFoot, Point(target));
            long falloff = weapon.FalloffRaw(distance);
            int roll = sim.Rng.NextInt(stream, 65536);
            long damage = pack.Resolver.DamageRaw(in weapon, falloff, roll, pack.MaxHealthRaw);
            StableId victim = Entities.StableOf(target);
            if (Shielded(victim, tick)) damage = 0;
            c.Damage(sim.GameplayTarget(victim), new GameplayApplyKey(launcher, (ulong)launchTick, 0, launcher, (ulong)launchTick, ProjectileActionOrdinal), 1, damage);
            return damage;
        }

        internal const uint ProjectileActionOrdinal = 2;
    }
}
