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

    public sealed partial class CombatSystem
    {
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
    }
}
