using System.Collections.Generic;

namespace WarCore.Content.MapTools
{
    // Pass 2: cross rules in canonical key order. Line numbers come from the parsed nodes; the values from the pass 1 model.
    internal sealed partial class MapDescReader
    {
        private const string Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        private const long WorldLimit = 16384;

        private void Second(MapDesc d)
        {
            int w = d.Size.X, depth = d.Size.Z;
            JsonNode origin = root.Find("origin");
            if (d.Origin.X < -WorldLimit || (long)d.Origin.X + w > WorldLimit) throw Fail(SizeCode, origin.Items[0].Line);
            if (d.Origin.Z < -WorldLimit || (long)d.Origin.Z + depth > WorldLimit) throw Fail(SizeCode, origin.Items[1].Line);

            JsonNode heights = root.Find("heights");
            if (heights != null)
            {
                if (heights.Items.Count != depth + 1) throw Fail(Grid, heights.Line);
                foreach (JsonNode row in heights.Items)
                    if (row.Items.Count != w + 1) throw Fail(Grid, row.Line);
            }

            JsonNode surfaces = root.Find("surfaces");
            if (surfaces != null)
            {
                JsonNode rows = surfaces.Find("rows");
                if (rows.Items.Count != depth) throw Fail(Grid, rows.Line);
                foreach (JsonNode row in rows.Items)
                    if (!GridRow(row.Text, w, d.Surfaces.Names.Count, false)) throw Fail(Grid, row.Line);
            }

            var layerNames = new HashSet<string>();
            foreach (MapLayer layer in d.Layers) layerNames.Add(layer.Name);
            JsonNode walls = root.Find("walls");
            var wallIds = new HashSet<ulong>();
            for (int i = 0; i < d.Walls.Count; i++)
            {
                MapWall wall = d.Walls[i];
                JsonNode e = walls.Items[i];
                Enter("wall", e, "id", true);
                if (!wallIds.Add(wall.Id) || wall.Id >= d.Next.Wall) throw Fail(IdCode, e.Find("id").Line);
                if (!layerNames.Contains(wall.Layer)) throw Fail(WallCode, e.Find("layer").Line);
                if ((long)wall.Cell.X + wall.Size.X > w || (long)wall.Cell.Z + wall.Size.Z > depth) throw Fail(WallCode, e.Find("size").Line);
                // ceilingRaw - floorRaw spans up to 2^64 - 1, so the room is compared as ulong once it is known to be non-negative.
                if (d.CeilingRaw < d.FloorRaw || unchecked((ulong)(d.CeilingRaw - d.FloorRaw)) < (ulong)wall.HeightRaw)
                    throw Fail(WallCode, e.Find("heightRaw").Line);
                Tail(d, e, wall.Owner, wall.Src, "wall");
            }

            JsonNode points = root.Find("points");
            var pointIds = new HashSet<int>();
            for (int i = 0; i < d.Points.Count; i++)
            {
                MapPoint point = d.Points[i];
                JsonNode e = points.Items[i];
                Enter("point", e, "id", false);
                if (!pointIds.Add(point.Id) || point.Id >= d.Next.Point) throw Fail(IdCode, e.Find("id").Line);
                Inside(d, point.At, e.Find("at"));
                if (point.Spawn.HasValue) Inside(d, point.Spawn.Value, e.Find("spawn"));
                Tail(d, e, point.Owner, point.Src, "point");
            }

            JsonNode bases = root.Find("bases");
            var baseIds = new HashSet<int>();
            for (int i = 0; i < d.Bases.Count; i++)
            {
                MapBase anchor = d.Bases[i];
                JsonNode e = bases.Items[i];
                Enter("base", e, "id", false);
                if (!baseIds.Add(anchor.Id) || anchor.Id >= d.Next.Base) throw Fail(IdCode, e.Find("id").Line);
                Inside(d, anchor.At, e.Find("at"));
                Tail(d, e, anchor.Owner, anchor.Src, "base");
            }

            JsonNode units = root.Find("units");
            var unitKeys = new HashSet<ulong>();
            for (int i = 0; i < d.Units.Count; i++)
            {
                MapUnit unit = d.Units[i];
                JsonNode e = units.Items[i];
                Enter("unit", e, "key", true);
                if (!unitKeys.Add(unit.Key) || unit.Key >= d.Next.Unit) throw Fail(IdCode, e.Find("key").Line);
                Inside(d, unit.At, e.Find("at"));
                Tail(d, e, unit.Owner, unit.Src, "unit");
            }

            OwnerGrid(root.Find("heightOwner"), heights != null, depth + 1, w + 1, d.Owners.Count);
            OwnerGrid(root.Find("surfaceOwner"), surfaces != null, depth, w, d.Owners.Count);

            JsonNode stamps = root.Find("stamps");
            var stampKeys = new HashSet<(string, string, ulong)>();
            for (int i = 0; i < d.Stamps.Count; i++)
            {
                MapStamp stamp = d.Stamps[i];
                EnterStamp(stamps.Items[i]);
                if (!stampKeys.Add((stamp.Instance, stamp.Kind, stamp.Src))) throw Fail(OwnerCode, stamps.Items[i].Line);
                Leave();
            }

            JsonNode removed = root.Find("removed");
            var removedKeys = new HashSet<(string, string, ulong)>();
            for (int i = 0; i < d.Removed.Count; i++)
            {
                MapSrc gone = d.Removed[i];
                Enter("removed", removed.Items[i], null, false);
                if (!removedKeys.Add((gone.Instance, gone.Kind, gone.Src))) throw Fail(OwnerCode, removed.Items[i].Line);
                Leave();
            }
        }

        // Entry owner index and the kind of its "src"; leaves the entry scope.
        private void Tail(MapDesc d, JsonNode e, int? owner, MapSrc src, string kind)
        {
            if (owner.HasValue && owner.Value >= d.Owners.Count) throw Fail(OwnerCode, e.Find("owner").Line);
            if (src != null && src.Kind != kind) throw Fail(OwnerCode, e.Find("src").Find("kind").Line);
            Leave();
        }

        // Half-open map range in millimetres: origin <= v < origin + size.
        private void Inside(MapDesc d, MapMm at, JsonNode pair)
        {
            long x0 = d.Origin.X * 1000L, z0 = d.Origin.Z * 1000L;
            if (at.X < x0 || at.X >= x0 + d.Size.X * 1000L) throw Fail(PointCode, pair.Items[0].Line);
            if (at.Z < z0 || at.Z >= z0 + d.Size.Z * 1000L) throw Fail(PointCode, pair.Items[1].Line);
        }

        private void OwnerGrid(JsonNode grid, bool allowed, int rowCount, int length, int owners)
        {
            if (grid == null) return;
            if (!allowed || grid.Items.Count != rowCount) throw Fail(OwnerCode, grid.Line);
            foreach (JsonNode row in grid.Items)
                if (!GridRow(row.Text, length, owners, true)) throw Fail(OwnerCode, row.Line);
        }

        // One base64 character per cell, each an index below the table size; owner grids also allow '.' (no owner).
        internal static bool GridRow(string row, int length, int tableSize, bool dot)
        {
            if (row.Length != length) return false;
            foreach (char c in row)
            {
                if (dot && c == '.') continue;
                int index = Base64.IndexOf(c);
                if (index < 0 || index >= tableSize) return false;
            }
            return true;
        }
    }
}
