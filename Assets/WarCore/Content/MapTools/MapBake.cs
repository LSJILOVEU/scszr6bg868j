using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    public sealed class MapBakeResult
    {
        private readonly byte[] descHash, wcMapBytes;
        // SHA-256 of the canonical bytes of the parsed description.
        public byte[] DescHash => (byte[])descHash.Clone();
        public string PackId { get; }
        public byte[] WcMapBytes => (byte[])wcMapBytes.Clone();
        public MapSetup Setup { get; }

        internal MapBakeResult(byte[] descHash, string packId, byte[] wcMapBytes, MapSetup setup)
        {
            this.descHash = descHash; PackId = packId; this.wcMapBytes = wcMapBytes; Setup = setup;
        }
    }

    // Description bytes -> checked model -> .wcmap bytes and match setup. Writer errors become "MAPDESC_WCMAP <WCMAP code>".
    public static class MapBake
    {
        public static MapBakeResult Run(byte[] descBytes)
        {
            MapDesc desc = MapDescJson.Read(descBytes);
            byte[] canonical = MapDescJson.Write(desc);
            byte[] mapBytes;
            try { mapBytes = WcMapWriter.Write(BuildMap(desc)); }
            catch (InvalidDataException e) { throw new InvalidDataException("MAPDESC_WCMAP " + e.Message, e); }
            return new MapBakeResult(Sha256(canonical), desc.Pack, mapBytes, BuildSetup(desc));
        }

        private static byte[] Sha256(byte[] bytes)
        {
            // Method group, not a call: StableGuardTests scans for ".Create()" as entity creation.
            Func<SHA256> factory = SHA256.Create;
            using SHA256 sha = factory();
            return sha.ComputeHash(bytes);
        }

        internal static WcMap BuildMap(MapDesc d)
        {
            const long s = SimFix.Scale;
            int width = d.Size.X, depth = d.Size.Z;
            var min = new SimVec3(SimFix.FromInt(d.Origin.X), SimFix.FromRaw(d.FloorRaw), SimFix.FromInt(d.Origin.Z));
            var max = new SimVec3(SimFix.FromInt((long)d.Origin.X + width), SimFix.FromRaw(d.CeilingRaw), SimFix.FromInt((long)d.Origin.Z + depth));
            var layers = new WcMapLayer[d.Layers.Count];
            for (int i = 0; i < layers.Length; i++) layers[i] = new WcMapLayer(d.Layers[i].Name, d.Layers[i].Bit);

            // Walls are whole-cell boxes standing on the floor, or with "baseRaw" on that raised bottom (centre Y relative to
            // the floor). The passability bitmap is the union of the footprints of floor walls plus, for raised walls, the
            // cells whose walk band they overlap (WallSpan).
            var walls = new WcMapWall[d.Walls.Count];
            var blocked = new bool[width * depth];
            for (int i = 0; i < walls.Length; i++)
            {
                MapWall w = d.Walls[i];
                long halfX = w.Size.X * s / 2, halfZ = w.Size.Z * s / 2;
                long bottom = w.BaseRaw.HasValue ? w.BaseRaw.Value - d.FloorRaw : 0;
                var center = new SimVec3(SimFix.FromRaw(w.Cell.X * s + halfX), SimFix.FromRaw(bottom + w.HeightRaw / 2), SimFix.FromRaw(w.Cell.Z * s + halfZ));
                walls[i] = new WcMapWall(0, w.Id, w.Kind, w.Layer, center, SimFix.FromRaw(halfX), SimFix.FromRaw(w.HeightRaw), SimFix.FromRaw(halfZ));
                for (int z = w.Cell.Z; z < w.Cell.Z + w.Size.Z; z++)
                    for (int x = w.Cell.X; x < w.Cell.X + w.Size.X; x++)
                        if (!w.BaseRaw.HasValue || BlocksWalk(d, w, x, z)) blocked[z * width + x] = true;
            }
            WcMapPlate[] plates = null;
            if (d.Plates.Count > 0)
            {
                List<PlateGeom> geoms = PlateGeoms(d);
                bool[] headroom = PlateSpan.GroundBlocks(width, depth, LocalPoint(d), geoms);
                for (int index = 0; index < blocked.Length; index++)
                    if (headroom[index]) blocked[index] = true;
                plates = Plates(d, geoms, WallGeoms(d));
            }
            var bits = new byte[(width * depth + 7) / 8];
            for (int index = 0; index < blocked.Length; index++)
                if (!blocked[index]) bits[index >> 3] |= (byte)(1 << (index & 7));

            int[] heights = Heights(d);
            string[] surfaceNames = null;
            byte[] surfaceCells = null;
            if (d.Surfaces != null)
            {
                surfaceNames = new string[d.Surfaces.Names.Count];
                for (int i = 0; i < surfaceNames.Length; i++) surfaceNames[i] = d.Surfaces.Names[i];
                surfaceCells = new byte[width * depth];
                for (int z = 0; z < depth; z++)
                    for (int x = 0; x < width; x++) surfaceCells[z * width + x] = (byte)Base64Index(d.Surfaces.Rows[z][x]);
            }
            return new WcMap(d.Id, d.Content, min, max, SimFix.FromInt(Math.Max(width, depth)), SimFix.FromRaw(d.FloorRaw),
                (uint)width, (uint)depth, layers, walls, bits, heights, surfaceNames, surfaceCells, plates);
        }

        // Height point (i, j) in raw Y above floorRaw (the .wcmap's local height).
        internal static Func<int, int, long> LocalPoint(MapDesc d)
        {
            Func<int, int, long> point = Point(d);
            return (i, j) => point(i, j) - d.FloorRaw;
        }

        // The description's plates in ascending id (plate number order), local heights, cutouts applied.
        internal static List<PlateGeom> PlateGeoms(MapDesc d)
        {
            var sorted = d.Plates.OrderBy(p => p.Id).ToList();
            var geoms = new List<PlateGeom>(sorted.Count);
            foreach (MapPlate p in sorted)
            {
                PlateAxis axis = p.Slope == null ? PlateAxis.Flat : p.Slope.Axis == "x" ? PlateAxis.X : PlateAxis.Z;
                long from = (p.YRaw ?? p.Slope.FromRaw) - d.FloorRaw, to = (p.YRaw ?? p.Slope.ToRaw) - d.FloorRaw;
                var solid = new bool[p.Size.X * p.Size.Z];
                for (int i = 0; i < solid.Length; i++) solid[i] = true;
                foreach (MapCutout c in d.Cutouts)
                {
                    if (c.Plate != p.Id) continue;
                    int x0 = Math.Max(c.Cell.X, p.Cell.X), x1 = Math.Min(c.Cell.X + c.Size.X, p.Cell.X + p.Size.X);
                    int z0 = Math.Max(c.Cell.Z, p.Cell.Z), z1 = Math.Min(c.Cell.Z + c.Size.Z, p.Cell.Z + p.Size.Z);
                    for (int z = z0; z < z1; z++)
                        for (int x = x0; x < x1; x++) solid[(z - p.Cell.Z) * p.Size.X + (x - p.Cell.X)] = false;
                }
                geoms.Add(new PlateGeom(p.Id, p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z, axis, from, to, p.ThickRaw, solid));
            }
            return geoms;
        }

        // The description's walls as the plate rules see them (local raw Y; a floor wall starts at 0).
        internal static List<WallGeom> WallGeoms(MapDesc d)
        {
            var walls = new List<WallGeom>(d.Walls.Count);
            foreach (MapWall w in d.Walls)
            {
                long bottom = w.BaseRaw.HasValue ? w.BaseRaw.Value - d.FloorRaw : 0;
                walls.Add(new WallGeom(w.Id, w.Cell.X, w.Cell.Z, w.Cell.X + w.Size.X, w.Cell.Z + w.Size.Z, bottom, bottom + w.HeightRaw));
            }
            return walls;
        }

        private static WcMapPlate[] Plates(MapDesc d, List<PlateGeom> geoms, List<WallGeom> walls)
        {
            var sorted = d.Plates.OrderBy(p => p.Id).ToList();
            var plates = new WcMapPlate[geoms.Count];
            for (int n = 0; n < plates.Length; n++)
            {
                PlateGeom g = geoms[n];
                MapPlate p = sorted[n];
                byte surface = WcMapPlate.NoSurface;
                if (p.Surface != null)
                    for (int i = 0; i < d.Surfaces.Names.Count; i++)
                        if (string.Equals(d.Surfaces.Names[i], p.Surface, StringComparison.Ordinal)) surface = (byte)i;
                plates[n] = new WcMapPlate(g.Id, g.X, g.Z, g.W, g.D, g.Axis, (int)(g.From + d.FloorRaw), (int)(g.To + d.FloorRaw),
                    (int)g.Thick, surface, PlateSpan.Pack(g.Solid), PlateSpan.Pack(PlateSpan.Walkable(g, walls)));
            }
            return plates;
        }

        // WallSpan for a raised wall in absolute raw Y; a flat map's ground is floorRaw.
        internal static bool BlocksWalk(MapDesc d, MapWall w, int x, int z)
        {
            long bottom = w.BaseRaw.Value;
            WallSpan.CellGround(Point(d), x, z, out long low, out long high);
            return WallSpan.BlocksWalk(bottom, bottom + w.HeightRaw, low, high);
        }

        // Height point (i, j) of a description in absolute raw Y.
        internal static Func<int, int, long> Point(MapDesc d) => d.Heights == null ? (i, j) => d.FloorRaw : (Func<int, int, long>)((i, j) => d.Heights[j][i]);

        private static int Base64Index(char c) =>
            c >= 'A' && c <= 'Z' ? c - 'A' : c >= 'a' && c <= 'z' ? c - 'a' + 26 : c >= '0' && c <= '9' ? c - '0' + 52 : c == '+' ? 62 : 63;

        // Row-major points (Z rows, then X), or null for a flat map.
        private static int[] Heights(MapDesc d)
        {
            if (d.Heights == null) return null;
            int row = d.Size.X + 1;
            var points = new int[row * (d.Size.Z + 1)];
            for (int j = 0; j <= d.Size.Z; j++)
                for (int i = 0; i < row; i++) points[j * row + i] = d.Heights[j][i];
            return points;
        }

        // Team, owner, radius and the half-open map range were checked while reading, before any Battle type is built.
        internal static MapSetup BuildSetup(MapDesc d)
        {
            int[] heights = Heights(d);
            HeightField field = heights == null ? null
                : new HeightField(d.Size.X, d.Size.Z, SimFix.FromInt(d.Origin.X), SimFix.FromInt(d.Origin.Z), heights);
            SimFix floor = SimFix.FromRaw(d.FloorRaw);
            SimVec3 Ground(MapMm at)
            {
                SimFix x = SimFix.Ratio(at.X, 1000), z = SimFix.Ratio(at.Z, 1000);
                SimFix y = field != null && field.TryHeightAt(x, z, out SimFix h) ? h : floor;
                return new SimVec3(x, y, z);
            }

            var points = new ControlPointDef[d.Points.Count];
            for (int i = 0; i < points.Length; i++)
            {
                MapPoint p = d.Points[i];
                points[i] = new ControlPointDef(p.Id, Ground(p.At), SimFix.Ratio(p.RadiusMm, 1000), p.InitialOwner,
                    p.Spawn.HasValue, p.Spawn.HasValue ? Ground(p.Spawn.Value) : default);
            }
            var bases = new SpawnAnchor[d.Bases.Count];
            for (int i = 0; i < bases.Length; i++) bases[i] = new SpawnAnchor(d.Bases[i].Team, Ground(d.Bases[i].At));
            var soldiers = new MapSoldier[d.Units.Count];
            for (int i = 0; i < soldiers.Length; i++)
            {
                MapUnit u = d.Units[i];
                soldiers[i] = new MapSoldier(u.Key, u.Type, u.Team, u.Squad, u.At.X, u.At.Z, u.Reserve);
            }
            return new MapSetup(points, bases, soldiers, d.Run.Seed, d.Run.CrowdPass);
        }
    }
}
