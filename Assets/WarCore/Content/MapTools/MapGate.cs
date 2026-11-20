using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // Read-only editor gate: description bytes + content pack -> every problem found, in a fixed order. Three steps, and an
    // entry that fails one step does not take part in the next:
    //   1. description and bake (read errors, wall-on-terrain clearance, writer errors), stop on any problem;
    //   2. structure (references, wall layers, teams with bases, standable anchors and placed units);
    //   3. reachability of every qualified base to every qualified control point.
    // Stricter than the runtime on purpose: footprints are checked on flat maps too and every team needs a base.
    public enum WallSightBand : byte { SightPasses = 1, BodyShows = 2 }

    // One line of MapGate.WallHeights: a wall, its first cell, its top above the highest ground under it (raw), and its band.
    public readonly struct WallHeightLine
    {
        public readonly ulong WallId;
        public readonly int CellX, CellZ;
        public readonly long TopAboveGroundRaw;
        public readonly WallSightBand Band;
        public WallHeightLine(ulong wallId, int cellX, int cellZ, long topAboveGroundRaw, WallSightBand band)
        { WallId = wallId; CellX = cellX; CellZ = cellZ; TopAboveGroundRaw = topAboveGroundRaw; Band = band; }
        public override string ToString() => "wall " + WallId + " (" + CellX + ", " + CellZ + ") " + TopAboveGroundRaw + " raw " + Band;
    }

    public static class MapGate
    {
        // The same as InterimLocomotion.UnitFootprint; Content does not reference Interim.
        public const long FootprintRaw = SimFix.Scale / 4;
        private const uint MoverLayer = 1u;
        private static readonly SimFix Footprint = SimFix.FromRaw(FootprintRaw);

        public static IReadOnlyList<MapProblem> Check(byte[] descBytes, ContentPack pack)
        {
            if (descBytes == null) throw new ArgumentNullException(nameof(descBytes));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            var problems = new List<MapProblem>();
            MapDesc desc;
            try { desc = MapDescJson.Read(descBytes); }
            catch (InvalidDataException e)
            {
                problems.Add(ReadProblem(e.Message));
                return problems.AsReadOnly();
            }
            if (!string.Equals(pack.Id, desc.Pack, StringComparison.Ordinal))
                throw new ArgumentException("Content pack '" + pack.Id + "' is not the description's pack '" + desc.Pack + "'.", nameof(pack));

            WcMap map = Bake(desc, problems);
            if (map != null) Structure(desc, map, pack, problems);
            return Sorted(problems);
        }

        // ---- step 1 ----

        private static MapProblem ReadProblem(string message)
        {
            int space = message.IndexOf(' ');
            string code = space < 0 ? message : message.Substring(0, space);
            const string marker = " line ";
            int at = message.LastIndexOf(marker, StringComparison.Ordinal);
            int? line = null;
            if (at >= 0 && int.TryParse(message.Substring(at + marker.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) line = n;
            return new MapProblem(code, line: line);
        }

        // Walls that fail the terrain clearance (floor walls) or are buried (raised walls) are reported and left out of the
        // bake; the rest goes through the writer.
        private static WcMap Bake(MapDesc desc, List<MapProblem> problems)
        {
            var kept = new List<MapWall>();
            foreach (MapWall wall in desc.Walls)
            {
                if (wall.BaseRaw.HasValue ? WallAboveGround(desc, wall) : WallClearsTerrain(desc, wall)) { kept.Add(wall); continue; }
                problems.Add(new MapProblem(wall.BaseRaw.HasValue ? "GATE_WALL_BURIED" : "GATE_WALL_TERRAIN", "wall " + Text(wall.Id), (wall.Cell.X, wall.Cell.Z)));
            }
            List<MapPlate> plates = desc.Plates.Count > 0 ? PlateProblems(desc, kept, problems) : new List<MapPlate>();
            MapDesc baked = kept.Count == desc.Walls.Count && plates.Count == desc.Plates.Count ? desc : WithParts(desc, kept, plates);
            byte[] bytes = null;
            try { bytes = WcMapWriter.Write(MapBake.BuildMap(baked)); }
            catch (InvalidDataException e) { problems.Add(new MapProblem("MAPDESC_WCMAP", e.Message.Split(' ')[0])); }
            return problems.Count == 0 ? WcMapReader.Read(bytes) : null;
        }

        // As WcMapValidation: the wall top must stand 1 m above the highest height point under its footprint, both far edges included.
        private static bool WallClearsTerrain(MapDesc desc, MapWall wall)
        {
            if (desc.Heights == null) return true;
            long highest = long.MinValue;
            for (int z = wall.Cell.Z; z <= wall.Cell.Z + wall.Size.Z; z++)
                for (int x = wall.Cell.X; x <= wall.Cell.X + wall.Size.X; x++) highest = Math.Max(highest, desc.Heights[z][x]);
            return desc.FloorRaw + wall.HeightRaw >= highest + WcMapValidation.WallClearanceRaw;
        }

        // As WcMapValidation WALL_BURIED: a raised wall's top must be above the highest height point under its footprint
        // (on a flat map, floorRaw), both far edges included.
        private static bool WallAboveGround(MapDesc desc, MapWall wall)
        {
            long highest = WallSpan.HighestPoint(MapBake.Point(desc), wall.Cell.X, wall.Cell.Z, wall.Cell.X + wall.Size.X, wall.Cell.Z + wall.Size.Z);
            return wall.BaseRaw.Value + wall.HeightRaw > highest;
        }

        // The description with only the kept walls and plates; a cutout goes with its plate.
        private static MapDesc WithParts(MapDesc d, List<MapWall> walls, List<MapPlate> plates)
        {
            var ids = new HashSet<ulong>(plates.Select(p => p.Id));
            return new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(),
                d.Heights?.Select(r => r.ToArray()).ToArray(), d.Surfaces, plates.ToArray(), d.Cutouts.Where(c => ids.Contains(c.Plate)).ToArray(),
                walls.ToArray(), d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(), d.Run, d.Owners.ToArray(),
                d.HeightOwner?.ToArray(), d.SurfaceOwner?.ToArray(), d.Stamps.ToArray(), d.Removed.ToArray(), d.Next);
        }

        // Plate rules 3-5 (PlateSpan): buried plates are left out, then every plate of a conflicting stack pair, then every
        // wall through a remaining plate (removed from walls). Returns the kept plates in description order.
        private static List<MapPlate> PlateProblems(MapDesc desc, List<MapWall> walls, List<MapProblem> problems)
        {
            List<PlateGeom> geoms = MapBake.PlateGeoms(desc);
            Func<int, int, long> ground = MapBake.LocalPoint(desc);
            var above = new List<PlateGeom>();
            foreach (PlateGeom g in geoms)
            {
                if (PlateSpan.AboveGround(g, ground, out int x, out int z)) { above.Add(g); continue; }
                problems.Add(new MapProblem("GATE_PLATE_BURIED", "plate " + Text(g.Id), (x, z)));
            }
            var dropped = new HashSet<ulong>();
            foreach (PlateSpan.Conflict c in PlateSpan.Stacks(desc.Size.X, desc.Size.Z, above, out _, out _))
            {
                problems.Add(new MapProblem("GATE_PLATE_STACK", "plate " + Text(above[c.A].Id) + " plate " + Text(above[c.B].Id), (c.CellX, c.CellZ)));
                dropped.Add(above[c.A].Id); dropped.Add(above[c.B].Id);
            }
            List<PlateGeom> remaining = above.Where(g => !dropped.Contains(g.Id)).ToList();
            var wallGeoms = new List<WallGeom>(walls.Count);
            foreach (MapWall w in walls)
            {
                long bottom = w.BaseRaw.HasValue ? w.BaseRaw.Value - desc.FloorRaw : 0;
                wallGeoms.Add(new WallGeom(w.Id, w.Cell.X, w.Cell.Z, w.Cell.X + w.Size.X, w.Cell.Z + w.Size.Z, bottom, bottom + w.HeightRaw));
            }
            var through = new HashSet<ulong>();
            foreach (PlateSpan.Conflict c in PlateSpan.WallsThrough(wallGeoms, remaining))
            {
                problems.Add(new MapProblem("GATE_WALL_PLATE", "wall " + Text(wallGeoms[c.A].Id) + " plate " + Text(remaining[c.B].Id), (c.CellX, c.CellZ)));
                through.Add(wallGeoms[c.A].Id);
            }
            walls.RemoveAll(w => through.Contains(w.Id));
            var keep = new HashSet<ulong>(remaining.Select(g => g.Id));
            return desc.Plates.Where(p => keep.Contains(p.Id)).ToList();
        }

        // Plate rule 6 against the pack: no cell of a plate's top steeper than min(pack walkable slope, 5/3). 5/3 keeps the
        // wall-probe sphere (radius 0.25 m) going downhill within the 0.5 m minimum plate thickness: 0.25 * sqrt(1 + s^2) < 0.5.
        internal const int PlateSlopeNum = 5, PlateSlopeDen = 3;

        private static void PlateSlopes(WcMap map, ContentPack pack, List<MapProblem> problems)
        {
            long num = pack.Terrain?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator, den = pack.Terrain?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator;
            foreach (WcMapPlate plate in map.Plates)
            {
                HeightField top = plate.TopField(map.Min.X, map.Min.Z);
                bool found = false;
                for (int j = 0; j < plate.Depth && !found; j++)
                    for (int i = 0; i < plate.Width && !found; i++)
                    {
                        if (top.CellWithinSlope(i, j, num, den) && top.CellWithinSlope(i, j, PlateSlopeNum, PlateSlopeDen)) continue;
                        problems.Add(new MapProblem("GATE_PLATE_SLOPE", "plate " + Text(plate.Id), (plate.X + i, plate.Z + j)));
                        found = true;
                    }
            }
        }

        // ---- steps 2 and 3 ----

        private static void Structure(MapDesc desc, WcMap map, ContentPack pack, List<MapProblem> problems)
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pack.UnitTypeCount; i++) types.Add(pack.UnitTypeId(i));
            foreach (MapUnit unit in desc.Units)
            {
                if (types.Contains(unit.Type)) continue;
                problems.Add(new MapProblem("GATE_REF", "unit " + Text(unit.Key), Cell(desc, At(unit.At)), unit.At));
            }

            // Checked before Derive, which throws on these.
            bool surfacesBroken = false;
            if (desc.Surfaces != null)
                foreach (string name in desc.Surfaces.Names)
                {
                    if (pack.Terrain != null && pack.Terrain.TryFind(name, out _)) continue;
                    surfacesBroken = true;
                    problems.Add(new MapProblem("GATE_REF", "surface " + name));
                }

            foreach (MapLayer layer in desc.Layers)
                if ((1u << layer.Bit & MoverLayer) != 0 && desc.Walls.Any(w => string.Equals(w.Layer, layer.Name, StringComparison.Ordinal)))
                    problems.Add(new MapProblem("GATE_LAYER", "layer " + layer.Name));

            if (desc.Points.Count > 0)
                for (int team = 1; team <= 2; team++)
                    if (!desc.Bases.Any(b => b.Team == team)) problems.Add(new MapProblem("GATE_MATCH", "team " + Text(team)));

            PlateSlopes(map, pack, problems);
            if (surfacesBroken) return;
            NavGrid nav = TerrainDerivation.Derive(map, pack).Nav;

            // Anchors, units and point centres (ML-9b) with a plate id stand on a walkable node of that plate's layer (ML-6 graph
            // under the pack); the footprint on a plate is the movers' business. Without plate ids the graph is not built and
            // nothing changes.
            LayeredNav layered = HasPlateRefs(desc) ? WcMapLayeredNav.Build(map, pack) : null;
            var points = new List<MapPoint>();
            foreach (MapPoint point in desc.Points)
            {
                if (point.Plate.HasValue ? OnPlate(layered, map, point.Plate.Value, point.At, out _) : Standable(nav, point.At)) { points.Add(point); continue; }
                problems.Add(new MapProblem("GATE_POINT_BLOCKED", "point " + Text(point.Id) + Layer(map, point.Plate), Cell(nav, At(point.At)), point.At));
            }
            var bases = new List<MapBase>();
            foreach (MapBase b in desc.Bases)
            {
                if (b.Plate.HasValue ? OnPlate(layered, map, b.Plate.Value, b.At, out _) : Standable(nav, b.At)) { bases.Add(b); continue; }
                problems.Add(FootprintProblem(nav, b.At, "base " + Text(b.Id) + Layer(map, b.Plate)));
            }
            foreach (MapPoint point in desc.Points)
                if (point.Spawn.HasValue && !(point.SpawnPlate.HasValue ? OnPlate(layered, map, point.SpawnPlate.Value, point.Spawn.Value, out _) : Standable(nav, point.Spawn.Value)))
                    problems.Add(FootprintProblem(nav, point.Spawn.Value, "spawn " + Text(point.Id) + Layer(map, point.SpawnPlate)));
            // Without control points there is no match, and the runtime places reserve units at the start too.
            bool reservesPlaced = desc.Points.Count == 0;
            foreach (MapUnit unit in desc.Units)
                if ((reservesPlaced || !unit.Reserve) && !(unit.Plate.HasValue ? OnPlate(layered, map, unit.Plate.Value, unit.At, out _) : Standable(nav, unit.At)))
                    problems.Add(FootprintProblem(nav, unit.At, "unit " + Text(unit.Key) + Layer(map, unit.Plate)));

            // A base and a centre both on the ground: the ground grid, as before. Otherwise the layered graph from the base's
            // node (its plate's, or the ground's) to the centre's node (its plate's, or the ground's): the ground grid would
            // reach the cell under a centre on a plate (ML-9b).
            var finder = new GridPathfinder(nav);
            LayeredPathfinder layeredFinder = layered != null ? new LayeredPathfinder(layered) : null;
            foreach (MapBase b in bases)
                foreach (MapPoint point in points)
                {
                    bool found;
                    if (!b.Plate.HasValue && !point.Plate.HasValue) found = finder.Find(At(b.At), At(point.At)) == GridPathStatus.Found;
                    else
                        found = Node(layered, map, b.Plate, b.At, out int start) && Node(layered, map, point.Plate, point.At, out int goal)
                            && layeredFinder.Find(start, goal) == GridPathStatus.Found;
                    if (!found)
                        problems.Add(new MapProblem("GATE_UNREACHABLE", "base " + Text(b.Id) + " point " + Text(point.Id), Cell(nav, At(b.At)), b.At));
                }
        }

        private static bool HasPlateRefs(MapDesc desc) =>
            desc.Bases.Any(b => b.Plate.HasValue) || desc.Units.Any(u => u.Plate.HasValue) || desc.Points.Any(p => p.SpawnPlate.HasValue || p.Plate.HasValue);

        // The layer of a plate id: its index in the baked plate table (ascending id) + 1.
        private static int LayerOf(WcMap map, ulong plate)
        {
            for (int k = 0; k < map.Plates.Count; k++)
                if (map.Plates[k].Id == plate) return k + 1;
            return -1;
        }

        private static string Layer(WcMap map, ulong? plate) => plate.HasValue ? " layer " + Text(LayerOf(map, plate.Value)) : "";

        // A walkable node of the plate's layer under the point (MapCells, as the bake and the runtime).
        private static bool OnPlate(LayeredNav layered, WcMap map, ulong plate, MapMm at, out int node)
        {
            node = -1;
            int layer = LayerOf(map, plate);
            return layer > 0 && MapCells.TryCell(map, MapCells.Mm(at.X), MapCells.Mm(at.Z), out int cx, out int cz)
                && layered.TryNode(layer, cx, cz, out node) && layered.IsPassable(node);
        }

        // The node under a point on a plate (walkable, as OnPlate) or on the ground (layer 0).
        private static bool Node(LayeredNav layered, WcMap map, ulong? plate, MapMm at, out int node)
        {
            if (plate.HasValue) return OnPlate(layered, map, plate.Value, at, out node);
            node = -1;
            return MapCells.TryCell(map, MapCells.Mm(at.X), MapCells.Mm(at.Z), out int cx, out int cz) && layered.TryNode(0, cx, cz, out node);
        }

        // ---- ML-3 wall-height report (read before a pack turns its body on; not a load error) ----

        // Every wall of the description that is low for the body: its top minus the highest height point under its footprint
        // (both far edges included; floorRaw on a flat map), taken as the least favourable side on a slope. SightPasses: at
        // most the eye height, so standing sight and level shots go over it. BodyShows: above the eye but below the body
        // height, so it hides the viewpoint while the top of the capsule shows over it. Taller walls are not listed. Flat
        // maps are listed too (they have no lower bound on wall height). Walls in description order.
        public static IReadOnlyList<WallHeightLine> WallHeights(byte[] descBytes, CombatBody body)
        {
            if (descBytes == null) throw new ArgumentNullException(nameof(descBytes));
            MapDesc desc = MapDescJson.Read(descBytes);
            Func<int, int, long> point = MapBake.Point(desc);
            var lines = new List<WallHeightLine>();
            foreach (MapWall wall in desc.Walls)
            {
                long top = (wall.BaseRaw ?? desc.FloorRaw) + wall.HeightRaw;
                long above = top - WallSpan.HighestPoint(point, wall.Cell.X, wall.Cell.Z, wall.Cell.X + wall.Size.X, wall.Cell.Z + wall.Size.Z);
                if (above <= body.Eye.Raw) lines.Add(new WallHeightLine(wall.Id, wall.Cell.X, wall.Cell.Z, above, WallSightBand.SightPasses));
                else if (above < body.Height.Raw) lines.Add(new WallHeightLine(wall.Id, wall.Cell.X, wall.Cell.Z, above, WallSightBand.BodyShows));
            }
            return lines.AsReadOnly();
        }

        private static MapProblem FootprintProblem(NavGrid nav, MapMm at, string id) => new MapProblem("GATE_SPAWN_FOOTPRINT", id, Cell(nav, At(at)), at);

        private static bool Standable(NavGrid nav, MapMm at)
        {
            SimVec3 p = At(at);
            return nav.SegmentClear(p, p, Footprint);
        }

        private static SimVec3 At(MapMm at) => new SimVec3(SimFix.Ratio(at.X, 1000), SimFix.FromRaw(0), SimFix.Ratio(at.Z, 1000));

        // The reader keeps every "at" inside the half-open map range, so TryCell always succeeds here.
        private static (int, int) Cell(NavGrid nav, SimVec3 p)
        {
            nav.TryCell(p, out int key);
            return (key % nav.Width, key / nav.Width);
        }

        // The same arithmetic as NavGrid.TryCell, for the case where no grid is built (a broken surface table).
        private static (int, int) Cell(MapDesc desc, SimVec3 p) =>
            ((int)((p.X.Raw - desc.Origin.X * SimFix.Scale) / SimFix.Scale), (int)((p.Z.Raw - desc.Origin.Z * SimFix.Scale) / SimFix.Scale));

        private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

        // Key: code, no cell before a cell, z, x, id, line (missing before present); ordinal strings; stable for equal keys.
        private static IReadOnlyList<MapProblem> Sorted(List<MapProblem> problems) =>
            problems.OrderBy(p => p.Code, StringComparer.Ordinal)
                .ThenBy(p => p.HasCell ? 1 : 0).ThenBy(p => p.CellZ).ThenBy(p => p.CellX)
                .ThenBy(p => p.Id, StringComparer.Ordinal)
                .ThenBy(p => p.HasLine ? 1 : 0).ThenBy(p => p.Line)
                .ToList().AsReadOnly();
    }
}
