using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    /// <summary>Hexagonal guard-slot tables, one per control point, built once from point geometry.
    /// Slots are indexed globally: the slots of point p occupy [Start(p), Start(p) + Count(p)).</summary>
    public sealed class GuardSlots
    {
        // Default lattice spacing in raw fixed-point units (1 m). A battle may set another (ai.slotSpacingMm, AiParts.SlotSpacingRaw).
        public const long SpacingRaw = SimFix.Scale;
        private const long RowHeightNumerator = 866025, RowHeightDenominator = 1000000;
        private static readonly int[] RingDq = { 1, 1, 0, -1, -1, 0 };
        private static readonly int[] RingDr = { 0, -1, -1, 0, 1, 1 };

        private readonly SimVec3[] positions;
        private readonly int[] start, count, pointOf, canonical, sorted;
        private readonly SimVec3[] centers;
        private readonly SimWide[] reachSquared;
        // Nearest searches one lattice spacing around a slot (the neighbouring-slot distance).
        private readonly long adjacentSquared;

        // ringRaw widens each point's slot table beyond its capture radius (the table covers radius + ring); 0 keeps the circle.
        public GuardSlots(ReadOnlySpan<ControlPointDef> points, long spacingRaw = SpacingRaw, long ringRaw = 0)
        {
            if (spacingRaw <= 0) throw new ArgumentOutOfRangeException(nameof(spacingRaw));
            if (ringRaw < 0) throw new ArgumentOutOfRangeException(nameof(ringRaw));
            adjacentSquared = spacingRaw * spacingRaw;
            int n = points.Length;
            start = new int[n]; count = new int[n]; centers = new SimVec3[n]; reachSquared = new SimWide[n];
            var all = new List<SimVec3>(); var owner = new List<int>();
            for (int p = 0; p < n; p++)
            {
                SimFix zone = points[p].Radius + SimFix.FromRaw(ringRaw);
                SimVec3[] table = Build(points[p].Center, zone, spacingRaw);
                start[p] = all.Count; count[p] = table.Length; centers[p] = points[p].Center;
                // Every slot lies within radius + ring, so a position within one spacing of a slot lies within radius + ring
                // + spacing of the centre: this skips whole points without changing what Nearest finds.
                SimFix reach = zone + SimFix.FromRaw(spacingRaw);
                reachSquared[p] = new SimVec3(reach, default, default).LengthSquared;
                for (int i = 0; i < table.Length; i++) { all.Add(table[i]); owner.Add(p); }
            }
            positions = all.ToArray(); pointOf = owner.ToArray();
            sorted = new int[positions.Length];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = i;
            Array.Sort(sorted, Compare);
            canonical = new int[positions.Length];
            for (int i = 0; i < sorted.Length;)
            {
                int run = i;
                while (i < sorted.Length && SameCoordinates(positions[sorted[i]], positions[sorted[run]])) { canonical[sorted[i]] = sorted[run]; i++; }
            }
        }

        public int Total => positions.Length;
        public int Start(int point) => start[point];
        public int Count(int point) => count[point];
        public SimVec3 At(int slot) => positions[slot];
        public int PointOf(int slot) => pointOf[slot];
        /// <summary>The slot that carries the occupancy record for this coordinate (lowest point index wins).</summary>
        public int Canonical(int slot) => canonical[slot];

        /// <summary>Lattice slots inside the circle, ring by ring; slot 0 is the center. Neighbours are spacingRaw apart; a row
        /// r steps up holds dz = spacingRaw * r * 866025 / 1000000 (integer division, truncated toward zero).</summary>
        public static SimVec3[] Build(SimVec3 center, SimFix radius, long spacingRaw = SpacingRaw)
        {
            if (spacingRaw <= 0) throw new ArgumentOutOfRangeException(nameof(spacingRaw));
            var list = new List<SimVec3> { center };
            SimWide limit = new SimVec3(radius, default, default).LengthSquared;
            for (int k = 1; ; k++)
            {
                int q = -k, r = k; bool any = false;
                for (int direction = 0; direction < 6; direction++)
                    for (int step = 0; step < k; step++)
                    {
                        long dx = spacingRaw * (2 * q + r) / 2;
                        long dz = spacingRaw * r * RowHeightNumerator / RowHeightDenominator;
                        var offset = new SimVec3(SimFix.FromRaw(dx), default, SimFix.FromRaw(dz));
                        if (offset.LengthSquared <= limit)
                        { list.Add(new SimVec3(center.X + offset.X, center.Y, center.Z + offset.Z)); any = true; }
                        q += RingDq[direction]; r += RingDr[direction];
                    }
                if (!any) break;
            }
            return list.ToArray();
        }

        /// <summary>Find the slot whose coordinates equal the target. With point &gt;= 0 only that point's own table counts,
        /// otherwise the canonical slot is returned.</summary>
        public bool TryFind(SimVec3 target, int point, out int slot)
        {
            int low = 0, high = sorted.Length;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (CompareTo(positions[sorted[mid]], target) < 0) low = mid + 1; else high = mid;
            }
            for (int i = low; i < sorted.Length && SameCoordinates(positions[sorted[i]], target); i++)
                if (point < 0 || pointOf[sorted[i]] == point)
                { slot = point < 0 ? canonical[sorted[i]] : sorted[i]; return true; }
            slot = -1;
            return false;
        }

        /// <summary>The canonical slot nearest to a position and within one lattice spacing horizontally, ties to the lower slot index; -1 when none.</summary>
        public int Nearest(SimVec3 position)
        {
            int best = -1; long bestDistance = 0;
            for (int p = 0; p < start.Length; p++)
            {
                if (CombatSystem.Horizontal(position - centers[p]).LengthSquared > reachSquared[p]) continue;
                for (int s = start[p]; s < start[p] + count[p]; s++)
                {
                    if (canonical[s] != s) continue;
                    long dx = position.X.Raw - positions[s].X.Raw, dz = position.Z.Raw - positions[s].Z.Raw;
                    long distance = dx * dx + dz * dz;
                    if (distance <= adjacentSquared && (best < 0 || distance < bestDistance)) { best = s; bestDistance = distance; }
                }
            }
            return best;
        }

        private int Compare(int a, int b)
        {
            int order = CompareTo(positions[a], positions[b]);
            return order != 0 ? order : a.CompareTo(b);
        }
        private static int CompareTo(SimVec3 a, SimVec3 b) => a.X.Raw != b.X.Raw ? a.X.Raw.CompareTo(b.X.Raw) : a.Z.Raw.CompareTo(b.Z.Raw);
        private static bool SameCoordinates(SimVec3 a, SimVec3 b) => a.X.Raw == b.X.Raw && a.Z.Raw == b.Z.Raw;
    }
}
