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
        public AiUnit(StableId id, int team, SimVec3 here) { Id = id; Team = team; Here = here; }
    }

    // Read-only battlefield facts for AI parts. The scheduler owns it and calls Begin once per Step; nothing a part
    // reads here changes during a Step.
    public sealed class AiField
    {
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        // Front line per team, found on first use in a Step. Sized by the teams that have a spawn anchor.
        private readonly int[] frontline;
        private readonly bool[] known;
        internal AiField(MatchPack pack, CaptureSystem capture)
        {
            this.pack = pack; this.capture = capture;
            int teams = 0;
            foreach (SpawnAnchor anchor in pack.Bases) teams = Math.Max(teams, anchor.Team + 1);
            frontline = new int[teams]; known = new bool[teams];
        }
        internal void Begin(long tick) { Tick = tick; Array.Clear(known, 0, known.Length); }

        public long Tick { get; private set; }
        public int PointCount => pack.Points.Length;
        public SimVec3 Center(int point) => pack.Points[point].Center;
        public int Owner(int point) => capture.Points[point].Owner;
        // The team's first spawn anchor.
        public bool TryBase(int team, out SimVec3 at)
        {
            foreach (SpawnAnchor anchor in pack.Bases)
                if (anchor.Team == team) { at = anchor.Point; return true; }
            at = default; return false;
        }
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
                    SimWide distance = CombatSystem.Horizontal(pack.Points[i].Center - pack.Points[j].Center).LengthSquared;
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

    // The parts one battle's AI runs with. Missing parts are the defaults, which leave the battle as it was before
    // parts existed.
    public sealed class AiParts
    {
        public IAiDispatch Dispatch { get; }
        public IAiPlacement Placement { get; }
        public AiParts(IAiDispatch dispatch = null, IAiPlacement placement = null)
        { Dispatch = dispatch ?? new NearestDispatch(); Placement = placement ?? new NearestSlotPlacement(); }

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
    }
}
