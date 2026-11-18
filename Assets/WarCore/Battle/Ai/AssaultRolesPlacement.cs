using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Assault roles ("assault-roles"): the attackers of a point split by unit key into a frontal arc, a left and a right
    // flank and a rear group. Flankers and the rear group walk to a waypoint first and then take the edge slots on
    // their side.
    //
    // Axis: from the team's own point nearest to the target (equal distances to the lower point index) to the target's
    // centre; without an own point from the team's first spawn anchor. A point the team owns (its own nearest point, so
    // a zero axis), a team with neither, or an anchor on the centre gets the default: anchor = the unit itself, no
    // waypoint. Left is to the left of someone looking along the axis: left = (-axis.z, 0, axis.x); right is its negative.
    // All lengths are whole millimetres turned into raw units by mm * Scale / 1000 (toward zero); the axis is the
    // horizontal origin-to-centre vector normalized with SimVec3.Normalized (each component rounded toward zero), and an
    // offset is axis * length with SimVec3 * SimFix (each component toward zero).
    public sealed class AssaultRolesPlacement : IAiPlacement, IAiWaypoint
    {
        private enum Role { Front, Left, Right, Rear }

        // Mix(key ^ RoleSalt): % 1000 picks the role (front, flank, rear in that order); bit 20 splits the flank share
        // into left (0) and right (1); (>> 32) % (frontArcMm + 1) - frontArcMm / 2 is the frontal offset in mm.
        private const ulong RoleSalt = 0x7f4a7c159e3779b9UL;
        private readonly int frontPermille, flankPermille, rearPermille, frontArcMm, flankSideMm, rearOutMm, flankMinMm, anchorMm, arriveMm;
        private readonly SimFix flankSide, rearOut, anchor;
        private readonly SimWide flankMinSquared, arriveSquared;

        // The three shares add up to 1000 (the content loader checks it). frontArcMm is the whole width of the frontal
        // arc; flankMinMm is measured from the axis origin to the centre, so the choice holds while the unit walks.
        public AssaultRolesPlacement(int frontPermille, int flankPermille, int rearPermille, int frontArcMm, int flankSideMm,
            int rearOutMm, int flankMinMm, int anchorMm, int arriveMm)
        {
            this.frontPermille = frontPermille; this.flankPermille = flankPermille; this.rearPermille = rearPermille;
            this.frontArcMm = frontArcMm; this.flankSideMm = flankSideMm; this.rearOutMm = rearOutMm;
            this.flankMinMm = flankMinMm; this.anchorMm = anchorMm; this.arriveMm = arriveMm;
            flankSide = Mm(flankSideMm); rearOut = Mm(rearOutMm); anchor = Mm(anchorMm);
            flankMinSquared = new SimVec3(Mm(flankMinMm), default, default).LengthSquared;
            arriveSquared = new SimVec3(Mm(arriveMm), default, default).LengthSquared;
        }

        internal static SimFix Mm(long mm) => SimFix.FromRaw(mm * SimFix.Scale / 1000);

        public ulong Fold(ulong digest)
        {
            var hash = new StateHash(); hash.Add("battle.ai.assault-roles.v1"); hash.Add(digest);
            hash.Add(frontPermille); hash.Add(flankPermille); hash.Add(rearPermille); hash.Add(frontArcMm); hash.Add(flankSideMm);
            hash.Add(rearOutMm); hash.Add(flankMinMm); hash.Add(anchorMm); hash.Add(arriveMm);
            return hash.Value;
        }

        public SimVec3 Anchor(in AiUnit unit, int point, AiField field)
        {
            if (!TryAxis(unit.Team, point, field, out SimVec3 axis, out _)) return unit.Here;
            SimVec3 center = field.Center(point), left = Left(axis);
            ulong mix = AiParts.Mix(unit.Id.Value ^ RoleSalt);
            switch (RoleOf(mix))
            {
                case Role.Left: return center + left * anchor;
                case Role.Right: return center - left * anchor;
                case Role.Rear: return center + axis * anchor;
                default:
                    long offset = (long)((mix >> 32) % (ulong)(frontArcMm + 1)) - frontArcMm / 2;
                    return center - axis * anchor + left * Mm(offset);
            }
        }

        public bool TryWaypoint(in AiUnit unit, int point, AiField field, out SimVec3 at)
        {
            at = default;
            if (!TryAxis(unit.Team, point, field, out SimVec3 axis, out SimWide originSquared) || originSquared < flankMinSquared) return false;
            SimVec3 center = field.Center(point);
            switch (RoleOf(AiParts.Mix(unit.Id.Value ^ RoleSalt)))
            {
                case Role.Left: at = center + Left(axis) * flankSide; break;
                case Role.Right: at = center - Left(axis) * flankSide; break;
                case Role.Rear: at = center + axis * rearOut; break;
                default: return false;
            }
            return CombatSystem.Horizontal(unit.Here - at).LengthSquared > arriveSquared;
        }

        private Role RoleOf(ulong mix)
        {
            int roll = (int)(mix % 1000UL);
            if (roll < frontPermille) return Role.Front;
            if (roll < frontPermille + flankPermille) return ((mix >> 20) & 1UL) == 0 ? Role.Left : Role.Right;
            return Role.Rear;
        }

        private static SimVec3 Left(SimVec3 axis) => new SimVec3(-axis.Z, default, axis.X);

        private static bool TryAxis(int team, int point, AiField field, out SimVec3 axis, out SimWide originSquared)
        {
            axis = default; originSquared = default;
            SimVec3 center = field.Center(point), origin = default;
            int own = -1;
            // A target the team owns is its own nearest point: the axis is zero and the default applies.
            for (int i = 0; i < field.PointCount; i++)
            {
                if (field.Owner(i) != team) continue;
                SimWide distance = CombatSystem.Horizontal(center - field.Center(i)).LengthSquared;
                if (own < 0 || distance < originSquared) { own = i; originSquared = distance; origin = field.Center(i); }
            }
            if (own < 0)
            {
                if (!field.TryBase(team, out origin)) return false;
                originSquared = CombatSystem.Horizontal(center - origin).LengthSquared;
            }
            SimVec3 toward = CombatSystem.Horizontal(center - origin);
            axis = toward.Normalized;
            return axis.X.Raw != 0 || axis.Z.Raw != 0;
        }
    }
}
