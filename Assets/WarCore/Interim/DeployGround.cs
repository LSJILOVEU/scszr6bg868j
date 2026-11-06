using WarCore.Sim;

namespace WarCore.Interim
{
    // Where a deployed unit may stand and how high it stands. A flat world keeps the footprint-corner test and the flat floor;
    // a terrain world uses the exact clearance query of its navigation grid and the height field.
    public sealed class DeployGround
    {
        private static SimFix Radius => SimFix.Ratio(1, 4);
        private readonly NavGrid grid;
        private readonly TerrainTables terrain;
        private readonly long flatRaw;

        public DeployGround(NavGrid grid, TerrainTables terrain, long flatRaw)
        {
            this.grid = grid; this.terrain = terrain; this.flatRaw = flatRaw;
        }

        public ulong Digest => grid.Digest;

        public bool CanStand(SimVec3 at)
        {
            if (terrain != null) return grid.SegmentClear(at, at, InterimLocomotion.UnitFootprint);
            if (!grid.TryCell(at, out int cell) || !grid.IsPassable(cell % grid.Width, cell / grid.Width)) return false;
            // Check the full footprint against blocked cells, including the map boundary.
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    var corner = at + new SimVec3(Radius * SimFix.FromInt(x), default, Radius * SimFix.FromInt(z));
                    if (!grid.TryCell(corner, out int c) || !grid.IsPassable(c % grid.Width, c / grid.Width)) return false;
                }
            return true;
        }

        // The point on the ground below (x, z); false when the height field does not cover it.
        public bool TryGround(SimVec3 at, out SimVec3 grounded)
        {
            SimFix y = SimFix.FromRaw(flatRaw);
            if (terrain?.Ground != null && !terrain.Ground.TryHeightAt(at.X, at.Z, out y)) { grounded = at; return false; }
            grounded = new SimVec3(at.X, y, at.Z);
            return true;
        }
    }
}
