using System.Collections.Generic;

namespace WarCore.Content.MapTools
{
    // "plates" and "cutouts" (description version 2): pass 1 reads single values, pass 2 checks them against the map, the
    // surface table, next and each other. Field errors are MAPDESC_PLATE; key, missing-key and type errors MAPDESC_KEY;
    // ids MAPDESC_ID; owner and src MAPDESC_OWNER.
    internal sealed partial class MapDescReader
    {
        internal const int MaxPlateEntries = 4096;

        private MapPlate[] Plates()
        {
            JsonNode node = Version2 ? Opt(root, "plates", JsonKind.Array) : null;
            if (node == null) return new MapPlate[0];
            if (node.Items.Count > MaxPlateEntries) throw Fail(PlateCode, node.Line);
            var plates = new MapPlate[node.Items.Count];
            for (int i = 0; i < plates.Length; i++)
            {
                JsonNode e = node.Items[i];
                Enter("plate", e, "id", true);
                Item(e, JsonKind.Object);
                Keys(e, "id", "cell", "size", "yRaw", "slope", "thickRaw", "surface", "owner", "src");
                ulong id = ULong(Req(e, "id", JsonKind.Number), 1, IdCode);
                MapXZ cell = IntPair(Req(e, "cell", JsonKind.Array), 0, PlateCode);
                MapXZ size = IntPair(Req(e, "size", JsonKind.Array), 1, PlateCode);
                JsonNode y = e.Find("yRaw"), slopeNode = e.Find("slope");
                if ((y == null) == (slopeNode == null)) throw Fail(PlateCode, e.Line);
                long? yRaw = null;
                MapSlope slope = null;
                if (y != null) yRaw = Long(y, -HeightLimit, HeightLimit, PlateCode);
                else
                {
                    Item(slopeNode, JsonKind.Object);
                    Keys(slopeNode, "axis", "fromRaw", "toRaw");
                    JsonNode axis = Req(slopeNode, "axis", JsonKind.String);
                    if (axis.Text != "x" && axis.Text != "z") throw Fail(PlateCode, axis.Line);
                    long from = Long(Req(slopeNode, "fromRaw", JsonKind.Number), -HeightLimit, HeightLimit, PlateCode);
                    long to = Long(Req(slopeNode, "toRaw", JsonKind.Number), -HeightLimit, HeightLimit, PlateCode);
                    slope = new MapSlope(axis.Text, from, to);
                }
                long thick = Long(Req(e, "thickRaw", JsonKind.Number), WcMapPlate.MinThickRaw, HeightLimit, PlateCode);
                JsonNode surface = Opt(e, "surface", JsonKind.String);
                plates[i] = new MapPlate(id, cell, size, yRaw, slope, thick, surface?.Text, Owner(e), OptSrc(e));
                Leave();
            }
            return plates;
        }

        private MapCutout[] Cutouts()
        {
            JsonNode node = Version2 ? Opt(root, "cutouts", JsonKind.Array) : null;
            if (node == null) return new MapCutout[0];
            if (node.Items.Count > MaxPlateEntries) throw Fail(PlateCode, node.Line);
            var cutouts = new MapCutout[node.Items.Count];
            for (int i = 0; i < cutouts.Length; i++)
            {
                JsonNode e = node.Items[i];
                Enter("cutout", e, "id", true);
                Item(e, JsonKind.Object);
                Keys(e, "id", "plate", "cell", "size", "owner", "src");
                ulong id = ULong(Req(e, "id", JsonKind.Number), 1, IdCode);
                ulong plate = ULong(Req(e, "plate", JsonKind.Number), 1, PlateCode);
                MapXZ cell = IntPair(Req(e, "cell", JsonKind.Array), 0, PlateCode);
                MapXZ size = IntPair(Req(e, "size", JsonKind.Array), 1, PlateCode);
                cutouts[i] = new MapCutout(id, plate, cell, size, Owner(e), OptSrc(e));
                Leave();
            }
            return cutouts;
        }

        // Pass 2, after the surface table and before the walls: plates, then cutouts.
        private void SecondPlates(MapDesc d)
        {
            int w = d.Size.X, depth = d.Size.Z;
            JsonNode plates = root.Find("plates");
            var plateIds = new HashSet<ulong>();
            for (int i = 0; i < d.Plates.Count; i++)
            {
                MapPlate plate = d.Plates[i];
                JsonNode e = plates.Items[i];
                Enter("plate", e, "id", true);
                if (!plateIds.Add(plate.Id) || plate.Id >= d.Next.Plate) throw Fail(IdCode, e.Find("id").Line);
                if ((long)plate.Cell.X + plate.Size.X > w || (long)plate.Cell.Z + plate.Size.Z > depth) throw Fail(PlateCode, e.Find("size").Line);
                if (plate.YRaw.HasValue) Level(d, plate.YRaw.Value, e.Find("yRaw"));
                else
                {
                    JsonNode slope = e.Find("slope");
                    Level(d, plate.Slope.FromRaw, slope.Find("fromRaw"));
                    Level(d, plate.Slope.ToRaw, slope.Find("toRaw"));
                    if (plate.Slope.FromRaw == plate.Slope.ToRaw) throw Fail(PlateCode, slope.Line);
                }
                if (plate.Surface != null && (d.Surfaces == null || !Contains(d.Surfaces.Names, plate.Surface)))
                    throw Fail(PlateCode, e.Find("surface").Line);
                Tail(d, e, plate.Owner, plate.Src, "plate");
            }

            JsonNode cutouts = root.Find("cutouts");
            var cutoutIds = new HashSet<ulong>();
            for (int i = 0; i < d.Cutouts.Count; i++)
            {
                MapCutout cutout = d.Cutouts[i];
                JsonNode e = cutouts.Items[i];
                Enter("cutout", e, "id", true);
                if (!cutoutIds.Add(cutout.Id) || cutout.Id >= d.Next.Cutout) throw Fail(IdCode, e.Find("id").Line);
                if (!plateIds.Contains(cutout.Plate)) throw Fail(PlateCode, e.Find("plate").Line);
                if ((long)cutout.Cell.X + cutout.Size.X > w || (long)cutout.Cell.Z + cutout.Size.Z > depth) throw Fail(PlateCode, e.Find("size").Line);
                Tail(d, e, cutout.Owner, cutout.Src, "cutout");
            }
        }

        // A plate top inside [floorRaw, ceilingRaw].
        private void Level(MapDesc d, long raw, JsonNode node)
        {
            if (raw < d.FloorRaw || raw > d.CeilingRaw) throw Fail(PlateCode, node.Line);
        }

        private static bool Contains(IReadOnlyList<string> names, string name)
        {
            foreach (string n in names)
                if (string.Equals(n, name, System.StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
