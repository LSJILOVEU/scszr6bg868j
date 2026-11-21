using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // TRV-3: which link shapes a graph built with links carries.
    [Flags]
    public enum TraversalShapes : byte { None = 0, Edge = 1, Over = 2, All = Edge | Over }

    // ML-6: the walkable graph of a validated .wcmap with plates under one pack's terrain rules. The ground is
    // TerrainDerivation's, under the same rules; a plate cell is walkable when its baked walkable bit is set, its surface is
    // passable and it is within the pack's walkable slope. A plate without a surface (255) counts as the default surface of
    // a map without a surface table: passable, cost 1, speed 1000. Top points come from WcMapPlate.TopRaw, as the bake, the
    // rules and the crossing test.
    public static class WcMapLayeredNav
    {
        public static LayeredNav Build(WcMap map, ContentPack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            return Build(map, pack.Terrain, pack.Id);
        }

        // Null when the map has no plates.
        public static LayeredNav Build(WcMap map, TerrainRules rules, string packId)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Plates.Count == 0) return null;
            // Derive resolves every name of the map's surface table first, so the lookups below cannot miss.
            TerrainTables ground = TerrainDerivation.Derive(map, rules, packId);
            int slopeNum = rules?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator, slopeDen = rules?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator;
            var defaultDef = new SurfaceDef("default", true, 1000, 1);
            int n = map.Plates.Count;
            var slabs = new PlateSlab[n];
            var costs = new byte[n][];
            var speeds = new ushort[n][];
            for (int k = 0; k < n; k++)
            {
                WcMapPlate p = map.Plates[k];
                SurfaceDef def = defaultDef;
                if (p.Surface != WcMapPlate.NoSurface) rules.TryFind(map.SurfaceNames[p.Surface], out def);
                var points = new int[(p.Width + 1) * (p.Depth + 1)];
                for (int j = 0; j <= p.Depth; j++)
                    for (int i = 0; i <= p.Width; i++) points[j * (p.Width + 1) + i] = (int)p.TopRaw(i, j);
                HeightField top = p.TopField(map.Min.X, map.Min.Z);
                var solid = new bool[p.Width * p.Depth];
                costs[k] = new byte[p.Width * p.Depth];
                speeds[k] = new ushort[p.Width * p.Depth];
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                    {
                        int at = j * p.Width + i;
                        solid[at] = p.IsSolid(i, j);
                        if (!solid[at] || !p.IsWalkable(i, j) || !def.Passable || !top.CellWithinSlope(i, j, slopeNum, slopeDen)) continue;
                        costs[k][at] = (byte)def.Cost;
                        speeds[k][at] = (ushort)def.SpeedMilli;
                    }
                slabs[k] = new PlateSlab(p.Id, p.X, p.Z, p.Width, p.Depth, points, p.ThickRaw, solid);
            }
            return new LayeredNav(ground, map.Min.Y.Raw, slabs, costs, speeds);
        }

        // TRV-3: as Build(map, rules, packId), plus the links LinkDerive measures under the traversal rules (outside the zones),
        // keeping only the chosen shapes. Null traversal rules: exactly Build(map, rules, packId). Otherwise the graph always
        // has a link table, empty when nothing was measured or chosen. Null when the map has no plates.
        public static LayeredNav Build(WcMap map, TerrainRules rules, string packId, TraversalRules traversal,
            IReadOnlyList<NoTraverseZone> zones, TraversalShapes shapes = TraversalShapes.All)
        {
            LayeredNav nav = Build(map, rules, packId);
            if (traversal == null || nav == null) return nav;
            TraversalLink[] measured = LinkDerive.Derive(map, nav, traversal, zones);
            var links = new List<NavLink>(measured.Length);
            foreach (TraversalLink l in measured)
            {
                if ((shapes & (l.Shape == TraversalShape.Edge ? TraversalShapes.Edge : TraversalShapes.Over)) == 0) continue;
                links.Add(new NavLink(l.From, l.To, l.Shape == TraversalShape.Edge ? 1 : 2, traversal[l.Kind].PathCost, l.Kind, l.Ticks, l.Cross, l.Gap));
            }
            return nav.WithLinks(links);
        }
    }
}
