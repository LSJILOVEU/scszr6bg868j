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

        // Counts and the first 8 coordinates of each rule (see TerrainCheck.Text).
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
                return new TerrainCheckResult(true, 0, 0, 0, Text(true, 0, null, 0, null, 0, null));

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
            return new TerrainCheckResult(false, wallCount, navCount, heightCount,
                Text(false, wallCount, walls, navCount, nav, heightCount, heights));
        }

        // Four lines, always: "shape same|differs", then "walls N", "nav N", "heights N", each count followed by up to
        // the first 8 disagreeing coordinates "x,z" (cells) or "i,j" (points) in scan order.
        private static string Text(bool shape, int walls, List<string> wallAt, int nav, List<string> navAt, int heights, List<string> heightAt)
        {
            var text = new StringBuilder();
            text.Append("shape ").Append(shape ? "differs" : "same").Append('\n');
            Line(text, "walls", walls, wallAt);
            Line(text, "nav", nav, navAt);
            Line(text, "heights", heights, heightAt);
            return text.ToString();
        }

        private static void Line(StringBuilder text, string name, int count, List<string> at)
        {
            text.Append(name).Append(' ').Append(count);
            if (at != null)
                foreach (string a in at) text.Append(' ').Append(a);
            text.Append('\n');
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
