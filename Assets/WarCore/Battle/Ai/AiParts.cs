using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // The unit that is thinking, as the scheduler (SimpleAiSystem) hands it to a part.
    public readonly struct AiUnit
    {
        public readonly StableId Id;
        public readonly int Team;
        public readonly SimVec3 Here;
        // How hard the unit is pressed (Pressure); Calm when the battle runs without that system.
        public readonly AiNerve Nerve;
        // The layer the unit stands on (0 = the ground, 1..N = a floor plate); 0 on a map without plates.
        public readonly int Layer;
        public AiUnit(StableId id, int team, SimVec3 here, AiNerve nerve = AiNerve.Calm, int layer = 0)
        { Id = id; Team = team; Here = here; Nerve = nerve; Layer = layer; }
    }

    // What the pressure system (Pressure) says about one unit: Pinned at or above the pinned line; Broken from the
    // broken line until the pressure has fallen back to the recover line.
    public enum AiNerve { Calm, Pinned, Broken }

    // Read-only battlefield facts for AI parts. The scheduler owns it and calls Begin once per Step; nothing a part
    // reads here changes during a Step.
    public sealed class AiField
    {
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        // Front line per team, found on first use in a Step. Sized by the teams that have a spawn anchor.
        private readonly int[] frontline;
        private readonly bool[] known;
        // The walking costs to the points on a map with floor plates; null without plates.
        private readonly IPointReach reach;
        internal AiField(MatchPack pack, CaptureSystem capture, IPointReach reach = null)
        {
            this.pack = pack; this.capture = capture; this.reach = reach;
            int teams = 0;
            foreach (SpawnAnchor anchor in pack.Bases) teams = Math.Max(teams, anchor.Team + 1);
            frontline = new int[teams]; known = new bool[teams];
        }
        internal void Begin(long tick) { Tick = tick; Array.Clear(known, 0, known.Length); }

        public long Tick { get; private set; }
        public int PointCount => pack.Points.Length;
        public SimVec3 Center(int point) => pack.Points[point].Center;
        public int Owner(int point) => capture.Points[point].Owner;
        // The layer of the point's centre (0 = the ground).
        public int Layer(int point) => pack.Points[point].Layer;
        // The team's first spawn anchor.
        public bool TryBase(int team, out SimVec3 at) => TryBase(team, out at, out _);
        public bool TryBase(int team, out SimVec3 at, out int layer)
        {
            foreach (SpawnAnchor anchor in pack.Bases)
                if (anchor.Team == team) { at = anchor.Point; layer = anchor.Layer; return true; }
            at = default; layer = 0; return false;
        }
        // How far a point is from a place, to compare points by (smaller = nearer; nothing else may be read from it). Without
        // floor plates: the squared horizontal distance to the centre, always true. With plates: the square of the walking cost
        // from the place (a cell of that layer) to the centre; false when the point cannot be walked to from there.
        public bool TrySpan(SimVec3 from, int fromLayer, int point, out SimWide span)
        {
            if (reach == null) { span = CombatSystem.Horizontal(from - pack.Points[point].Center).LengthSquared; return true; }
            bool found = reach.TryCost(point, from, fromLayer, out long cost);
            span = found ? new SimVec3(SimFix.FromRaw(cost), default, default).LengthSquared : default;
            return found;
        }
        // True when the team's base (TryBase) can be walked to from a place; always true on a map without floor plates.
        public bool ReachesBase(int team, SimVec3 from, int fromLayer) => reach == null || reach.TryBaseCost(team, from, fromLayer, out _);
        // The team's owned point closest to any point it does not own (ties to the lower point index); -1 when it owns none or all.
        public int Frontline(int team)
        {
            if ((uint)team >= (uint)frontline.Length) return FindFrontline(team);
            if (!known[team]) { frontline[team] = FindFrontline(team); known[team] = true; }
            return frontline[team];
        }
        private int FindFrontline(int team)
        {
            int front = -1; SimWide best = default;
            for (int i = 0; i < pack.Points.Length; i++)
            {
                if (capture.Points[i].Owner != team) continue;
                for (int j = 0; j < pack.Points.Length; j++)
                {
                    if (capture.Points[j].Owner == team) continue;
                    if (!TrySpan(pack.Points[i].Center, pack.Points[i].Layer, j, out SimWide distance)) continue;
                    if (front < 0 || distance < best) { front = i; best = distance; }
                }
            }
            return front;
        }
    }

    // Dispatch: which control point a unit goes for. The answer may depend only on the unit, the field and the part's
    // own parameters, so the same unit gets the same answer on every tick and after a restore.
    public interface IAiDispatch
    {
        // Folds the part's tag and parameters into the battle policy digest. A part whose parameters leave the battle
        // as it was without the part returns the digest unchanged.
        ulong Fold(ulong digest);
        // Index of the point to go for; -1 when there is none (the unit stops).
        int Pick(in AiUnit unit, AiField field);
    }

    // Placement: where at the point a unit wants to stand. The scheduler takes the available, standable guard slot
    // nearest to the anchor (ties to the lower slot number). The anchor must not change while the facts do not, or
    // the unit keeps swapping slots.
    public interface IAiPlacement
    {
        ulong Fold(ulong digest);
        SimVec3 Anchor(in AiUnit unit, int point, AiField field);
    }

    // Optional second face of a placement: a point to walk to before the unit goes for its slot. The scheduler asks it
    // once per think, after the dispatch, and walks the unit there (no slot reserved) only while the unit is farther
    // from the point's centre than the waypoint is, the waypoint is standable and is not a slot coordinate; otherwise
    // the unit takes its slot as usual. Stateless like the anchor: the part answers from the unit, the field and its
    // own parameters, and returns false once the unit is close enough to the waypoint.
    public interface IAiWaypoint
    {
        bool TryWaypoint(in AiUnit unit, int point, AiField field, out SimVec3 at);
    }

    // What the engage part answers at a think. Hold: stand where it is (it still fires). Advance: go on as a unit that
    // is left alone would. Fallback: go to the team's nearest owned point and stand there (to the team's base when it
    // owns none, stand when it has neither). Cover: walk to a spot nearby with a wall close in front of it, toward where the
    // fire that pressed the unit came from (Cover, Pressure.TryPressedFrom); the same as Hold when the battle has no
    // cover rules, or the unit stands in cover already or finds no spot.
    public enum AiEngage { Hold, Advance, Fallback, Cover }

    // Engage: what a unit that sees an enemy, or is pressed, does. The answers may depend only on the unit, the enemy's
    // point, the field and the part's own parameters (no state), like the other parts.
    public interface IAiEngage
    {
        ulong Fold(ulong digest);
        // An enemy is in sight, at this point. Advance goes on to the unit's point (dispatch and placement), skipping
        // suppression and search.
        AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field);
        // No enemy is in sight. Asked only for a unit that is not Calm. Advance leaves the think as it was before parts
        // (suppression, search, then the point); Hold keeps suppression and skips the rest. Cover skips suppression
        // while the unit walks.
        AiEngage Unseen(in AiUnit unit, AiField field);
    }

    // The parts one battle's AI runs with. Missing parts are the defaults, which leave the battle as it was before
    // parts existed.
    public sealed class AiParts
    {
        public IAiDispatch Dispatch { get; }
        public IAiPlacement Placement { get; }
        // Guard-slot lattice spacing in raw units, a property of the scheduler rather than of a part: every placement picks
        // from the same lattice. The default (GuardSlots.SpacingRaw, 1 m) leaves the battle as it was.
        public long SlotSpacingRaw { get; }
        // Width in raw units of the ring of guard slots outside each capture circle (also a scheduler property); 0 = none.
        public long SlotRingRaw { get; }
        public IAiEngage Engage { get; }
        // Cover (a scheduler property, used when the engage part answers Cover), in raw units: how far a unit looks for a
        // spot behind a wall, the spacing of the spots it tries, and how close in front of the spot the wall must stand.
        // All 0 = no cover (the answer reads as Hold).
        public long CoverRadiusRaw { get; }
        public long CoverStepRaw { get; }
        public long CoverDepthRaw { get; }
        public AiParts(IAiDispatch dispatch = null, IAiPlacement placement = null, long slotSpacingRaw = GuardSlots.SpacingRaw, long slotRingRaw = 0,
            IAiEngage engage = null, long coverRadiusRaw = 0, long coverStepRaw = 0, long coverDepthRaw = 0)
        {
            if (slotSpacingRaw <= 0) throw new ArgumentOutOfRangeException(nameof(slotSpacingRaw));
            if (slotRingRaw < 0) throw new ArgumentOutOfRangeException(nameof(slotRingRaw));
            if (coverRadiusRaw < 0) throw new ArgumentOutOfRangeException(nameof(coverRadiusRaw));
            if (coverRadiusRaw > 0 ? coverStepRaw <= 0 : coverStepRaw != 0) throw new ArgumentOutOfRangeException(nameof(coverStepRaw));
            if (coverRadiusRaw > 0 ? coverDepthRaw <= 0 : coverDepthRaw != 0) throw new ArgumentOutOfRangeException(nameof(coverDepthRaw));
            Dispatch = dispatch ?? new NearestDispatch(); Placement = placement ?? new NearestSlotPlacement();
            SlotSpacingRaw = slotSpacingRaw; SlotRingRaw = slotRingRaw; Engage = engage ?? new HoldEngage();
            CoverRadiusRaw = coverRadiusRaw; CoverStepRaw = coverStepRaw; CoverDepthRaw = coverDepthRaw;
        }

        // The only source of variation a part may use: Mix(unit key ^ a constant of the part). Stateless, so the same
        // unit decides the same way on every tick and after a restore.
        public static ulong Mix(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                return x ^ (x >> 31);
            }
        }

        // A trait of one unit in thousandths (0..999): Mix(unit key ^ salt) % 1000, one salt per trait. The same unit
        // always gets the same value; nothing is saved.
        public static int Permille(ulong key, ulong salt) => (int)(Mix(key ^ salt) % 1000UL);
    }
}
