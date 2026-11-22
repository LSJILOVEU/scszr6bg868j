using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    // What a shot ended on. Open = nothing on the line; FriendBlocked = a teammate stopped the bullet without damage.
    public enum FireOutcome : byte { Open = 0, Hit, WallBlocked, FriendBlocked, FriendHit }

    // One shot of this tick. Origin: where the shot started; End: where it stopped (the bullet's real end for aimed and
    // automatic shots). Without a body (legacy) aimed and automatic shots start at the shooter's foot and an explicit target
    // shot records foot + 0.5 m to the target's foot + 0.5 m. ML-3 new mode: Origin is the muzzle point (foot + muzzle
    // height) for all three paths and an explicit target shot ends at the target's viewpoint; CombatSystem.GroundOfFireOrigin
    // turns Origin back into the shooter's ground point. Distance is horizontal; ML-9: 3D on a map with floor plates.
    public readonly struct FireRecord
    {
        public readonly StableId Shooter, Target;
        public readonly SimVec3 Origin, End;
        public readonly SimFix Distance;
        public readonly long DamageRaw;
        public readonly int ShooterTeam;
        public readonly FireOutcome Outcome;
        // STANCE-1: how far above the shooter's foot Origin lies (raw): the muzzle of its stance at the shot with a body, 0 for a
        // legacy aimed or automatic shot, 0.5 m for a legacy explicit target shot.
        public readonly long MuzzleRaw;
        public FireRecord(StableId shooter, StableId target, SimVec3 origin, SimVec3 end, SimFix distance, long damageRaw, int shooterTeam,
            FireOutcome outcome = FireOutcome.Hit, long muzzleRaw = 0)
        {
            Shooter = shooter; Target = target; Origin = origin; End = end; Distance = distance; DamageRaw = damageRaw; ShooterTeam = shooterTeam;
            Outcome = outcome; MuzzleRaw = muzzleRaw;
        }
    }

    public sealed partial class CombatSystem
    {
        // Rotates the horizontal part of a direction about the vertical axis by a uniform offset in [-angle, +angle]
        // (thousandths of a degree) chosen by roll; the vertical component is kept.
        private static SimVec3 Rotate(SimVec3 direction, int angleMilli, int roll)
        {
            long offset = angleMilli * (2L * roll - 65535) / 65535;
            SimFix turns = SimFix.Ratio(offset, 360000);
            SimFix sin = SimMath.Sin(turns), cos = SimMath.Cos(turns);
            // Fixed-point products would truncate a sub-millimetre vector to zero; its direction alone is what matters.
            if (Math.Abs(direction.X.Raw) < SimFix.Scale && Math.Abs(direction.Z.Raw) < SimFix.Scale) direction = direction.Normalized;
            return new SimVec3(direction.X * cos - direction.Z * sin, direction.Y, direction.X * sin + direction.Z * cos);
        }

        private void FireAtTarget(SimContext c, EntityId shooter, in Combatant unit, in Position position, in WeaponDef weapon,
            long tick, in FireIntent intent)
        {
            if (!Entities.TryResolve(intent.Target, out EntityRef resolved)) return;
            EntityId target = (EntityId)resolved;
            SimVec3 origin = position.Value;
            long from = stances != null ? stances.MuzzleRaw(shooter) : targetOriginRaw;
            if (!IsEnemy(shooter, target) || !Entities.TryGet(positions, target, out Position targetPosition)
                || SpanSquared(origin, targetPosition.Value) > new SimVec3(weapon.FalloffEnd, default, default).LengthSquared
                || !Visible(shooter, target)) return;
            // PROJ-1: a projectile weapon fires from the muzzle at the target's viewpoint, without spread, after the same checks.
            if (pack.IsProjectile(unit.Weapon))
            {
                SimVec3 muzzle = Lift(origin, from), toward = Eye(targetPosition.Value, target) - muzzle;
                if (toward.LengthSquared.Sign == 0) return;
                Launch(shooter, in unit, in weapon, tick, muzzle, from, toward, unit.SpreadStored, unit.SpreadLastMilli);
                return;
            }
            // The judgement is the sight line (viewpoint to viewpoint); the record runs from foot + targetOrigin (legacy 0.5 m,
            // new mode the muzzle) to the target's viewpoint, lifted once.
            Fire(c, shooter, target, in unit, in position, in weapon, tick, Lift(origin, from), Eye(targetPosition.Value, target),
                targetPosition.Value, FireOutcome.Hit, unit.SpreadStored, unit.SpreadLastMilli, from);
        }

        private void FireRay(SimContext c, EntityId shooter, in Combatant unit, in Position position, in WeaponDef weapon,
            long tick, SimVec3 origin, SimVec3 direction, int angle)
        {
            Struck struck = Trace(shooter, origin, direction, weapon.FalloffEnd, out EntityId hit, out SimFix distance, out SimVec3 end);
            int stored = weapon.Spread.IsNone ? 0 : weapon.Spread.StoredAfter(angle);
            long last = weapon.Spread.IsNone ? 0 : tick * 1000;
            if (struck == Struck.Enemy || (struck == Struck.Friend && pack.FriendlyFire))
                Fire(c, shooter, hit, in unit, in position, in weapon, tick, origin, end, Point(hit),
                    struck == Struck.Enemy ? FireOutcome.Hit : FireOutcome.FriendHit, stored, last, MuzzleOf(shooter));
            else
                Miss(shooter, struck == Struck.Friend ? Entities.StableOf(hit) : default, in unit, in weapon, tick, origin, end,
                    struck == Struck.Wall ? FireOutcome.WallBlocked : struck == Struck.Friend ? FireOutcome.FriendBlocked : FireOutcome.Open,
                    stored, last, MuzzleOf(shooter));
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
            FireOutcome outcome, int spreadStored, long spreadLast, long muzzleRaw)
        {
            SimFix distance = Span(position.Value, targetCenter);
            long falloff = weapon.FalloffRaw(distance);
            int roll = sim.Rng.NextInt(hitStream, 65536);
            long damage = pack.Resolver.DamageRaw(in weapon, falloff, roll, pack.MaxHealthRaw);
            StableId source = Entities.StableOf(shooter), victim = Entities.StableOf(target);
            damage = SubmitDamage(c, victim, new GameplayApplyKey(source, (ulong)tick, 0, source, (ulong)tick, 0), damage, tick);
            Entities.Set(combatants, shooter, Settle(in unit, in weapon, tick, spreadStored, spreadLast));
            fires[fireCount++] = new FireRecord(source, victim, origin, recordEnd, distance, damage, unit.Team, outcome, muzzleRaw);
        }

        private void Miss(EntityId shooter, StableId blocker, in Combatant unit, in WeaponDef weapon, long tick,
            SimVec3 origin, SimVec3 end, FireOutcome outcome, int spreadStored, long spreadLast, long muzzleRaw)
        {
            Entities.Set(combatants, shooter, Settle(in unit, in weapon, tick, spreadStored, spreadLast));
            fires[fireCount++] = new FireRecord(Entities.StableOf(shooter), blocker, origin, end, Span(origin, end), 0, unit.Team, outcome, muzzleRaw);
        }
    }
}
