using System;
using System.Collections.Generic;
using System.Text;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // Disagreement counts of one view against one session. Clean = same shape and every count zero.
    public readonly struct TerrainCheckResult
    {
        private readonly string text;

        public bool ShapeMismatch { get; }
        public int WallCells { get; }
        public int NavCells { get; }
        public int HeightPoints { get; }
        public bool IsClean => !ShapeMismatch && WallCells == 0 && NavCells == 0 && HeightPoints == 0;

        internal TerrainCheckResult(bool shape, int walls, int nav, int heights, string text)
        {
            ShapeMismatch = shape; WallCells = walls; NavCells = nav; HeightPoints = heights; this.text = text;
        }

        public override string ToString() => text ?? "";
    }

    // Cell-by-cell agreement of what is drawn with what Sim uses:
    //   (s) shape: width, depth, raw origin, height field and surface table present on both sides;
    //   (a) a visible wall box contains the cell centre at FlatHeight + 0.5 m exactly where SimSession.BlockedCell is true;
    //   (c) on terrain, Sim's navigation bit = file bit AND walkable surface AND slope within the rules (TerrainDerivation);
    //   (h) on a height field, every view point height equals Sim's ground point height.
    // Control points are checked by the terrain-world run (L8b), not here.
    public static class TerrainCheck
    {
        private const int Listed = 8;

        public static TerrainCheckResult Run(ViewMap view, SimSession session)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (session == null) throw new ArgumentNullException(nameof(session));
            ViewMap own = session.Map;
            TerrainTables terrain = session.Terrain;
            if (own == null || own.Width != view.Width || own.Depth != view.Depth || own.MinXRaw != view.MinXRaw
                || own.MinZRaw != view.MinZRaw || own.HasHeightField != view.HasHeightField
                || own.HasSurfaceTable != view.HasSurfaceTable || (terrain != null && terrain.Ground != null) != view.HasHeightField)
                return new TerrainCheckResult(true, 0, 0, 0, "shape differs");

            var walls = new List<string>();
            var nav = new List<string>();
            var heights = new List<string>();
            int wallCount = 0, navCount = 0, heightCount = 0;
            HeightField viewField = terrain != null && view.HasHeightField ? ViewGround.Field(view) : null;
            TerrainRules rules = session.TerrainRules;
            long num = rules?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator, den = rules?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator;
            for (int z = 0; z < view.Depth; z++)
                for (int x = 0; x < view.Width; x++)
                {
                    if (VisibleWall(view, x, z) != session.BlockedCell(x, z))
                        Note(walls, ref wallCount, x, z);
                    if (terrain == null)
                        continue;
                    bool expected = view.IsPassable(x, z) && SurfaceWalkable(view, rules, x, z)
                        && (viewField == null || viewField.CellWithinSlope(x, z, num, den));
                    if (terrain.Nav.IsPassable(x, z) != expected)
                        Note(nav, ref navCount, x, z);
                }
            if (viewField != null)
                for (int j = 0; j <= view.Depth; j++)
                    for (int i = 0; i <= view.Width; i++)
                        if (view.VertexRaw(i, j) != terrain.Ground.VertexRaw(i, j))
                            Note(heights, ref heightCount, i, j);
            if (wallCount == 0 && navCount == 0 && heightCount == 0)
                return new TerrainCheckResult(false, 0, 0, 0, "");
            var text = new StringBuilder();
            text.Append("walls ").Append(wallCount).Append(' ').Append(string.Join(" ", walls)).Append('\n');
            text.Append("nav ").Append(navCount).Append(' ').Append(string.Join(" ", nav)).Append('\n');
            text.Append("heights ").Append(heightCount).Append(' ').Append(string.Join(" ", heights)).Append('\n');
            return new TerrainCheckResult(false, wallCount, navCount, heightCount, text.ToString());
        }

        private static void Note(List<string> list, ref int count, int x, int z)
        {
            count++;
            if (list.Count < Listed) list.Add(x + "," + z);
        }

        // A wall box (centre and full size, as drawn) contains the centre of cell (x, z) at FlatHeight + 0.5 m.
        private static bool VisibleWall(ViewMap view, int x, int z)
        {
            float px = view.MinX + x + 0.5f, py = view.FloorY + 0.5f, pz = view.MinZ + z + 0.5f;
            foreach (ViewWall w in view.Walls)
                if (Math.Abs(px - w.CenterX) <= w.SizeX / 2 && Math.Abs(py - w.CenterY) <= w.SizeY / 2
                    && Math.Abs(pz - w.CenterZ) <= w.SizeZ / 2)
                    return true;
            return false;
        }

        // Without a surface table every cell is on the default, walkable surface (TerrainDerivation). With one, the map's
        // name for the cell must be a walkable surface of the session's rules; an unknown name cannot occur (derivation throws).
        private static bool SurfaceWalkable(ViewMap view, TerrainRules rules, int x, int z)
        {
            if (!view.HasSurfaceTable)
                return true;
            return rules != null && rules.TryFind(view.SurfaceName(view.SurfaceIndex(x, z)), out SurfaceDef def) && def.Passable;
        }
    }
}
