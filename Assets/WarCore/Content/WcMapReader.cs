using System;
using System.IO;
using System.Text;
using WarCore.Sim;
using static WarCore.Content.WcMapValidation;

namespace WarCore.Content
{
    public static class WcMapReader
    {
        public static WcMap Read(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            Require(bytes.Length >= 40 && bytes.Length <= MaxFileBytes, "LENGTH");
            using var stream = new MemoryStream(bytes, false);
            using var r = new BinaryReader(stream);
            try
            {
                Require(r.ReadUInt32() == WcMapWriter.Magic, "MAGIC");
                Require(r.ReadUInt16() == WcMapWriter.FormatVersion, "VERSION");
                Require(r.ReadUInt16() == WcMapWriter.SectionCount, "SECTION_COUNT");
                Require(HashEquals(bytes, bytes.Length - 32, WcMapWriter.Digest(bytes, 0, bytes.Length - 32)), "FILE_HASH");
                var sections = new byte[12][];
                var offsets = new uint[12];
                for (ushort expected = 1; expected <= 11; expected++)
                {
                    long start = stream.Position;
                    Require(bytes.Length - 32 - start >= 38, "TRUNCATED");
                    ushort id = r.ReadUInt16();
                    Require(id >= 1 && id <= 11, "UNKNOWN_SECTION");
                    Require(id == expected, "SECTION_ORDER");
                    uint length = r.ReadUInt32();
                    Require(length <= MaxFileBytes && length <= bytes.Length - 32 - stream.Position - 32, "LENGTH");
                    if (id == 5 || id == 8 || id == 9 || id == 11) Require(length == 0, "UNSUPPORTED_SECTION");
                    sections[id] = r.ReadBytes((int)length);
                    Require(HashEquals(bytes, (int)stream.Position, WcMapWriter.Digest(bytes, (int)start, 6 + (int)length)), "SECTION_HASH");
                    stream.Position += 32;
                    offsets[id] = (uint)start;
                }
                Require(stream.Position == bytes.Length - 32, "TRAILING_BYTES");
                return Decode(sections, offsets);
            }
            catch (EndOfStreamException ex) { throw new InvalidDataException("WCMAP_TRUNCATED", ex); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("WCMAP_UTF8", ex); }
        }

        private static bool HashEquals(byte[] bytes, int offset, byte[] hash)
        {
            for (int i = 0; i < hash.Length; i++) if (bytes[offset + i] != hash[i]) return false;
            return true;
        }

        private static BinaryReader Section(byte[][] sections, int id) => new BinaryReader(new MemoryStream(sections[id], false));
        private static void End(BinaryReader r) => Require(r.BaseStream.Position == r.BaseStream.Length, "PAYLOAD_LENGTH");
        private static string Text(BinaryReader r)
        {
            ushort length = r.ReadUInt16();
            Require(length > 0 && length <= 255 && length <= r.BaseStream.Length - r.BaseStream.Position, "STRING_LENGTH");
            string value = new UTF8Encoding(false, true).GetString(r.ReadBytes(length));
            TextBytes(value);
            return value;
        }
        private static SimFix Fix(BinaryReader r) => SimFix.FromRaw(r.ReadInt64());
        private static SimVec3 Vector(BinaryReader r) => new SimVec3(Fix(r), Fix(r), Fix(r));

        private static WcMap Decode(byte[][] sections, uint[] offsets)
        {
            using var header = Section(sections, 1);
            string mapId = Text(header), contentId = Text(header);
            SimVec3 min = Vector(header), max = Vector(header);
            SimFix chunkSize = Fix(header);
            Require(header.ReadInt64() == SimFix.Scale, "CELL_SIZE"); End(header);

            using var layerReader = Section(sections, 2);
            ushort count = layerReader.ReadUInt16();
            Require(count > 0 && count <= 32, "LAYERS");
            var layers = new WcMapLayer[count];
            for (int i = 0; i < count; i++)
            {
                layers[i] = new WcMapLayer(Text(layerReader), layerReader.ReadByte());
                Require(i == 0 || StringComparer.Ordinal.Compare(layers[i - 1].Name, layers[i].Name) < 0, "LAYER_ORDER");
            }
            End(layerReader);
            using var flat = Section(sections, 3);
            Require(flat.ReadUInt64() == 0, "REFERENCE");
            SimFix height = Fix(flat);
            byte diagonal = flat.ReadByte(), heightFlags = flat.ReadByte();
            Require(diagonal <= 1 && heightFlags == 0, "HEIGHT_PROFILE");
            ushort pointsX = 0, pointsZ = 0;
            int[] heightPoints = null;
            if (diagonal == 1)
            {
                pointsX = flat.ReadUInt16(); pointsZ = flat.ReadUInt16();
                Require(pointsX >= 2 && pointsX <= MaxSide + 1 && pointsZ >= 2 && pointsZ <= MaxSide + 1, "HEIGHT_SIZE");
                Require(flat.BaseStream.Length - flat.BaseStream.Position == pointsX * pointsZ * 4, "PAYLOAD_LENGTH");
                heightPoints = new int[pointsX * pointsZ];
                for (int i = 0; i < heightPoints.Length; i++) heightPoints[i] = flat.ReadInt32();
            }
            End(flat);

            using var wallReader = Section(sections, 4);
            uint wallCount = wallReader.ReadUInt32();
            Require(wallCount <= MaxWalls, "CAPACITY");
            var walls = new WcMapWall[(int)wallCount];
            for (int i = 0; i < walls.Length; i++)
            {
                ulong chunk = wallReader.ReadUInt64(), key = wallReader.ReadUInt64();
                string kind = Text(wallReader), layer = Text(wallReader);
                Require(wallReader.ReadByte() == 1, "SHAPE");
                SimVec3 center = Vector(wallReader);
                SimFix hx = Fix(wallReader), h = Fix(wallReader), hz = Fix(wallReader);
                Require(wallReader.ReadInt64() == 0, "YAW");
                walls[i] = new WcMapWall(chunk, key, kind, layer, center, hx, h, hz);
                Require(i == 0 || walls[i - 1].Placement != key, "DUPLICATE_KEY");
                Require(i == 0 || walls[i - 1].Placement < key, "WALL_ORDER");
            }
            End(wallReader);
            using var surface = Section(sections, 6);
            byte surfaceId = surface.ReadByte(), surfaceFlags = surface.ReadByte();
            Require(surfaceId == 0 && surfaceFlags <= 1, "SURFACE");
            if (surfaceFlags == 0) End(surface);
            using var nav = Section(sections, 7);
            Require(nav.ReadByte() == 1, "NAV_TYPE");
            uint width = nav.ReadUInt32(), depth = nav.ReadUInt32();
            Require(width > 0 && width <= MaxSide && depth > 0 && depth <= MaxSide, "CAPACITY");
            Require(nav.ReadByte() == 1 && nav.ReadByte() == 0, "NAV_PROFILE");
            uint length = nav.ReadUInt32();
            Require(length == (width * depth + 7) / 8 && length == nav.BaseStream.Length - nav.BaseStream.Position, "BITMAP_LENGTH");
            byte[] bitmap = nav.ReadBytes((int)length); End(nav);
            Require(heightPoints == null || (pointsX == width + 1 && pointsZ == depth + 1), "HEIGHT_SIZE");
            string[] surfaceNames = null;
            byte[] surfaceCells = null;
            if (surfaceFlags == 1)
            {
                int nameCount = surface.ReadByte();
                Require(nameCount >= 1 && nameCount <= MaxSurfaceNames, "SURFACE_COUNT");
                surfaceNames = new string[nameCount];
                for (int i = 0; i < nameCount; i++) surfaceNames[i] = Text(surface);
                surfaceCells = surface.ReadBytes((int)(width * depth));
                Require(surfaceCells.Length == width * depth, "SURFACE_LENGTH");
                End(surface);
            }
            using var directory = Section(sections, 10);
            Require(directory.ReadUInt32() == 1 && directory.ReadUInt64() == 0, "CHUNKS");
            SimVec3 origin = Vector(directory);
            Require(origin.Equals(new SimVec3(min.X, height, min.Z)), "CHUNK_ORIGIN");
            Require(directory.ReadUInt32() == 4, "REFERENCE");
            foreach (ushort id in new ushort[] { 3, 4, 6, 7 })
                Require(directory.ReadUInt16() == id && directory.ReadUInt32() == offsets[id], "REFERENCE");
            Require(directory.ReadUInt32() == 0, "DEPENDENCIES"); End(directory);
            var map = new WcMap(mapId, contentId, min, max, chunkSize, height, width, depth, layers, walls, bitmap,
                heightPoints, surfaceNames, surfaceCells);
            Validate(map);
            return map;
        }
    }
}
