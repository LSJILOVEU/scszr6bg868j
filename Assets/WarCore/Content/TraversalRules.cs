using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // TRV-2: the two geometric shapes of a traversal link. The engine knows only shapes and numbers; names such as "climb"
    // or "vault" live in the content pack's kind ids.
    //   Edge: two cells that share an edge, on different layers, with no seam between them.
    //   Over: three cells in an orthogonal line; the middle one is blocked on both end layers by something solid.
    public enum TraversalShape : byte { Edge = 1, Over = 2 }

    // One row of the content pack's traversal.kinds table. Heights are raw (1 m = 65536); intervals are closed.
    public sealed class TraversalKind
    {
        public const int MaxTicksBase = 300, MaxTicksPerMetre = 300;
        public const long MaxPathCost = 1_000_000, MaxNoiseRaw = 100 * SimFix.Scale;

        public string Id { get; }
        public TraversalShape Shape { get; }
        public long RiseMin { get; }
        public long RiseMax { get; }
        public long FallMin { get; }
        public long FallMax { get; }
        public long GapMin { get; }
        public int TicksBase { get; }
        public int TicksPerMetreUp { get; }
        public int TicksPerMetreDown { get; }
        public long PathCost { get; }
        public bool Fire { get; }
        public long NoiseRaw { get; }

        // Throws ArgumentException when a value is outside the bounds the content reader enforces, so a kind built in code
        // obeys the same limits (in particular TicksBase >= 1, which makes every link take at least one tick).
        public TraversalKind(string id, TraversalShape shape, long riseMin, long riseMax, long fallMin, long fallMax, long gapMin,
            int ticksBase, int ticksPerMetreUp, int ticksPerMetreDown, long pathCost, bool fire = false, long noiseRaw = 0)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A traversal kind needs an id.", nameof(id));
            if (shape != TraversalShape.Edge && shape != TraversalShape.Over) throw new ArgumentException("Unknown traversal shape.", nameof(shape));
            Height(riseMin, nameof(riseMin)); Height(riseMax, nameof(riseMax));
            Height(fallMin, nameof(fallMin)); Height(fallMax, nameof(fallMax)); Height(gapMin, nameof(gapMin));
            if (riseMin > riseMax) throw new ArgumentException("riseMin above riseMax.", nameof(riseMin));
            if (fallMin > fallMax) throw new ArgumentException("fallMin above fallMax.", nameof(fallMin));
            if (ticksBase < 1 || ticksBase > MaxTicksBase) throw new ArgumentException("ticksBase out of range.", nameof(ticksBase));
            if (ticksPerMetreUp < 0 || ticksPerMetreUp > MaxTicksPerMetre) throw new ArgumentException("ticksPerMetreUp out of range.", nameof(ticksPerMetreUp));
            if (ticksPerMetreDown < 0 || ticksPerMetreDown > MaxTicksPerMetre) throw new ArgumentException("ticksPerMetreDown out of range.", nameof(ticksPerMetreDown));
            if (pathCost < 0 || pathCost > MaxPathCost) throw new ArgumentException("pathCost out of range.", nameof(pathCost));
            if (noiseRaw < 0 || noiseRaw > MaxNoiseRaw) throw new ArgumentException("noiseRaw out of range.", nameof(noiseRaw));
            Id = id; Shape = shape; RiseMin = riseMin; RiseMax = riseMax; FallMin = fallMin; FallMax = fallMax; GapMin = gapMin;
            TicksBase = ticksBase; TicksPerMetreUp = ticksPerMetreUp; TicksPerMetreDown = ticksPerMetreDown;
            PathCost = pathCost; Fire = fire; NoiseRaw = noiseRaw;
        }

        private static void Height(long value, string name)
        {
            if (value < 0 || value > HeightField.HeightLimitRaw) throw new ArgumentException(name + " out of range.", name);
        }

        // Does a link of this shape with these measures match the kind? Intervals closed; the gap is a lower bound only.
        public bool Matches(TraversalShape shape, long rise, long fall, long gap) =>
            shape == Shape && rise >= RiseMin && rise <= RiseMax && fall >= FallMin && fall <= FallMax && gap >= GapMin;

        // ticksBase + ceil(up * rise / 1 m) + ceil(down * fall / 1 m); rise, fall >= 0 and below 2^30, so each product is
        // below 2^39 and the total (at most 300 + 2 * 2457600) fits an int.
        public int Ticks(long rise, long fall) =>
            (int)(TicksBase + (TicksPerMetreUp * rise + SimFix.Scale - 1) / SimFix.Scale + (TicksPerMetreDown * fall + SimFix.Scale - 1) / SimFix.Scale);
    }

    // TRV-2: the content pack's traversal table (section "traversal", key "kinds"). Order matters: a link takes the first
    // kind that matches. Not part of any digest yet; Fold is what TRV-3 will fold into the navigation digest.
    public sealed class TraversalRules
    {
        public const int MaxKinds = 32;
        public const string FoldTag = "content.traversal.v1";

        private readonly TraversalKind[] kinds;

        public TraversalRules(IReadOnlyList<TraversalKind> kinds)
        {
            if (kinds == null) throw new ArgumentNullException(nameof(kinds));
            if (kinds.Count == 0 || kinds.Count > MaxKinds) throw new ArgumentException("1 to " + MaxKinds + " traversal kinds.", nameof(kinds));
            this.kinds = new TraversalKind[kinds.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < this.kinds.Length; i++)
            {
                TraversalKind kind = kinds[i] ?? throw new ArgumentNullException(nameof(kinds));
                if (!ids.Add(kind.Id)) throw new ArgumentException("Traversal kind '" + kind.Id + "' is duplicated.", nameof(kinds));
                this.kinds[i] = kind;
            }
        }

        public int Count => kinds.Length;
        public TraversalKind this[int index] => kinds[index];

        // The index of the first kind matching the measures, or -1.
        public int FirstMatch(TraversalShape shape, long rise, long fall, long gap)
        {
            for (int i = 0; i < kinds.Length; i++)
                if (kinds[i].Matches(shape, rise, fall, gap)) return i;
            return -1;
        }

        // Folds the whole table, in table order: tag, count, then every field of every kind.
        public void Fold(StateHash hash)
        {
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            hash.Add(FoldTag);
            hash.Add(kinds.Length);
            foreach (TraversalKind k in kinds)
            {
                hash.Add(k.Id); hash.Add((byte)k.Shape);
                hash.Add(k.RiseMin); hash.Add(k.RiseMax); hash.Add(k.FallMin); hash.Add(k.FallMax); hash.Add(k.GapMin);
                hash.Add(k.TicksBase); hash.Add(k.TicksPerMetreUp); hash.Add(k.TicksPerMetreDown);
                hash.Add(k.PathCost); hash.Add(k.Fire); hash.Add(k.NoiseRaw);
            }
        }
    }
}
