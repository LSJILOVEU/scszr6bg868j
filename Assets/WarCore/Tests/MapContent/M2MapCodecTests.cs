using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M2MapCodecTests
    {
        [Test]
        public void GoldenVector_IsCompleteWriterOutputAndStableOnRepeat()
        {
            byte[] first = WcMapWriter.Write(M2MapFixture.Model());
            Assert.That(first, Is.EqualTo(M2MapFixture.GoldenBytes));
            Assert.That(WcMapWriter.Write(M2MapFixture.Model()), Is.EqualTo(first));
            Assert.That(WcMapWriter.Write(WcMapReader.Read(first)), Is.EqualTo(first));
            TestContext.Out.WriteLine("WCMap M2 golden vector (complete file):");
            for (int i = 0; i < first.Length; i += 16)
                TestContext.Out.WriteLine(string.Join(" ", first.Skip(i).Take(16).Select(v => v.ToString("X2"))));
        }

        [Test]
        public void RoundTrip_PreservesEveryModelField()
        {
            WcMap expected = M2MapFixture.Model();
            WcMap actual = WcMapReader.Read(WcMapWriter.Write(expected));
            Assert.That(actual.MapId, Is.EqualTo(expected.MapId));
            Assert.That(actual.ContentId, Is.EqualTo(expected.ContentId));
            Assert.That(actual.Min, Is.EqualTo(expected.Min));
            Assert.That(actual.Max, Is.EqualTo(expected.Max));
            Assert.That(actual.ChunkSize, Is.EqualTo(expected.ChunkSize));
            Assert.That(actual.CellSize, Is.EqualTo(expected.CellSize));
            Assert.That(actual.FlatHeight, Is.EqualTo(expected.FlatHeight));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Depth, Is.EqualTo(expected.Depth));
            Assert.That(actual.Cost, Is.EqualTo(expected.Cost));
            Assert.That(actual.SurfaceId, Is.EqualTo(expected.SurfaceId));
            Assert.That(actual.NavigationLayer, Is.EqualTo(expected.NavigationLayer));
            Assert.That(actual.Passability, Is.EqualTo(expected.Passability));
            Assert.That(actual.Layers.Count, Is.EqualTo(expected.Layers.Count));
            for (int i = 0; i < actual.Layers.Count; i++)
            {
                Assert.That(actual.Layers[i].Name, Is.EqualTo(expected.Layers[i].Name));
                Assert.That(actual.Layers[i].Bit, Is.EqualTo(expected.Layers[i].Bit));
                Assert.That(actual.LayerMask(actual.Layers[i].Name), Is.EqualTo(expected.Layers[i].Mask));
            }
            Assert.That(actual.Walls.Count, Is.EqualTo(expected.Walls.Count));
            for (int i = 0; i < actual.Walls.Count; i++)
            {
                WcMapWall a = actual.Walls[i], e = expected.Walls[i];
                Assert.That(a.Chunk, Is.EqualTo(e.Chunk)); Assert.That(a.Placement, Is.EqualTo(e.Placement));
                Assert.That(a.Key, Is.EqualTo(e.Key)); Assert.That(a.KindId, Is.EqualTo(e.KindId));
                Assert.That(a.LayerName, Is.EqualTo(e.LayerName)); Assert.That(a.LocalCenter, Is.EqualTo(e.LocalCenter));
                Assert.That(a.HalfX, Is.EqualTo(e.HalfX)); Assert.That(a.Height, Is.EqualTo(e.Height));
                Assert.That(a.HalfZ, Is.EqualTo(e.HalfZ));
            }
            for (int z = 0; z < actual.Depth; z++)
                for (int x = 0; x < actual.Width; x++) Assert.That(actual.IsPassable(x, z), Is.EqualTo(expected.IsPassable(x, z)));
        }

        [Test]
        public void PermutingWallAndLayerInput_DoesNotChangeBytes()
        {
            WcMap map = TwoWalls();
            WcMapLayer[] layers = { new WcMapLayer("walls", 1), new WcMapLayer("actors", 0) };
            byte[] first = WcMapWriter.Write(M2MapFixture.Copy(map, layers: layers));
            Array.Reverse(layers);
            Assert.That(WcMapWriter.Write(M2MapFixture.Copy(map, map.Walls.Reverse().ToArray(), layers)), Is.EqualTo(first));
        }

        [Test]
        public void EmptyWalls_RoundTripAsCountZeroAndAllCellsPassable()
        {
            WcMap map = WcMapReader.Read(WcMapWriter.Write(M2MapFixture.Model(false)));
            Assert.That(map.Walls, Is.Empty);
            Assert.That(map.Passability, Is.EqualTo(new byte[] { 255, 255, 255, 255 }));
        }

        [Test]
        public void EveryTruncatedPrefix_IsRejected()
        {
            byte[] bytes = M2MapFixture.GoldenBytes;
            for (int size = 0; size < bytes.Length; size++)
                Assert.Throws<InvalidDataException>(() => WcMapReader.Read(bytes.Take(size).ToArray()), "size=" + size);
        }

        [TestCase("version", "VERSION")]
        [TestCase("magic", "MAGIC")]
        [TestCase("length", "LENGTH")]
        [TestCase("fileHash", "FILE_HASH")]
        [TestCase("sectionHash", "SECTION_HASH")]
        [TestCase("unknownSection", "UNKNOWN_SECTION")]
        [TestCase("sectionOrder", "SECTION_ORDER")]
        [TestCase("sectionCount", "SECTION_COUNT")]
        public void EnvelopeCorruption_IsExplicitlyRejected(string kind, string code)
        {
            byte[] bytes = M2MapFixture.GoldenBytes;
            switch (kind)
            {
                case "version": bytes[4] = 99; break;
                case "magic": bytes[0] = 0; break;
                case "length": Put(bytes, 10, w => w.Write(uint.MaxValue)); break;
                case "fileHash": bytes[bytes.Length - 1] ^= 1; break;
                case "sectionHash": bytes[8 + 6 + 70] ^= 1; break;
                case "unknownSection": bytes[8] = 99; break;
                case "sectionOrder": bytes[8] = 2; break;
                case "sectionCount": bytes[6] = 12; break;
            }
            if (kind != "fileHash") FixWholeHash(bytes);
            Reject(bytes, code);
        }

        [TestCase("duplicateKey", "DUPLICATE_KEY")]
        [TestCase("reference", "REFERENCE")]
        [TestCase("coordinate", "BOUNDS")]
        [TestCase("navigation", "NAV_WALL_MISMATCH")]
        [TestCase("stringLength", "STRING_LENGTH")]
        [TestCase("utf8", "UTF8")]
        [TestCase("cellSize", "CELL_SIZE")]
        [TestCase("capacity", "CAPACITY")]
        [TestCase("yaw", "YAW")]
        [TestCase("shape", "SHAPE")]
        [TestCase("cost", "NAV_PROFILE")]
        [TestCase("navLayer", "NAV_PROFILE")]
        [TestCase("surface", "SURFACE")]
        [TestCase("chunk", "REFERENCE")]
        [TestCase("layerBit", "LAYERS")]
        [TestCase("directoryReference", "REFERENCE")]
        [TestCase("directoryOrigin", "CHUNK_ORIGIN")]
        [TestCase("gridAlignment", "GRID")]
        public void RehashedInvalidPayload_ReachesSemanticValidation(string kind, string code)
        {
            byte[][] payloads = Payloads(kind == "duplicateKey" ? WcMapWriter.Write(TwoWalls()) : M2MapFixture.GoldenBytes);
            switch (kind)
            {
                case "duplicateKey": Put(payloads[4], 4 + 86 + 8, w => w.Write(1UL)); break;
                case "reference": payloads[4][28] = (byte)'x'; break;
                case "coordinate": Put(payloads[4], 34, w => w.Write(long.MaxValue)); break;
                case "navigation": payloads[7][16] |= 16; break;
                case "stringLength": Put(payloads[1], 0, w => w.Write((ushort)256)); break;
                case "utf8": payloads[2][4] = 255; break;
                case "cellSize": Put(payloads[1], 62, w => w.Write(2 * SimFix.Scale)); break;
                case "capacity": Put(payloads[7], 1, w => w.Write(257U)); break;
                case "yaw": Put(payloads[4], 82, w => w.Write(1L)); break;
                case "shape": payloads[4][33] = 99; break;
                case "cost": payloads[7][9] = 0; break;
                case "navLayer": payloads[7][10] = 1; break;
                case "surface": payloads[6][0] = 1; break;
                case "chunk": Put(payloads[4], 4, w => w.Write(1UL)); break;
                case "layerBit": payloads[2][9] = 32; break;
                case "directoryReference": payloads[10][40] = 1; break;
                case "directoryOrigin": payloads[10][12] = 1; break;
                case "gridAlignment": payloads[4][34] = 1; break;
            }
            Reject(Pack(payloads), code);
        }

        [TestCase(5)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(11)]
        public void NonemptyUnsupportedSection_IsRejected(int id)
        {
            byte[][] payloads = Payloads(M2MapFixture.GoldenBytes);
            payloads[id] = new byte[] { 0 };
            Reject(Pack(payloads), "UNSUPPORTED_SECTION");
        }

        [Test]
        public void Writer_RejectsDuplicateKeysAndInconsistentNavigation()
        {
            WcMap map = M2MapFixture.Model();
            Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(M2MapFixture.Copy(map, new[] { map.Walls[0], map.Walls[0] })));
            Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(M2MapFixture.Copy(map, bits: new byte[] { 255, 255, 255, 255 })));
        }

        [Test]
        public void Grid_NegativeOriginAndHalfOpenEdges()
        {
            WcMap source = M2MapFixture.Model();
            var shifted = new WcMap("shift", "c", SimVec3.Metres(-8, 0, -4), SimVec3.Metres(0, 4, 0),
                source.ChunkSize, default, 8, 4, source.Layers.ToArray(), source.Walls.ToArray(), source.Passability.ToArray());
            WcMap map = WcMapReader.Read(WcMapWriter.Write(shifted));
            Assert.That(map.TryCell(SimFix.FromRaw(-1), SimFix.FromRaw(-1), out int x, out int z), Is.True);
            Assert.That(x, Is.EqualTo(7)); Assert.That(z, Is.EqualTo(3));
            Assert.That(map.TryCell(map.Min.X, map.Min.Z, out x, out z), Is.True);
            Assert.That(x, Is.Zero); Assert.That(z, Is.Zero);
            Assert.That(map.TryCell(SimFix.FromInt(-7), SimFix.FromInt(-3), out x, out z), Is.True);
            Assert.That(x, Is.EqualTo(1)); Assert.That(z, Is.EqualTo(1));
            Assert.That(map.TryCell(default, SimFix.FromRaw(-1), out _, out _), Is.False);
            Assert.That(map.TryCell(SimFix.FromRaw(-1), default, out _, out _), Is.False);
            Assert.That(map.TryCell(SimFix.FromRaw(map.Min.X.Raw - 1), map.Min.Z, out _, out _), Is.False);
        }

        [Test]
        public void ContentAssembly_HasNoUnityDependency()
        {
            Assert.That(typeof(WcMapReader).Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void Utf8IdentifiersAndNonzeroFlatHeight_ArePreserved()
        {
            WcMap source = M2MapFixture.Model(false);
            var expected = new WcMap("m\u00e9", "c\u00f6", SimVec3.Metres(-8, 5, -4), SimVec3.Metres(0, 9, 0),
                source.ChunkSize, SimFix.FromInt(5), 8, 4, new[] { new WcMapLayer("w\u00e4lls", 31) },
                Array.Empty<WcMapWall>(), source.Passability.ToArray());
            WcMap actual = WcMapReader.Read(WcMapWriter.Write(expected));
            Assert.That(actual.MapId, Is.EqualTo(expected.MapId));
            Assert.That(actual.ContentId, Is.EqualTo(expected.ContentId));
            Assert.That(actual.Min, Is.EqualTo(expected.Min));
            Assert.That(actual.Max, Is.EqualTo(expected.Max));
            Assert.That(actual.FlatHeight.Raw, Is.EqualTo(5 * SimFix.Scale));
            Assert.That(actual.LayerMask("w\u00e4lls"), Is.EqualTo(0x80000000u));
        }

        [Test]
        public void UnusedBitmapBits_MustBeZero()
        {
            var map = new WcMap("m", "c", default, SimVec3.Metres(7, 4, 1), SimFix.FromInt(7), default, 7, 1,
                new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), new byte[] { 127 });
            byte[][] payloads = Payloads(WcMapWriter.Write(map));
            payloads[7][15] = 255;
            Reject(Pack(payloads), "NAV_WALL_MISMATCH");
        }

        private static WcMap TwoWalls()
        {
            WcMap map = M2MapFixture.Model();
            WcMapWall w = map.Walls[0];
            return M2MapFixture.Copy(map, new[] { w, new WcMapWall(0, 2, w.KindId, w.LayerName, w.LocalCenter, w.HalfX, w.Height, w.HalfZ) });
        }

        private static void Reject(byte[] bytes, string code)
            => Assert.That(Assert.Throws<InvalidDataException>(() => WcMapReader.Read(bytes)).Message, Is.EqualTo("WCMAP_" + code));

        private static void Put(byte[] bytes, int offset, Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream(bytes, true);
            stream.Position = offset;
            using var writer = new BinaryWriter(stream);
            write(writer);
        }

        private static byte[][] Payloads(byte[] bytes)
        {
            var result = new byte[12][];
            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.BaseStream.Position = 8;
            for (int i = 1; i <= 11; i++)
            {
                Assert.That(reader.ReadUInt16(), Is.EqualTo(i));
                result[i] = reader.ReadBytes((int)reader.ReadUInt32());
                reader.BaseStream.Position += 32;
            }
            return result;
        }

        // Independent envelope construction: corrupt payloads receive valid digests.
        private static byte[] Pack(byte[][] payloads)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            using var sha = SHA256.Create();
            writer.Write(new byte[] { 87, 67, 77, 80, 2, 0, 11, 0 });
            var offsets = new int[12];
            for (ushort id = 1; id <= 11; id++)
            {
                offsets[id] = (int)stream.Position;
                if (id == 10)
                {
                    int at = 42;
                    foreach (int reference in new[] { 3, 4, 6, 7 })
                    { Put(payloads[10], at, w => w.Write((uint)offsets[reference])); at += 6; }
                }
                writer.Write(id); writer.Write((uint)payloads[id].Length); writer.Write(payloads[id]);
                writer.Write(sha.ComputeHash(stream.ToArray(), offsets[id], payloads[id].Length + 6));
            }
            writer.Write(sha.ComputeHash(stream.ToArray()));
            return stream.ToArray();
        }

        private static void FixWholeHash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            Array.Copy(sha.ComputeHash(bytes, 0, bytes.Length - 32), 0, bytes, bytes.Length - 32, 32);
        }
    }
}
