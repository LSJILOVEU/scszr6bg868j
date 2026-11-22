using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // VSPREAD-1 combat.verticalSpread: the vertical tilt of a shot and where aimed and automatic lines end. Everything here
    // reads the pack and the stances as they are now; nothing is stored, so a restored world answers the same. Without the
    // section every method below gives what the paths gave before (no tilt, no extra random number, the old end points).
    public sealed partial class CombatSystem
    {
        // The vertical angle of a shot whose turned angle is shotAngle (ShotAngle's result, 0..90000): shotAngle times the
        // shooter's vertical permille / 1000, capped at VerticalSpread.MaxVerticalMilli; 0 without a vertical spread. Read
        // only: no random number, no state. An angle outside 0..90000 throws, with or without the section.
        public int VerticalAngle(EntityId shooter, int shotAngle)
        {
            if (shotAngle < 0 || shotAngle > SpreadSpec.MaxLegalMilliDegrees)
                throw new ArgumentOutOfRangeException(nameof(shotAngle), "A shot angle is 0 to " + SpreadSpec.MaxLegalMilliDegrees + ".");
            VerticalSpread vertical = pack.VerticalSpread;
            return vertical == null ? 0 : VerticalSpread.Angle(shotAngle, VerticalPermille(shooter, vertical));
        }

        // The shooter's level value; while changing stance the wider of the two levels; the standing value without stances.
        private int VerticalPermille(EntityId shooter, VerticalSpread vertical)
        {
            if (stances == null) return vertical.Permille;
            StanceView view = stances.StanceOf(shooter);
            int now = vertical.PermilleOf(view.Level);
            return view.IsChanging ? Math.Max(now, vertical.PermilleOf(view.Target)) : now;
        }

        // After the horizontal turn: a second number from combat.spread tilts the shot when its vertical angle is above 0.
        private SimVec3 TiltShot(EntityId shooter, int shot, SimVec3 direction)
        {
            int angle = VerticalSpread.Angle(shot, VerticalPermille(shooter, pack.VerticalSpread));
            return angle > 0 ? VerticalSpread.Tilt(direction, angle, sim.Rng.NextInt(spreadStream, 65536)) : direction;
        }

        private bool AimsAtCenter => pack.VerticalSpread != null && pack.VerticalSpread.Aim == VerticalAim.Center;

        // Where the built-in line from shooter at target ends. Aim "center": the target's foot lifted by half the height of its
        // stance now (the pack's body without stances), unless a wall, the ground or a plate cuts the line from the shooter's
        // muzzle to it; then, and without aim "center", ShotEnd(target) (the eye) as before.
        public SimVec3 ShotEnd(EntityId shooter, EntityId target)
        {
            if (!AimsAtCenter) return ShotEnd(target);
            SimVec3 middle = Lift(Point(target), stances != null ? stances.HeightRaw(target) / 2 : centerRaw);
            return LineClear(MuzzlePoint(shooter), middle) ? middle : ShotEnd(target);
        }

        // Where an aimed line at a ground point ends: with aim "center" and the controlled player shooting, the nearest living
        // enemy whose foot is within the snap distance of the point (ties to the smaller StableId) is aimed at as
        // ShotEnd(shooter, enemy); otherwise the point lifted to the standing eye, as before.
        public SimVec3 AimedEnd(EntityId shooter, SimVec3 point)
        {
            if (AimsAtCenter && IsPlayer(shooter) && TrySnap(shooter, point, out EntityId enemy)) return ShotEnd(shooter, enemy);
            return Lift(point, aimEndRaw);
        }

        // A scan of the published units (no spatial query, so QueriesThisStep does not count it; no allocation). SpanSquared is
        // the horizontal distance, 3D on a map with plates.
        private bool TrySnap(EntityId shooter, SimVec3 point, out EntityId enemy)
        {
            enemy = default;
            SimWide limit = new SimVec3(SimFix.FromRaw(pack.VerticalSpread.SnapRaw), default, default).LengthSquared, best = default;
            StableId bestId = default;
            bool found = false;
            foreach (EntityId unit in stage.Capture())
            {
                if (!IsEnemy(shooter, unit) || !Entities.TryGet(positions, unit, out Position position)) continue;
                SimWide span = SpanSquared(point, position.Value);
                if (span > limit) continue;
                StableId id = Entities.StableOf(unit);
                if (found && (span > best || (span == best && id.CompareTo(bestId) >= 0))) continue;
                enemy = unit; best = span; bestId = id; found = true;
            }
            return found;
        }
    }
}
