using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // Read-only editor gate: description bytes + content pack -> every problem found, in a fixed order. Three steps, and an
    // entry that fails one step does not take part in the next:
    //   1. description and bake (read errors, wall-on-terrain clearance, writer errors), stop on any problem;
    //   2. structure (references, wall layers, teams with bases, standable anchors and placed units);
    //   3. reachability of every qualified base to every qualified control point.
    // Stricter than the runtime on purpose: footprints are checked on flat maps too and every team needs a base.
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

        // Walls that fail the terrain clearance are reported and left out of the bake; the rest goes through the writer.
        private static WcMap Bake(MapDesc desc, List<MapProblem> problems)
        {
            var kept = new List<MapWall>();
            foreach (MapWall wall in desc.Walls)
            {
                if (WallClearsTerrain(desc, wall)) { kept.Add(wall); continue; }
                problems.Add(new MapProblem("GATE_WALL_TERRAIN", "wall " + Text(wall.Id), (wall.Cell.X, wall.Cell.Z)));
            }
            MapDesc baked = kept.Count == desc.Walls.Count ? desc : WithWalls(desc, kept.ToArray());
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

        private static MapDesc WithWalls(MapDesc d, MapWall[] walls) =>
            new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(),
                d.Heights?.Select(r => r.ToArray()).ToArray(), d.Surfaces, walls, d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(),
                d.Run, d.Owners.ToArray(), d.HeightOwner?.ToArray(), d.SurfaceOwner?.ToArray(), d.Stamps.ToArray(), d.Removed.ToArray(), d.Next);

        // ---- steps 2 and 3 ----

        private static void Structure(MapDesc desc, WcMap map, ContentPack pack, List<MapProblem> problems)
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pack.UnitTypeCount; i++) types.Add(pack.UnitTypeId(i));
            var badUnits = new HashSet<ulong>();
            foreach (MapUnit unit in desc.Units)
            {
                if (types.Contains(unit.Type)) continue;
                badUnits.Add(unit.Key);
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

            if (surfacesBroken) return;
            NavGrid nav = TerrainDerivation.Derive(map, pack).Nav;

            var points = new List<MapPoint>();
            foreach (MapPoint point in desc.Points)
            {
                if (Standable(nav, point.At)) { points.Add(point); continue; }
                problems.Add(new MapProblem("GATE_POINT_BLOCKED", "point " + Text(point.Id), Cell(nav, At(point.At)), point.At));
            }
            var bases = new List<MapBase>();
            foreach (MapBase b in desc.Bases)
            {
                if (Standable(nav, b.At)) { bases.Add(b); continue; }
                problems.Add(FootprintProblem(nav, b.At, "base " + Text(b.Id)));
            }
            foreach (MapPoint point in desc.Points)
                if (point.Spawn.HasValue && !Standable(nav, point.Spawn.Value))
                    problems.Add(FootprintProblem(nav, point.Spawn.Value, "spawn " + Text(point.Id)));
            // Without control points there is no match, and the runtime places reserve units at the start too.
            bool reservesPlaced = desc.Points.Count == 0;
            foreach (MapUnit unit in desc.Units)
                if (!badUnits.Contains(unit.Key) && (reservesPlaced || !unit.Reserve) && !Standable(nav, unit.At))
                    problems.Add(FootprintProblem(nav, unit.At, "unit " + Text(unit.Key)));

            var finder = new GridPathfinder(nav);
            foreach (MapBase b in bases)
                foreach (MapPoint point in points)
                    if (finder.Find(At(b.At), At(point.At)) != GridPathStatus.Found)
                        problems.Add(new MapProblem("GATE_UNREACHABLE", "base " + Text(b.Id) + " point " + Text(point.Id), Cell(nav, At(b.At)), b.At));
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
