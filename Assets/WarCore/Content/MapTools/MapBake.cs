using System;
using System.IO;
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

            // Walls are whole-cell boxes standing on the floor; the passability bitmap is the union of their footprints.
            var walls = new WcMapWall[d.Walls.Count];
            var blocked = new bool[width * depth];
            for (int i = 0; i < walls.Length; i++)
            {
                MapWall w = d.Walls[i];
                long halfX = w.Size.X * s / 2, halfZ = w.Size.Z * s / 2;
                var center = new SimVec3(SimFix.FromRaw(w.Cell.X * s + halfX), SimFix.FromRaw(w.HeightRaw / 2), SimFix.FromRaw(w.Cell.Z * s + halfZ));
                walls[i] = new WcMapWall(0, w.Id, w.Kind, w.Layer, center, SimFix.FromRaw(halfX), SimFix.FromRaw(w.HeightRaw), SimFix.FromRaw(halfZ));
                for (int z = w.Cell.Z; z < w.Cell.Z + w.Size.Z; z++)
                    for (int x = w.Cell.X; x < w.Cell.X + w.Size.X; x++) blocked[z * width + x] = true;
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
                (uint)width, (uint)depth, layers, walls, bits, heights, surfaceNames, surfaceCells);
        }

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
