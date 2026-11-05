using System;

namespace WarCore.Sim
{
    internal struct SpatialCell
    {
        internal long X;
        internal long Y;
        internal long Z;
        internal int Level;
        internal int Start;
        internal int Count;
        internal int Cursor;
        internal uint Mask;
    }

    // A batch first counts, then packs each cell into one exact contiguous member block.
    // There is no per-body search, free-list fragmentation or steady-state growth.
    internal sealed class SpatialCellTable
    {
        internal readonly SpatialCell[] Cells;
        internal readonly int[] Members;
        internal readonly int[] Occupied;
        internal readonly SpatialCellRegions Regions;
        internal int Count;
        internal int MemberCount;
        private readonly string cellError;
        private readonly string memberError;

        internal SpatialCellTable(int cells, int members, bool regions)
        {
            Cells = new SpatialCell[cells];
            Members = new int[members];
            Occupied = new int[cells / 2];
            Regions = regions ? new SpatialCellRegions(cells / 2) : null;
            cellError = "Spatial cell capacity " + cells + " exhausted (load limit " + cells / 2 + ").";
            memberError = "Spatial total membership capacity " + members + " exhausted.";
        }

        internal static int Hash(long x, long y, long z, int level)
        {
            unchecked
            {
                ulong h = (ulong)x * 0x9E3779B185EBCA87UL ^ (ulong)y * 0xC2B2AE3D27D4EB4FUL
                    ^ (ulong)z * 0x165667B19E3779F9UL ^ (ulong)level * 0x85EBCA77C2B2AE63UL;
                h ^= h >> 33;
                h *= 0xFF51AFD7ED558CCDUL;
                h ^= h >> 33;
                return SimInteger.Int32Bits(h);
            }
        }

        internal int Find(long x, long y, long z, int level, SpatialQueryDiagnostics diagnostics = null)
            => FindHashed(x, y, z, level, Hash(x, y, z, level), diagnostics);

        internal int FindHashed(long x, long y, long z, int level, int hash, SpatialQueryDiagnostics diagnostics)
        {
            int mask = Cells.Length - 1;
            int slot = hash & mask;
            int probes = 1;
            while (Cells[slot].Count != 0 && (Cells[slot].X != x || Cells[slot].Y != y || Cells[slot].Z != z || Cells[slot].Level != level))
            {
                slot = (slot + 1) & mask;
                probes++;
            }
            if (diagnostics != null)
            {
                diagnostics.DictionaryLookups++;
                diagnostics.HashProbes += probes;
                if (Cells[slot].Count == 0)
                    diagnostics.EmptyLookups++;
            }
            return slot;
        }

        internal void Reset()
        {
            // Only occupied slots need clearing; queries never inspect the unused pool tail.
            for (int i = 0; i < Count; i++)
                Cells[Occupied[i]] = default;
            Count = 0;
            MemberCount = 0;
        }

        internal void CountMember(long x, long y, long z, int level)
        {
            int slot = Find(x, y, z, level);
            ref SpatialCell cell = ref Cells[slot];
            if (cell.Count == 0)
            {
                if (Count == Occupied.Length)
                    throw new SimBoundaryException(cellError);
                cell.X = x;
                cell.Y = y;
                cell.Z = z;
                cell.Level = level;
                Occupied[Count++] = slot;
            }
            if (MemberCount == Members.Length)
                throw new SimBoundaryException(memberError);
            cell.Count++;
            MemberCount++;
        }

        internal void Pack()
        {
            int offset = 0;
            for (int i = 0; i < Count; i++)
            {
                ref SpatialCell cell = ref Cells[Occupied[i]];
                cell.Start = offset;
                offset += cell.Count;
            }
        }

        internal void Fill(long x, long y, long z, int level, int id)
        {
            ref SpatialCell cell = ref Cells[Find(x, y, z, level)];
            Members[cell.Start + cell.Cursor++] = id;
        }

        internal void FlushMasks(SpatialBodyColumns bodies)
        {
            // Repacking dirties every occupied block. Recompute its exact union once after fill.
            // Zero-movement sync skips the entire batch, including this mask pass.
            for (int i = 0; i < Count; i++)
            {
                ref SpatialCell cell = ref Cells[Occupied[i]];
                uint mask = 0;
                for (int j = 0; j < cell.Count; j++)
                    mask |= bodies.Colliders[Members[cell.Start + j]].Layer;
                cell.Mask = mask;
            }
        }

        internal long AllocatedBytes => Cells.LongLength * 48 + Members.LongLength * 4 + Occupied.LongLength * 4 + (Regions == null ? 0 : Regions.AllocatedBytes);
        internal long UsedBytes => (long)Count * 52 + (long)MemberCount * 4 + (Regions == null ? 0 : Regions.UsedBytes);
    }
}
