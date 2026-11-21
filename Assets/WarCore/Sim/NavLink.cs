using System;

namespace WarCore.Sim
{
    // TRV-3: one directed link of a LayeredNav (rule K16): a way from one walkable node to another that is not a walking
    // step or a seam (climbing onto a box, a vault over a low wall). Only what the run time needs is kept here: Sim does not
    // reference Content, where the links are measured.
    //
    // Span is the number of cells the link covers in the grid (1 or 2); entering To over the link costs
    // 1000 x Span x To's cell cost + PathCost. Kind is the index into the content table the link came from; Ticks is how long
    // the crossing takes; Cross is the height (raw) the body passes; Gap is the clear height above Cross (NoGap when nothing
    // is above).
    public readonly struct NavLink : IEquatable<NavLink>
    {
        public const int MaxSpan = 2;
        public const long MaxPathCost = 1_000_000;
        public const long NoGap = long.MaxValue;

        public readonly int From, To, Span, Kind, Ticks;
        public readonly long PathCost, Cross, Gap;

        public NavLink(int from, int to, int span, long pathCost, int kind, int ticks, long cross, long gap)
        {
            From = from; To = to; Span = span; PathCost = pathCost; Kind = kind; Ticks = ticks; Cross = cross; Gap = gap;
        }

        public bool Equals(NavLink o) => From == o.From && To == o.To && Span == o.Span && PathCost == o.PathCost && Kind == o.Kind
            && Ticks == o.Ticks && Cross == o.Cross && Gap == o.Gap;
        public override bool Equals(object obj) => obj is NavLink o && Equals(o);
        public override int GetHashCode() => From * 397 ^ To;
        public override string ToString() => From + "->" + To + " span " + Span + " cost " + PathCost + " kind " + Kind + " ticks " + Ticks
            + " cross " + Cross + " gap " + (Gap == NoGap ? "none" : Gap.ToString());
    }
}
