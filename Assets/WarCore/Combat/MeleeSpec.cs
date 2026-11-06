using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // One melee swing, all in whole ticks and metres. The swing starts on tick T (that tick is part of the windup), lands once on
    // T + Windup, and the unit may start again on T + Windup + Active + Recovery. Active and Recovery only delay the next swing.
    // The sector is SpatialQueries.Sector: CosHalfAnglePermille is the cosine of the half angle in thousandths (500 = 60 degrees).
    // A thrust is the same swing with a narrow straight hit shape instead: LineHalfWidth > 0 replaces the sector by a line of that half
    // width and length Radius along the locked heading (SpatialQueries.Capsule, so the ends are rounded and a target counts when its
    // collider touches the shape). CosHalfAnglePermille and HalfHeight are then not used for the hit; LineHalfWidth = 0 is the sector.
    public readonly struct MeleeSpec : IStateHash
    {
        public const int MaxTicks = 600, MaxTargetsLimit = 16;
        public static readonly SimFix MaxReach = SimFix.FromInt(16);
        public static readonly SimFix MaxDamage = SimFix.FromInt(1_000_000);
        public readonly int WindupTicks, ActiveTicks, RecoveryTicks, CosHalfAnglePermille, MaxTargets;
        public readonly SimFix Radius, HalfHeight, Damage, EngageRange, LineHalfWidth;

        public MeleeSpec(int windupTicks, int activeTicks, int recoveryTicks, SimFix radius, int cosHalfAnglePermille,
            SimFix halfHeight, SimFix damage, int maxTargets, SimFix engageRange, SimFix lineHalfWidth = default)
        {
            if (windupTicks < 1 || windupTicks > MaxTicks) throw new ArgumentOutOfRangeException(nameof(windupTicks));
            if (activeTicks < 1 || activeTicks > MaxTicks) throw new ArgumentOutOfRangeException(nameof(activeTicks));
            if (recoveryTicks < 0 || recoveryTicks > MaxTicks) throw new ArgumentOutOfRangeException(nameof(recoveryTicks));
            if (radius.Raw <= 0 || radius > MaxReach) throw new ArgumentOutOfRangeException(nameof(radius));
            if (cosHalfAnglePermille < 0 || cosHalfAnglePermille > 1000) throw new ArgumentOutOfRangeException(nameof(cosHalfAnglePermille));
            if (halfHeight.Raw <= 0 || halfHeight > MaxReach) throw new ArgumentOutOfRangeException(nameof(halfHeight));
            if (damage.Raw < 0 || damage > MaxDamage) throw new ArgumentOutOfRangeException(nameof(damage));
            if (maxTargets < 1 || maxTargets > MaxTargetsLimit) throw new ArgumentOutOfRangeException(nameof(maxTargets));
            if (engageRange.Raw <= 0 || engageRange > radius) throw new ArgumentOutOfRangeException(nameof(engageRange));
            if (lineHalfWidth.Raw < 0 || lineHalfWidth > MaxReach) throw new ArgumentOutOfRangeException(nameof(lineHalfWidth));
            LineHalfWidth = lineHalfWidth;
            WindupTicks = windupTicks; ActiveTicks = activeTicks; RecoveryTicks = recoveryTicks; Radius = radius;
            CosHalfAnglePermille = cosHalfAnglePermille; HalfHeight = halfHeight; Damage = damage; MaxTargets = maxTargets;
            EngageRange = engageRange;
        }

        public bool IsThrust => LineHalfWidth.Raw > 0;

        public int BusyTicks => WindupTicks + ActiveTicks + RecoveryTicks;

        public void WriteState(StateHash hash)
        {
            hash.Add(WindupTicks); hash.Add(ActiveTicks); hash.Add(RecoveryTicks); hash.Add(Radius.Raw);
            hash.Add(CosHalfAnglePermille); hash.Add(HalfHeight.Raw); hash.Add(Damage.Raw); hash.Add(MaxTargets); hash.Add(EngageRange.Raw);
            // Appended only for a thrust, so a sector spec keeps the bytes and digest it had.
            if (IsThrust) { hash.Add("thrust"); hash.Add(LineHalfWidth.Raw); }
        }
    }

    // Per-unit swing state. The component exists only in worlds whose CombatPack has melee specs, so worlds without melee carry
    // no extra bytes. StartTick is -1 while no swing is pending; origin and direction are cleared when the swing lands or is cancelled.
    public readonly struct MeleeState : IStateHash
    {
        public const string ComponentId = "combat.melee-state";
        public readonly int Spec;
        public readonly long StartTick, NextStartTick;
        public readonly SimVec3 Origin;
        public readonly long DirX, DirZ;

        public MeleeState(int spec, long startTick = -1, long nextStartTick = 0, SimVec3 origin = default, long dirX = 0, long dirZ = 0)
        { Spec = spec; StartTick = startTick; NextStartTick = nextStartTick; Origin = origin; DirX = dirX; DirZ = dirZ; }

        public bool Pending => StartTick >= 0;

        public void WriteState(StateHash hash)
        {
            hash.Add(Spec); hash.Add(StartTick); hash.Add(NextStartTick);
            hash.Add(Origin.X.Raw); hash.Add(Origin.Y.Raw); hash.Add(Origin.Z.Raw); hash.Add(DirX); hash.Add(DirZ);
        }
    }

    public sealed class MeleeStateSerializer : IStableComponentSerializer<MeleeState>
    {
        public int TypeId => 3002;
        public int Version => 1;
        public void Write(SimStateWriter writer, in MeleeState value)
        {
            writer.Int32(value.Spec); writer.Int64(value.StartTick); writer.Int64(value.NextStartTick);
            writer.Int64(value.Origin.X.Raw); writer.Int64(value.Origin.Y.Raw); writer.Int64(value.Origin.Z.Raw);
            writer.Int64(value.DirX); writer.Int64(value.DirZ);
        }
        public MeleeState Read(SimStateReader reader)
        {
            int spec = reader.Int32(); long start = reader.Int64(), next = reader.Int64();
            var origin = new SimVec3(SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()));
            return new MeleeState(spec, start, next, origin, reader.Int64(), reader.Int64());
        }
    }
}
