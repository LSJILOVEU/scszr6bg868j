using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    // PROJ-1: what a projectile ended on this tick. Origin: where it was fired (the muzzle); End: where it stopped.
    // Distance: for Hit and FriendHit the falloff distance (launch foot to the struck unit's foot now, as a shot's), otherwise
    // the length from Origin to End. Target: the struck unit, or the teammate that stopped it; default otherwise.
    public readonly struct ProjectileImpact
    {
        public readonly StableId Launcher, Target;
        public readonly int Team, Weapon;
        public readonly long LaunchTick;
        public readonly SimVec3 Origin, End;
        public readonly SimFix Distance;
        public readonly long DamageRaw;
        public readonly FireOutcome Outcome;
        public ProjectileImpact(StableId launcher, StableId target, int team, int weapon, long launchTick, SimVec3 origin, SimVec3 end,
            SimFix distance, long damageRaw, FireOutcome outcome)
        {
            Launcher = launcher; Target = target; Team = team; Weapon = weapon; LaunchTick = launchTick;
            Origin = origin; End = end; Distance = distance; DamageRaw = damageRaw; Outcome = outcome;
        }
    }

    // PROJ-1: one projectile in flight. Its position after k ticks of flight is Origin + Step * k (never accumulated).
    public readonly struct ProjectileView
    {
        public readonly StableId Launcher;
        public readonly int Team, Weapon, Flights;
        public readonly long LaunchTick;
        public readonly SimVec3 Origin, Step;
        public ProjectileView(StableId launcher, int team, int weapon, long launchTick, SimVec3 origin, SimVec3 step, int flights)
        { Launcher = launcher; Team = team; Weapon = weapon; LaunchTick = launchTick; Origin = origin; Step = step; Flights = flights; }
        public SimVec3 PositionAt(long tick) => Origin + Step * SimFix.FromInt((int)(tick - LaunchTick));
    }

    // PROJ-1: flies the projectiles combat fires (Order 31, right after combat). Registered only when the pack has a projectile
    // weapon, so a world without one keeps its systems, random streams, state bytes and hashes. Each tick, in firing order,
    // every projectile fired on an earlier tick tests the segment from its (k - 1)-th to its k-th position, the last one cut at
    // the weapon's FalloffEnd, and ends on the first unit, wall, ground or plate on it, or after its last segment.
    public sealed class ProjectileSystem : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly CombatSystem combat;
        private readonly CombatPack pack;
        private readonly RandomStreamId hitStream;
        private readonly StableId[] launchers;
        private readonly int[] teams, weapons;
        private readonly long[] launchTicks;
        private readonly SimVec3[] origins, steps;
        private readonly SimFix[] stepLengths;
        private readonly ProjectileImpact[] impacts;
        private int count, impactCount;
        private long lastTick;

        public ProjectileSystem(Simulation sim, CombatSystem combat, int capacity)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.sim = sim; this.combat = combat; pack = combat.Pack;
            launchers = new StableId[capacity]; teams = new int[capacity]; weapons = new int[capacity];
            launchTicks = new long[capacity]; origins = new SimVec3[capacity]; steps = new SimVec3[capacity];
            stepLengths = new SimFix[capacity]; impacts = new ProjectileImpact[capacity];
            combat.AttachProjectiles(this);
            hitStream = sim.Rng.Stream(HitStreamName);
        }

        public const string HitStreamName = "combat.projectile-hit";
        public int Order => 31;
        public string StateTypeId => "combat.projectiles";
        public int StateVersion => 1;
        public int Capacity => launchers.Length;
        public int Count => count;
        // This tick's landings, in firing order.
        public ReadOnlySpan<ProjectileImpact> Impacts => impacts.AsSpan(0, impactCount);
        public ProjectileView InFlight(int index)
        {
            if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
            return new ProjectileView(launchers[index], teams[index], weapons[index], launchTicks[index], origins[index], steps[index],
                pack.Projectiles[weapons[index]].Flights);
        }
        // The tick of the last step (or of the restore); positions in flight are PositionAt(LastTick).
        public long LastTick => lastTick;

        internal void Add(StableId launcher, int team, int weapon, long tick, SimVec3 origin, SimVec3 direction)
        {
            if (count == launchers.Length) throw new InvalidOperationException("Projectile capacity exceeded.");
            SimVec3 step = direction.Normalized * SimFix.FromRaw(pack.Projectiles[weapon].StepRaw);
            if (step.LengthSquared.Sign == 0) throw new InvalidOperationException("A projectile needs a direction.");
            Put(count++, launcher, team, weapon, tick, origin, step);
        }

        private void Put(int i, StableId launcher, int team, int weapon, long tick, SimVec3 origin, SimVec3 step)
        {
            launchers[i] = launcher; teams[i] = team; weapons[i] = weapon; launchTicks[i] = tick;
            origins[i] = origin; steps[i] = step; stepLengths[i] = step.Length;
        }

        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            lastTick = tick; impactCount = 0;
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (tick > launchTicks[i] && Land(c, i, tick)) continue;
                if (kept != i) Put(kept, launchers[i], teams[i], weapons[i], launchTicks[i], origins[i], steps[i]);
                kept++;
            }
            for (int i = kept; i < count; i++) Put(i, default, 0, 0, 0, default, default);
            count = kept;
        }

        // Flies projectile i through segment k = tick - launch tick; true when it ended.
        private bool Land(SimContext c, int i, long tick)
        {
            int weapon = weapons[i];
            WeaponDef def = pack.Weapons[weapon];
            int flights = pack.Projectiles[weapon].Flights;
            long k = tick - launchTicks[i];
            SimVec3 from = origins[i] + steps[i] * SimFix.FromInt((int)(k - 1));
            // The last segment ends where an instant shot's full range ends: origin + direction * FalloffEnd (review 1, advice 2).
            SimVec3 toward = steps[i];
            SimFix range = stepLengths[i];
            if (k >= flights)
            {
                SimVec3 last = origins[i] + steps[i].Normalized * def.FalloffEnd;
                toward = last - from; range = toward.Length;
            }
            StableId launcher = launchers[i];
            if (range.Raw <= 0) { Record(i, default, from, (from - origins[i]).Length, 0, FireOutcome.Open); return true; }
            CombatSystem.Landing landing = combat.TraceSegment(launcher, teams[i], from, toward, range, out EntityId hit, out SimVec3 end);
            switch (landing)
            {
                case CombatSystem.Landing.Enemy:
                case CombatSystem.Landing.Friend when pack.FriendlyFire:
                {
                    SimVec3 foot = origins[i] - new SimVec3(default, SimFix.FromRaw(combat.MuzzleRaw), default);
                    long damage = combat.LandDamage(c, hitStream, launcher, launchTicks[i], foot, hit, in def, tick, out SimFix distance);
                    Record(i, combat.Entities.StableOf(hit), end, distance, damage,
                        landing == CombatSystem.Landing.Enemy ? FireOutcome.Hit : FireOutcome.FriendHit);
                    return true;
                }
                case CombatSystem.Landing.Friend:
                    Record(i, combat.Entities.StableOf(hit), end, (end - origins[i]).Length, 0, FireOutcome.FriendBlocked);
                    return true;
                case CombatSystem.Landing.Wall:
                    Record(i, default, end, (end - origins[i]).Length, 0, FireOutcome.WallBlocked);
                    return true;
                default:
                    if (k < flights) return false;
                    Record(i, default, end, (end - origins[i]).Length, 0, FireOutcome.Open);
                    return true;
            }
        }

        private void Record(int i, StableId target, SimVec3 end, SimFix distance, long damage, FireOutcome outcome) =>
            impacts[impactCount++] = new ProjectileImpact(launchers[i], target, teams[i], weapons[i], launchTicks[i], origins[i], end,
                distance, damage, outcome);

        // Saved in firing order: launcher, team, weapon, launch tick, origin and per-tick step (raw); the flight length is the
        // weapon's. A restore checks every field against the pack and the clock.
        public void SaveState(SimStateWriter writer)
        {
            writer.UInt64(pack.PolicyDigest);
            writer.Int32(count);
            for (int i = 0; i < count; i++)
            {
                writer.UInt64(launchers[i].Value); writer.Int32(teams[i]); writer.Int32(weapons[i]); writer.Int64(launchTicks[i]);
                writer.Int64(origins[i].X.Raw); writer.Int64(origins[i].Y.Raw); writer.Int64(origins[i].Z.Raw);
                writer.Int64(steps[i].X.Raw); writer.Int64(steps[i].Y.Raw); writer.Int64(steps[i].Z.Raw);
                writer.Int32(pack.Projectiles[weapons[i]].Flights);
            }
        }

        public void LoadState(SimStateReader reader)
        {
            long tick = sim.Clock.Tick;
            SimStateReader.Require(reader.UInt64() == pack.PolicyDigest, "Projectile save belongs to another combat pack.");
            int n = reader.Count(launchers.Length);
            for (int i = 0; i < n; i++)
            {
                var launcher = new StableId(reader.UInt64());
                int team = reader.Int32(), weapon = reader.Int32();
                long launch = reader.Int64();
                SimVec3 origin = ReadPoint(reader, MaxCoordinateRaw), step = ReadPoint(reader, MaxStepRaw);
                int flights = reader.Int32();
                SimStateReader.Require(team >= 0, "Projectile team is negative.");
                SimStateReader.Require(weapon >= 0 && weapon < pack.Weapons.Length && pack.IsProjectile(weapon), "Projectile weapon is not a projectile weapon of the pack.");
                SimStateReader.Require(flights == pack.Projectiles[weapon].Flights, "Projectile flight length differs from its weapon.");
                SimStateReader.Require(launch <= tick && tick - launch < flights, "Projectile launch tick is outside its flight.");
                SimStateReader.Require(step.LengthSquared.Sign != 0, "Projectile step is zero.");
                SimStateReader.Require(i == 0 || launch >= launchTicks[i - 1], "Projectiles are not in firing order.");
                Put(i, launcher, team, weapon, launch, origin, step);
            }
            for (int i = n; i < count; i++) Put(i, default, 0, 0, 0, default, default);
            count = n; impactCount = 0; lastTick = tick;
        }

        // Positions stay within 2^32 raw (65536 m); a step is at most twice the largest per-tick step.
        private const long MaxCoordinateRaw = 1L << 32, MaxStepRaw = 2L * ProjectileSpec.MaxSpeed * SimFix.Scale / 30000;

        private static SimVec3 ReadPoint(SimStateReader reader, long bound)
        {
            long x = reader.Int64(), y = reader.Int64(), z = reader.Int64();
            SimStateReader.Require(x >= -bound && x <= bound && y >= -bound && y <= bound && z >= -bound && z <= bound, "Projectile vector is out of range.");
            return new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add(count);
            for (int i = 0; i < count; i++)
            {
                hash.Add(launchers[i].Value); hash.Add(teams[i]); hash.Add(weapons[i]); hash.Add(launchTicks[i]);
                hash.Add(origins[i].X.Raw); hash.Add(origins[i].Y.Raw); hash.Add(origins[i].Z.Raw);
                hash.Add(steps[i].X.Raw); hash.Add(steps[i].Y.Raw); hash.Add(steps[i].Z.Raw);
            }
        }
    }
}
