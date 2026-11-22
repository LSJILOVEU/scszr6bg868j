using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // The squad parts: wrappers around the battle's dispatch and placement. A member asks the inner part as its leader:
    // with the leader's id (so it gets the leader's point and role) and from the place the leader stands at now. Its
    // nerve stays its own, and so does everything the scheduler decides after the parts (engage, pressure, cover, the
    // slot). A leader, a skirmisher and a member whose leader is gone ask as themselves.
    //
    // With tempers or regrouping (SquadTemper, SquadRegroup) the engage part is wrapped as well. It changes one answer
    // only: what a calm leader or member (with its leader alive) does when it sees an enemy. Of a regrouping squad: go
    // on (Advance), to the point the dispatch wrapper names: the point of its team nearest its leader. Otherwise by the
    // squad's mood: rash goes on while the enemy is at least the rash range away, steady stands (Hold), normal asks the
    // inner part. Everybody else gets the inner part's answer (a skirmisher, a pressed unit, a member whose leader fell,
    // and every unit that sees nobody), and the inner part is always asked for the unit itself, not as its leader.
    //
    // The inner parts must keep nothing per unit (the rule for every part, AiParts.Mix): all members of a squad ask with
    // the same id, so a part with a memory could only remember by the leader's key.
    public sealed partial class Squads
    {
        private const ulong TightSalt = 0x2ad7d2bb4c1a9e37UL, OffsetSalt = 0xe07c265404be4294UL, SloppySalt = 0x76f988da831153b5UL;

        public IAiDispatch Wrap(IAiDispatch inner) => new SquadDispatch(this, inner ?? throw new ArgumentNullException(nameof(inner)));
        public IAiPlacement Wrap(IAiPlacement inner) => new SquadPlacement(this, inner ?? throw new ArgumentNullException(nameof(inner)));
        public IAiEngage Wrap(IAiEngage inner) => new SquadEngage(this, inner ?? throw new ArgumentNullException(nameof(inner)));

        // Where a member wants to stand, relative to its leader's anchor. The squad's width s (millimetres) comes from its
        // leader: spreadMin + Permille(leader ^ TightSalt) * (spreadMax - spreadMin) / 1000, a tight squad or a loose one;
        // the member scales it by (500 + Permille(member ^ SloppySalt)) / 1000, a tidy man or a sloppy one. Then, as in
        // the scatter placement, m = Mix(member ^ OffsetSalt), u = (m & 0xFFFFFFFF) % (2s + 1) - s and
        // v = (m >> 32) % (2s + 1) - s: whole millimetres in [-s, +s], x from the low 32 bits and z from the high 32.
        // Nothing is saved: the same member under the same leader gets the same offset on every tick.
        public SimVec3 Offset(StableId member, StableId leader)
        {
            long s = rules.SpreadMinMm + (long)AiParts.Permille(leader.Value, TightSalt) * (rules.SpreadMaxMm - rules.SpreadMinMm) / 1000;
            s = s * (500 + AiParts.Permille(member.Value, SloppySalt)) / 1000;
            ulong m = AiParts.Mix(member.Value ^ OffsetSalt), span = 2UL * (ulong)s + 1UL;
            long u = (long)((m & 0xFFFFFFFFUL) % span) - s, v = (long)((m >> 32) % span) - s;
            return new SimVec3(SimFix.Ratio(u, 1000), default, SimFix.Ratio(v, 1000));
        }

        // The member as its leader: the leader's id, the leader's place now, the member's own team, nerve and layer.
        // Squads are refused on a map with floor plates (ContentPolicies.CheckPlates), so the layer is always the ground's.
        private bool TryAsLeader(in AiUnit unit, out AiUnit asLeader)
        {
            if (!TryLeader(unit.Id, out StableId leader, out SimVec3 leaderHere)) { asLeader = default; return false; }
            asLeader = new AiUnit(leader, unit.Team, leaderHere, unit.Nerve, unit.Layer);
            return true;
        }

        // The squad a unit acts with: the one it leads, or its leader's while the leader is on the field and alive; and
        // where that leader stands now.
        private bool TryBody(in AiUnit unit, out int squad, out SimVec3 leaderHere)
        {
            squad = sizes.IndexOfKey(unit.Id.Value); leaderHere = unit.Here;
            if (squad >= 0) return true;
            if (!TryLeader(unit.Id, out StableId leader, out leaderHere)) return false;
            squad = sizes.IndexOfKey(leader.Value);
            return true;
        }

        // Where a regrouping squad goes: the point its team owns that is nearest its leader (ties to the lower index), as
        // the scheduler's own fallback measures it. The layer is the unit's own: squads are refused on a map with floor
        // plates (ContentPolicies.CheckPlates), so it is always the ground's, the leader's too.
        private bool TryRally(in AiUnit unit, AiField field, out int point)
        {
            point = -1;
            if (!TryBody(in unit, out int squad, out SimVec3 leaderHere) || marks.Values[squad] < 0) return false;
            SimWide best = default;
            for (int i = 0; i < field.PointCount; i++)
            {
                if (field.Owner(i) != unit.Team || !field.TrySpan(leaderHere, unit.Layer, i, out SimWide span)) continue;
                if (point < 0 || span < best) { point = i; best = span; }
            }
            return point >= 0;
        }

        private sealed class SquadDispatch : IAiDispatch
        {
            private readonly Squads squads;
            private readonly IAiDispatch inner;
            internal SquadDispatch(Squads squads, IAiDispatch inner) { this.squads = squads; this.inner = inner; }

            // After the inner part: the tag and the seven squad numbers, so a battle with other squad rules has another digest.
            public ulong Fold(ulong digest)
            {
                SquadRules r = squads.rules;
                var hash = new StateHash(); hash.Add("battle.ai.squads.v1"); hash.Add(inner.Fold(digest));
                hash.Add(r.MinSize); hash.Add(r.MaxSize); hash.Add(r.SkirmisherPermille); hash.Add(r.RecruitRadiusMm);
                hash.Add(r.FormTicks); hash.Add(r.SpreadMinMm); hash.Add(r.SpreadMaxMm);
                if (!squads.regrouping) return hash.Value;
                hash.Add("battle.ai.squad-regroup.v1");
                hash.Add(squads.regroup.BelowPermille); hash.Add(squads.regroup.UntilPermille); hash.Add(squads.regroup.MaxTicks);
                return hash.Value;
            }

            public int Pick(in AiUnit unit, AiField field)
            {
                if (squads.regrouping && squads.TryRally(in unit, field, out int point)) return point;
                return squads.TryAsLeader(in unit, out AiUnit asLeader) ? inner.Pick(in asLeader, field) : inner.Pick(in unit, field);
            }
        }

        private sealed class SquadEngage : IAiEngage
        {
            private readonly Squads squads;
            private readonly IAiEngage inner;
            internal SquadEngage(Squads squads, IAiEngage inner) { this.squads = squads; this.inner = inner; }

            // The tag, the inner part, the four temper numbers (zeros without tempers) and whether squads regroup.
            public ulong Fold(ulong digest)
            {
                SquadTemper t = squads.temper;
                var hash = new StateHash(); hash.Add("battle.ai.squad-engage.v1"); hash.Add(inner.Fold(digest));
                hash.Add(t.RashPermille); hash.Add(t.SteadyPermille); hash.Add(t.ShakenPermille); hash.Add(t.RashMinRangeMm);
                hash.Add(squads.regrouping ? 1 : 0);
                return hash.Value;
            }

            public AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field)
            {
                if (unit.Nerve != AiNerve.Calm || !squads.TryBody(in unit, out int squad, out _)) return inner.Decide(in unit, enemy, field);
                if (squads.regrouping && squads.marks.Values[squad] >= 0) return AiEngage.Advance;
                SquadMood mood = squads.tempered ? squads.Mood(squad) : SquadMood.None;
                if (mood == SquadMood.Steady) return AiEngage.Hold;
                if (mood == SquadMood.Rash && CombatSystem.Horizontal(enemy - unit.Here).LengthSquared >= squads.rashRangeSquared) return AiEngage.Advance;
                return inner.Decide(in unit, enemy, field);
            }

            public AiEngage Unseen(in AiUnit unit, AiField field) => inner.Unseen(in unit, field);
        }

        // Always a waypoint part, so the scheduler asks it; an inner part without waypoints answers no.
        private sealed class SquadPlacement : IAiPlacement, IAiWaypoint
        {
            private readonly Squads squads;
            private readonly IAiPlacement inner;
            private readonly IAiWaypoint waypoints;
            internal SquadPlacement(Squads squads, IAiPlacement inner) { this.squads = squads; this.inner = inner; waypoints = inner as IAiWaypoint; }

            public ulong Fold(ulong digest)
            {
                var hash = new StateHash(); hash.Add("battle.ai.squad-placement.v1"); hash.Add(inner.Fold(digest));
                return hash.Value;
            }

            public SimVec3 Anchor(in AiUnit unit, int point, AiField field) =>
                squads.TryAsLeader(in unit, out AiUnit asLeader)
                    ? inner.Anchor(in asLeader, point, field) + squads.Offset(unit.Id, asLeader.Id)
                    : inner.Anchor(in unit, point, field);

            // The leader's role, the member's own way there: the inner part is asked with the leader's id from where the
            // member stands, because "has it passed the waypoint" is a question about the member.
            public bool TryWaypoint(in AiUnit unit, int point, AiField field, out SimVec3 at)
            {
                at = default;
                if (waypoints == null) return false;
                if (!squads.TryAsLeader(in unit, out AiUnit asLeader)) return waypoints.TryWaypoint(in unit, point, field, out at);
                var onItsWay = new AiUnit(asLeader.Id, unit.Team, unit.Here, unit.Nerve, unit.Layer);
                return waypoints.TryWaypoint(in onItsWay, point, field, out at);
            }
        }
    }
}
