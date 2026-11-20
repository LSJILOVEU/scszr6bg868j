using System;
using System.Collections.Generic;
using System.Text;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // ML-10: disagreement counts of the plates a picture draws against the plates Sim uses. Clean = same shape, every count 0.
    public readonly struct PlateCheckResult
    {
        private readonly string text;

        public bool ShapeMismatch { get; }
        public int HoleCells { get; }
        public int TopCells { get; }
        public int WalkCells { get; }
        public bool IsClean => !ShapeMismatch && HoleCells == 0 && TopCells == 0 && WalkCells == 0;

        internal PlateCheckResult(bool shape, int holes, int tops, int walks, string text)
        {
            ShapeMismatch = shape; HoleCells = holes; TopCells = tops; WalkCells = walks; this.text = text;
        }

        // Counts and the first 8 cells of each rule as "layer:x,z" (map cells).
        public override string ToString() => text ?? "";
    }

    // Cell-by-cell agreement of the view's plates (ViewPlates) with the session's (PlateWorld), over each plate's rectangle:
    //   (s) shape: plates on both sides or on neither, the same count, map origin and size, each plate's layer and rectangle;
    //   (o) hole: the view has a solid cell exactly where Sim has a floor (PlateWorld.TryFloor, walkable off);
    //   (t) top: where both have one, the heights agree at two points of the cell, one in each triangle of its split;
    //   (w) walk: the view's walkable bit equals Sim's (PlateWorld.TryFloor, walkable on).
    public static class PlateCheck
    {
        private const int Listed = 8;

        public static PlateCheckResult Run(ViewPlates view, SimSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            PlateWorld own = session.PlateWorldForCheck;
            if (view == null && own == null) return new PlateCheckResult(false, 0, 0, 0, Text(false, 0, null, 0, null, 0, null));
            if (view == null || own == null || view.Count != own.LayerCount - 1 || !SameMap(view, session))
                return new PlateCheckResult(true, 0, 0, 0, Text(true, 0, null, 0, null, 0, null));
            var holes = new List<string>();
            var tops = new List<string>();
            var walks = new List<string>();
            int holeCount = 0, topCount = 0, walkCount = 0;
            for (int k = 0; k < view.Count; k++)
            {
                ViewPlate p = view[k];
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                    {
                        int x = p.X + i, z = p.Z + j;
                        bool topsAgree = true, holeAgrees = true;
                        foreach ((long u, long v) in new[] { (3L, 1L), (1L, 3L) })
                        {
                            long xRaw = view.MinXRaw + (long)x * SimFix.Scale + u * SimFix.Scale / 4;
                            long zRaw = view.MinZRaw + (long)z * SimFix.Scale + v * SimFix.Scale / 4;
                            bool mine = view.TryTopRaw(p.Layer, xRaw, zRaw, out long a);
                            bool sims = own.TryFloor(p.Layer, SimFix.FromRaw(xRaw), SimFix.FromRaw(zRaw), false, out SimFix b);
                            if (mine != sims) holeAgrees = false;
                            else if (mine && a != b.Raw) topsAgree = false;
                        }
                        if (!holeAgrees) Note(holes, ref holeCount, p.Layer, x, z);
                        else if (!topsAgree) Note(tops, ref topCount, p.Layer, x, z);
                        long cx = view.MinXRaw + (long)x * SimFix.Scale + SimFix.Scale / 2, cz = view.MinZRaw + (long)z * SimFix.Scale + SimFix.Scale / 2;
                        bool walkable = own.TryFloor(p.Layer, SimFix.FromRaw(cx), SimFix.FromRaw(cz), true, out _);
                        if (walkable != p.IsWalkable(i, j)) Note(walks, ref walkCount, p.Layer, x, z);
                    }
            }
            return new PlateCheckResult(false, holeCount, topCount, walkCount, Text(false, holeCount, holes, topCount, tops, walkCount, walks));
        }

        private static bool SameMap(ViewPlates view, SimSession session)
        {
            ViewMap map = session.Map;
            if (map == null || map.MinXRaw != view.MinXRaw || map.MinZRaw != view.MinZRaw || map.Width != view.MapWidth
                || map.Depth != view.MapDepth) return false;
            WarCore.Content.WcMap sims = session.MapForCheck;
            if (sims == null || sims.Plates.Count != view.Count) return false;
            for (int k = 0; k < view.Count; k++)
            {
                ViewPlate a = view[k];
                WarCore.Content.WcMapPlate b = sims.Plates[k];
                if (a.Layer != k + 1 || a.X != b.X || a.Z != b.Z || a.Width != b.Width || a.Depth != b.Depth) return false;
            }
            return true;
        }

        private static string Text(bool shape, int holes, List<string> holeList, int tops, List<string> topList, int walks, List<string> walkList)
        {
            var text = new StringBuilder();
            text.Append("shape ").Append(shape ? "mismatch" : "ok").Append('\n');
            Rule(text, "hole", holes, holeList);
            Rule(text, "top", tops, topList);
            Rule(text, "walk", walks, walkList);
            return text.ToString();
        }

        private static void Rule(StringBuilder text, string name, int count, List<string> list)
        {
            text.Append(name).Append(' ').Append(count);
            if (list != null)
                foreach (string cell in list) text.Append(' ').Append(cell);
            text.Append('\n');
        }

        private static void Note(List<string> list, ref int count, int layer, int x, int z)
        {
            count++;
            if (list.Count < Listed) list.Add(layer + ":" + x + "," + z);
        }
    }
}
