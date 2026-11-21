using System;
using System.Collections.Generic;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ML-12a: one entry of a floor view: its kind ("wall", "plate", "cutout", "point", "base", "unit"), id, the floor it
    // belongs to, and Ghost when that floor is above the chosen one (the editor draws it as a faint outline).
    public sealed class MapFloorItem
    {
        public string Kind { get; }
        public ulong Id { get; }
        public int Floor { get; }
        public bool Ghost { get; }

        internal MapFloorItem(string kind, ulong id, int floor, bool ghost) { Kind = kind; Id = id; Floor = floor; Ghost = ghost; }
    }

    // ML-12a: which entries the editor shows for a chosen floor (0 = the ground). An editing aid only; ML-10's LayerCut,
    // which follows the player at run time, is separate.
    //   Plates and walls of a building stamp ("building-<n>"): the floor in their source number (a stair belongs to the floor
    //   it starts on, the door to the ground). A cutout: its plate's floor. Any other wall inside a building's walls: the
    //   number of that building's floor plates whose top is at or below the wall's bottom; outside every building: 0. Any
    //   other plate: 0. Points, bases and units: the floor of the plate they stand on, 0 on the ground.
    public static class MapFloorView
    {
        public const string BuildingPrefix = "building-";

        public static IReadOnlyList<MapFloorItem> Of(MapDocument doc, int floor)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (floor < 0) throw new ArgumentException("the floor must be 0 or more", nameof(floor));
            MapDesc d = doc.Desc;
            var plateFloor = new Dictionary<ulong, int>();
            foreach (MapPlate p in d.Plates) plateFloor[p.Id] = BuildingFloor(p.Src, out int f) ? f : 0;
            List<Footprint> buildings = Footprints(d);
            var items = new List<MapFloorItem>();
            void Add(string kind, ulong id, int f) => items.Add(new MapFloorItem(kind, id, f, f > floor));
            foreach (MapWall w in d.Walls) Add("wall", w.Id, BuildingFloor(w.Src, out int f) ? f : WallFloor(d, buildings, w));
            foreach (MapPlate p in d.Plates) Add("plate", p.Id, plateFloor[p.Id]);
            foreach (MapCutout c in d.Cutouts) Add("cutout", c.Id, plateFloor.TryGetValue(c.Plate, out int f) ? f : 0);
            foreach (MapPoint p in d.Points) Add("point", (ulong)p.Id, On(plateFloor, p.Plate));
            foreach (MapBase b in d.Bases) Add("base", (ulong)b.Id, On(plateFloor, b.Plate));
            foreach (MapUnit u in d.Units) Add("unit", u.Key, On(plateFloor, u.Plate));
            return items.AsReadOnly();
        }

        private sealed class Footprint
        {
            internal string Instance;
            internal int X0, Z0, X1, Z1;
            internal List<long> Tops = new List<long>();
        }

        private static bool IsBuilding(MapSrc src) => src != null && src.Instance.StartsWith(BuildingPrefix, StringComparison.Ordinal);

        private static bool BuildingFloor(MapSrc src, out int floor)
        {
            floor = 0;
            if (!IsBuilding(src)) return false;
            MapBuildings.Decode(src.Src, out floor, out _);
            return true;
        }

        private static int On(Dictionary<ulong, int> plateFloor, ulong? plate) =>
            plate.HasValue && plateFloor.TryGetValue(plate.Value, out int f) ? f : 0;

        // Each building's cells (the box of its walls) and the tops of its flat floor plates.
        private static List<Footprint> Footprints(MapDesc d)
        {
            var byName = new Dictionary<string, Footprint>(StringComparer.Ordinal);
            var order = new List<Footprint>();
            foreach (MapWall w in d.Walls)
            {
                if (!IsBuilding(w.Src)) continue;
                if (!byName.TryGetValue(w.Src.Instance, out Footprint b))
                {
                    b = new Footprint { Instance = w.Src.Instance, X0 = int.MaxValue, Z0 = int.MaxValue, X1 = int.MinValue, Z1 = int.MinValue };
                    byName.Add(b.Instance, b);
                    order.Add(b);
                }
                b.X0 = Math.Min(b.X0, w.Cell.X); b.Z0 = Math.Min(b.Z0, w.Cell.Z);
                b.X1 = Math.Max(b.X1, w.Cell.X + w.Size.X); b.Z1 = Math.Max(b.Z1, w.Cell.Z + w.Size.Z);
            }
            foreach (MapPlate p in d.Plates)
                if (IsBuilding(p.Src) && p.YRaw.HasValue && byName.TryGetValue(p.Src.Instance, out Footprint b)) b.Tops.Add(p.YRaw.Value);
            return order;
        }

        private static int WallFloor(MapDesc d, List<Footprint> buildings, MapWall w)
        {
            long bottom = w.BaseRaw ?? d.FloorRaw;
            foreach (Footprint b in buildings)
            {
                if (w.Cell.X < b.X0 || w.Cell.Z < b.Z0 || w.Cell.X >= b.X1 || w.Cell.Z >= b.Z1) continue;
                int floor = 0;
                foreach (long top in b.Tops) if (top <= bottom) floor++;
                return floor;
            }
            return 0;
        }
    }
}
