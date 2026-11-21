using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // ML-9b: what the match systems read of the plates on a map with floor plates: a unit's layer (its vertical state; a unit
    // without one is on the ground) and a layer's walkable top for guard slots. Null on a map without plates.
    internal sealed class PlateLayers : IUnitLayers, ISlotFloor
    {
        private readonly Simulation sim;
        private readonly ComponentType<VerticalState> verticals;
        private readonly PlateWorld plates;

        private PlateLayers(Simulation sim, ComponentType<VerticalState> verticals, PlateWorld plates)
        {
            this.sim = sim; this.verticals = verticals; this.plates = plates;
        }

        internal static PlateLayers Of(WorldAssembly a) =>
            a.Plates != null ? new PlateLayers(a.Sim, a.Verticals, a.Plates) : null;

        public int LayerOf(EntityId unit) => sim.Entities.TryGet(verticals, unit, out VerticalState state) ? state.Layer : 0;

        public bool TryFloor(int layer, SimFix x, SimFix z, out SimFix y) => plates.TryFloor(layer, x, z, true, out y);
    }

    // ML-11a: the walking cost from any walkable cell of any layer to each control point and to each team's base, one table per
    // target, found when the world is built (the map never changes). Null on a map without plates.
    internal sealed class PointReach : IPointReach
    {
        private readonly NavGrid grid;
        private readonly LayeredNav nav;
        // By point index, and by team (its first spawn anchor); null where nobody can walk to the target.
        private readonly LayeredReach[] tables, bases;

        private PointReach(NavGrid grid, LayeredNav nav, MatchPack match)
        {
            this.grid = grid; this.nav = nav;
            ReadOnlySpan<ControlPointDef> points = match.Points;
            tables = new LayeredReach[points.Length];
            for (int p = 0; p < points.Length; p++)
                if (TryTarget(points[p], out int target)) tables[p] = new LayeredReach(nav, target);
            int teams = 0;
            foreach (SpawnAnchor anchor in match.Bases) teams = Math.Max(teams, anchor.Team + 1);
            bases = new LayeredReach[teams];
            bool[] seen = new bool[teams];
            foreach (SpawnAnchor anchor in match.Bases)
            {
                if (seen[anchor.Team]) continue;
                seen[anchor.Team] = true;
                // A cell nobody walks on gives a table that is reached from nowhere.
                if (TryNode(anchor.Point, anchor.Layer, out int target)) bases[anchor.Team] = new LayeredReach(nav, target);
            }
        }

        // TRV-7a: the cost table of each point, by point index (null where nobody reaches the point).
        internal IReadOnlyList<LayeredReach> Tables => tables;

        internal static PointReach Of(WorldAssembly a, MatchPack match) =>
            a.Plates != null ? new PointReach(a.Terrain.Nav, a.Plates.Nav, match) : null;

        public bool TryCost(int point, SimVec3 from, int fromLayer, out long cost) => TryCost(tables[point], from, fromLayer, out cost);

        public bool TryBaseCost(int team, SimVec3 from, int fromLayer, out long cost)
        {
            cost = 0;
            return (uint)team < (uint)bases.Length && TryCost(bases[team], from, fromLayer, out cost);
        }

        private bool TryCost(LayeredReach table, SimVec3 from, int fromLayer, out long cost)
        {
            cost = 0;
            if (table == null || !TryNode(from, fromLayer, out int node)) return false;
            cost = table.CostFrom(node);
            if (cost >= 0) return true;
            cost = 0; return false;
        }

        // The cell a point is walked to: its centre's cell when that can be walked on. A centre may stand on a cell nobody walks
        // on (under a wall); then it is the walkable cell of the centre's layer whose middle is nearest to the centre and inside
        // the circle (ties to the lower cell), and with none nobody reaches the point.
        private bool TryTarget(in ControlPointDef point, out int target)
        {
            if (TryNode(point.Center, point.Layer, out target) && nav.IsPassable(target)) return true;
            target = -1;
            long r = point.Radius.Raw, left = point.Center.X.Raw - r - grid.OriginX.Raw, right = point.Center.X.Raw + r - grid.OriginX.Raw;
            long low = point.Center.Z.Raw - r - grid.OriginZ.Raw, high = point.Center.Z.Raw + r - grid.OriginZ.Raw;
            if (right < 0 || high < 0) return false;
            int x0 = left <= 0 ? 0 : (int)Math.Min(left / SimFix.Scale, grid.Width), x1 = (int)Math.Min(right / SimFix.Scale, grid.Width - 1);
            int z0 = low <= 0 ? 0 : (int)Math.Min(low / SimFix.Scale, grid.Depth), z1 = (int)Math.Min(high / SimFix.Scale, grid.Depth - 1);
            SimWide limit = new SimVec3(point.Radius, default, default).LengthSquared, best = default;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!nav.TryNode(point.Layer, x, z, out int node) || !nav.IsPassable(node)) continue;
                    SimVec3 middle = grid.Center(z * grid.Width + x, point.Center.Y);
                    SimWide distance = (middle - point.Center).LengthSquared;
                    if (distance > limit || (target >= 0 && distance >= best)) continue;
                    target = node; best = distance;
                }
            return target >= 0;
        }

        private bool TryNode(SimVec3 at, int layer, out int node)
        {
            node = -1;
            return grid.TryCell(at, out int key) && nav.TryNode(layer, key % grid.Width, key / grid.Width, out node);
        }
    }
}
