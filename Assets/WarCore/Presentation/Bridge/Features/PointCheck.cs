using System;
using System.Collections.Generic;
using System.Text;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // Disagreement counts of the control points of one session. Clean = same point count and every count zero.
    public readonly struct PointCheckResult
    {
        private readonly string text;

        public int Positions { get; }
        public bool CountMismatch { get; }
        public int OffMap { get; }
        public int Blocked { get; }
        public int Heights { get; }
        public int Views { get; }
        public bool IsClean => !CountMismatch && OffMap == 0 && Blocked == 0 && Heights == 0 && Views == 0;

        internal PointCheckResult(int positions, bool count, int off, int blocked, int heights, int views, string text)
        {
            Positions = positions; CountMismatch = count; OffMap = off; Blocked = blocked; Heights = heights; Views = views;
            this.text = text;
        }

        // Counts and the first 8 entries of each rule (see PointCheck.Text).
        public override string ToString() => text ?? "";
    }

    // The match's own positions against the map Sim uses, and the drawn points against the match:
    //   positions, in order: every point centre, its spawn when it has one, then every base anchor;
    //   (o) off map: the cell (arithmetic shift, towards minus infinity) lies outside the grid; such a position is not checked further;
    //   (b) blocked: on terrain the navigation grid refuses the cell; on a flat map a wall touches it or the file bit is off;
    //   (h) height: on a height field the position's Y differs from Sim's ground there (bit for bit), or the ground has no height there;
    //   (n) count: the frame has a different number of points than the match;
    //   (v) views: Id, X, Z or Radius of a drawn point differs from what SimSession puts in the frame (same formula).
    // A flat map is not checked for height: capture uses the horizontal distance and deployment takes the ground again.
    public static class PointCheck
    {
        private const int Listed = 8;

        public static PointCheckResult Run(FrameView view, SimSession session)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (session == null) throw new ArgumentNullException(nameof(session));
            MatchPack match = session.MatchPack;
            if (match == null)
                return new PointCheckResult(0, false, 0, 0, 0, 0, Text(0, false, 0, null, 0, null, 0, null, 0, null));

            var positions = new List<SimVec3>();
            foreach (ControlPointDef def in match.Points)
            {
                positions.Add(def.Center);
                if (def.HasSpawn) positions.Add(def.Spawn);
            }
            foreach (SpawnAnchor anchor in match.Bases)
                positions.Add(anchor.Point);

            ViewMap map = session.Map;
            TerrainTables terrain = session.Terrain;
            HeightField ground = terrain?.Ground;
            var off = new List<string>();
            var blocked = new List<string>();
            var heights = new List<string>();
            int offCount = 0, blockedCount = 0, heightCount = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                SimVec3 p = positions[i];
                // Without a map nothing can be on it: every position counts as off map.
                if (map == null) { Note(off, ref offCount, i + "@-"); continue; }
                long cx = (p.X.Raw - map.MinXRaw) >> 16, cz = (p.Z.Raw - map.MinZRaw) >> 16;
                string at = i + "@" + cx + "," + cz;
                if (cx < 0 || cx >= map.Width || cz < 0 || cz >= map.Depth)
                {
                    Note(off, ref offCount, at);
                    continue;
                }
                int x = (int)cx, z = (int)cz;
                bool refused = terrain != null ? !terrain.Nav.IsPassable(x, z) : session.BlockedCell(x, z) || !map.IsPassable(x, z);
                if (refused)
                    Note(blocked, ref blockedCount, at);
                if (ground != null && (!ground.TryHeightAt(p.X, p.Z, out SimFix y) || y.Raw != p.Y.Raw))
                    Note(heights, ref heightCount, at);
            }

            ReadOnlySpan<ViewPoint> drawn = view.Points;
            bool count = drawn.Length != match.Points.Length;
            var views = new List<string>();
            int viewCount = 0;
            for (int i = 0; i < Math.Min(drawn.Length, match.Points.Length); i++)
            {
                ControlPointDef def = match.Points[i];
                ViewPoint v = drawn[i];
                if (v.Id != def.Id || !Same(v.X, (float)def.Center.X.Raw / SimFix.Scale) || !Same(v.Z, (float)def.Center.Z.Raw / SimFix.Scale)
                    || !Same(v.Radius, (float)def.Radius.Raw / SimFix.Scale))
                    Note(views, ref viewCount, i + "#" + def.Id);
            }
            return new PointCheckResult(positions.Count, count, offCount, blockedCount, heightCount, viewCount,
                Text(positions.Count, count, offCount, off, blockedCount, blocked, heightCount, heights, viewCount, views));
        }

        // Bit for bit, so that -0 and +0 or two NaNs are told apart exactly as the frame stores them.
        private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        // Six lines, always: "positions N", "count same|differs", then "off N", "blocked N", "heights N", "views N", each
        // count followed by up to the first 8 entries: "index@x,z" (position index and its cell) or "index#id" (point).
        private static string Text(int positions, bool count, int off, List<string> offAt, int blocked, List<string> blockedAt,
            int heights, List<string> heightAt, int views, List<string> viewAt)
        {
            var text = new StringBuilder();
            text.Append("positions ").Append(positions).Append('\n');
            text.Append("count ").Append(count ? "differs" : "same").Append('\n');
            Line(text, "off", off, offAt);
            Line(text, "blocked", blocked, blockedAt);
            Line(text, "heights", heights, heightAt);
            Line(text, "views", views, viewAt);
            return text.ToString();
        }

        private static void Line(StringBuilder text, string name, int count, List<string> at)
        {
            text.Append(name).Append(' ').Append(count);
            if (at != null)
                foreach (string a in at) text.Append(' ').Append(a);
            text.Append('\n');
        }

        private static void Note(List<string> list, ref int count, string at)
        {
            count++;
            if (list.Count < Listed) list.Add(at);
        }
    }
}
