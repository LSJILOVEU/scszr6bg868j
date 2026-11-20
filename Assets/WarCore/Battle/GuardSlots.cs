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

        // Slots of points whose centre is on the ground: every read port with its old signature sees only these, with the same
        // indices as on a map without plates. Slots of points on a plate (ML-9b) live in their own table, read through the
        // Plate* ports (for ML-11's AI); the AI of today sees such a point as one without slots.
        private readonly Table ground, plates;
        // Nearest searches one lattice spacing around a slot (the neighbouring-slot distance).
        private readonly long adjacentSquared;

        // ringRaw widens each point's slot table beyond its capture radius (the table covers radius + ring); 0 keeps the circle.
        // floor: the plates' walkable tops, needed when some point's centre is on a plate (ML-9b); its slots keep only the lattice
        // cells where floor answers, at that height.
        public GuardSlots(ReadOnlySpan<ControlPointDef> points, long spacingRaw = SpacingRaw, long ringRaw = 0, ISlotFloor floor = null)
        {
            if (spacingRaw <= 0) throw new ArgumentOutOfRangeException(nameof(spacingRaw));
            if (ringRaw < 0) throw new ArgumentOutOfRangeException(nameof(ringRaw));
            adjacentSquared = spacingRaw * spacingRaw;
            int n = points.Length;
            ground = new Table(n); plates = new Table(n);
            for (int p = 0; p < n; p++)
            {
                SimFix zone = points[p].Radius + SimFix.FromRaw(ringRaw);
                SimVec3[] table = Build(points[p].Center, zone, spacingRaw);
                // Every slot lies within radius + ring, so a position within one spacing of a slot lies within radius + ring
                // + spacing of the centre: this skips whole points without changing what Nearest finds.
                SimFix reach = zone + SimFix.FromRaw(spacingRaw);
                SimWide reachSquared = new SimVec3(reach, default, default).LengthSquared;
                int layer = points[p].Layer;
                if (layer == 0)
                {
                    ground.Add(p, 0, table, points[p].Center, reachSquared);
                    plates.Add(p, 0, Array.Empty<SimVec3>(), points[p].Center, reachSquared);
                    continue;
                }
                if (floor == null) throw new ArgumentException("Guard slots of a point on a floor plate need the plates' floor.", nameof(floor));
                var kept = new List<SimVec3>();
                foreach (SimVec3 slot in table)
                    if (floor.TryFloor(layer, slot.X, slot.Z, out SimFix y)) kept.Add(new SimVec3(slot.X, y, slot.Z));
                ground.Add(p, 0, Array.Empty<SimVec3>(), points[p].Center, reachSquared);
                plates.Add(p, layer, kept.ToArray(), points[p].Center, reachSquared);
            }
            ground.Seal(); plates.Seal();
        }

        public int Total => ground.Positions.Length;
        public int Start(int point) => ground.Start[point];
        public int Count(int point) => ground.Count[point];
        public SimVec3 At(int slot) => ground.Positions[slot];
        public int PointOf(int slot) => ground.PointOf[slot];
        /// <summary>The slot that carries the occupancy record for this coordinate (lowest point index wins).</summary>
        public int Canonical(int slot) => ground.Canonical[slot];

        // ML-9b: the slots of points on a plate, in their own index space (Y = the plate's top, a layer each); same meanings as
        // the ground ports; two slots share a canonical slot only on the same layer.
        public int PlateTotal => plates.Positions.Length;
        public int PlateStart(int point) => plates.Start[point];
        public int PlateCount(int point) => plates.Count[point];
        public SimVec3 PlateAt(int slot) => plates.Positions[slot];
        public int PlateLayerOf(int slot) => plates.Layers[slot];
        public int PlatePointOf(int slot) => plates.PointOf[slot];
        public int PlateCanonical(int slot) => plates.Canonical[slot];

        // ML-11a: both tables in one index space, for the AI: [0, Total) are the ground slots under their own indices, then the
        // plate slots at Total + their plate index. A point's slots are in one table only (the one of its centre's layer), so
        // each point has one run. Without a point on a plate these ports equal the ground ports.
        public int AllTotal => ground.Positions.Length + plates.Positions.Length;
        public int AllStart(int point) => plates.Count[point] != 0 ? Total + plates.Start[point] : ground.Start[point];
        public int AllCount(int point) => plates.Count[point] != 0 ? plates.Count[point] : ground.Count[point];
        public SimVec3 AllAt(int slot) => slot < Total ? ground.Positions[slot] : plates.Positions[slot - Total];
        public int AllLayerOf(int slot) => slot < Total ? 0 : plates.Layers[slot - Total];
        public int AllCanonical(int slot) => slot < Total ? ground.Canonical[slot] : Total + plates.Canonical[slot - Total];
        // TryFind and Nearest on the layer's table: layer 0 is the ground table, any other layer the plate table.
        public bool TryFindAll(int layer, SimVec3 target, int point, out int slot)
        {
            if (layer == 0) return ground.TryFind(0, target, point, out slot);
            bool found = plates.TryFind(layer, target, point, out slot);
            if (found) slot += Total;
            return found;
        }
        public int NearestAll(int layer, SimVec3 position)
        {
            if (layer == 0) return ground.Nearest(0, position, adjacentSquared);
            int slot = plates.Nearest(layer, position, adjacentSquared);
            return slot < 0 ? -1 : slot + Total;
        }

        /// <summary>Lattice slots inside the circle, ring by ring; slot 0 is the center (for a point on the ground; a point on a
        /// plate keeps only the slots on walkable cells, so it may have no slot at its centre). Neighbours are spacingRaw apart; a row
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
        public bool TryFind(SimVec3 target, int point, out int slot) => ground.TryFind(0, target, point, out slot);

        /// <summary>The canonical slot nearest to a position and within one lattice spacing horizontally, ties to the lower slot index; -1 when none.</summary>
        public int Nearest(SimVec3 position) => ground.Nearest(0, position, adjacentSquared);

        // ML-9b: the same on the plate slots of one layer.
        public bool TryFindOnPlate(int layer, SimVec3 target, int point, out int slot) => plates.TryFind(layer, target, point, out slot);
        public int NearestOnPlate(int layer, SimVec3 position) => plates.Nearest(layer, position, adjacentSquared);

        // One index space of slots: per point a run [Start, Start + Count); sorted by (layer, X, Z, index); same layer and X, Z
        // share the canonical slot of the lowest index.
        private sealed class Table
        {
            internal SimVec3[] Positions;
            internal int[] Layers, PointOf, Canonical;
            internal readonly int[] Start, Count;
            private readonly SimVec3[] centers;
            private readonly SimWide[] reachSquared;
            private int[] sorted;
            private readonly List<SimVec3> all = new List<SimVec3>();
            private readonly List<int> layers = new List<int>(), owner = new List<int>();

            internal Table(int points)
            {
                Start = new int[points]; Count = new int[points]; centers = new SimVec3[points]; reachSquared = new SimWide[points];
            }

            internal void Add(int point, int layer, SimVec3[] slots, SimVec3 center, SimWide reach)
            {
                Start[point] = all.Count; Count[point] = slots.Length; centers[point] = center; reachSquared[point] = reach;
                for (int i = 0; i < slots.Length; i++) { all.Add(slots[i]); layers.Add(layer); owner.Add(point); }
            }

            internal void Seal()
            {
                Positions = all.ToArray(); Layers = layers.ToArray(); PointOf = owner.ToArray();
                sorted = new int[Positions.Length];
                for (int i = 0; i < sorted.Length; i++) sorted[i] = i;
                Array.Sort(sorted, Compare);
                Canonical = new int[Positions.Length];
                for (int i = 0; i < sorted.Length;)
                {
                    int run = i;
                    while (i < sorted.Length && Same(sorted[i], Layers[sorted[run]], Positions[sorted[run]])) { Canonical[sorted[i]] = sorted[run]; i++; }
                }
            }

            internal bool TryFind(int layer, SimVec3 target, int point, out int slot)
            {
                int low = 0, high = sorted.Length;
                while (low < high)
                {
                    int mid = (low + high) >> 1;
                    if (CompareTo(Layers[sorted[mid]], Positions[sorted[mid]], layer, target) < 0) low = mid + 1; else high = mid;
                }
                for (int i = low; i < sorted.Length && Same(sorted[i], layer, target); i++)
                    if (point < 0 || PointOf[sorted[i]] == point)
                    { slot = point < 0 ? Canonical[sorted[i]] : sorted[i]; return true; }
                slot = -1;
                return false;
            }

            internal int Nearest(int layer, SimVec3 position, long adjacentSquared)
            {
                int best = -1; long bestDistance = 0;
                for (int p = 0; p < Start.Length; p++)
                {
                    if (CombatSystem.Horizontal(position - centers[p]).LengthSquared > reachSquared[p]) continue;
                    for (int s = Start[p]; s < Start[p] + Count[p]; s++)
                    {
                        if (Canonical[s] != s || Layers[s] != layer) continue;
                        long dx = position.X.Raw - Positions[s].X.Raw, dz = position.Z.Raw - Positions[s].Z.Raw;
                        long distance = dx * dx + dz * dz;
                        if (distance <= adjacentSquared && (best < 0 || distance < bestDistance)) { best = s; bestDistance = distance; }
                    }
                }
                return best;
            }

            private int Compare(int a, int b)
            {
                int order = CompareTo(Layers[a], Positions[a], Layers[b], Positions[b]);
                return order != 0 ? order : a.CompareTo(b);
            }
            private static int CompareTo(int layerA, SimVec3 a, int layerB, SimVec3 b) =>
                layerA != layerB ? layerA.CompareTo(layerB) : a.X.Raw != b.X.Raw ? a.X.Raw.CompareTo(b.X.Raw) : a.Z.Raw.CompareTo(b.Z.Raw);
            private bool Same(int slot, int layer, SimVec3 b) =>
                Layers[slot] == layer && Positions[slot].X.Raw == b.X.Raw && Positions[slot].Z.Raw == b.Z.Raw;
        }
    }
}
