using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // T1a-2a acceptance 9: a wall on a height field must stand at least 1 m above the highest grid point under its footprint
    // (both far edges included). Exactly 1 m is legal, 2 raw less is not. Reading is checked on independently built bytes.
    public sealed class Wp23WallTests
    {
        private const long S = Wp23.S;
        private const string Code = "WCMAP_WALL_TERRAIN";

        // 8 x 8 cells, Min.Y 0, Max.Y 6 m. Each peak (i, j, raw) is a pyramid falling by `fall` per grid step (Chebyshev),
        // gentle enough for the height field's slope limit; the peak itself is the highest point.
        private static WcMap Map((int x, int z, int w, int d)[] boxes, long wallRaw, params (int i, int j, long raw)[] peaks) =>
            MapWithFall(boxes, wallRaw, S / 2, peaks);

        private static WcMap MapWithFall((int x, int z, int w, int d)[] boxes, long wallRaw, long fall, params (int i, int j, long raw)[] peaks) =>
            Wp23.Make(8, 8, 0, 6 * S, boxes, wallRaw, (i, j) =>
            {
                long height = 0;
                foreach (var peak in peaks)
                    height = Math.Max(height, peak.raw - fall * Math.Max(Math.Abs(i - peak.i), Math.Abs(j - peak.j)));
                return (int)height;
            });

        private static void Accepts(WcMap map) => Assert.DoesNotThrow(() => WcMapReader.Read(WcMapWriter.Write(map)));
        private static void Rejects(WcMap map) =>
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(map)).Message, Is.EqualTo(Code));

        [Test]
        public void ExactlyOneMetreAbove_IsLegal_TwoRawLess_IsNot_OnTheFarCorner()
        {
            var box = new[] { (2, 2, 1, 1) };
            // The far corner (3,3) of the footprint is the highest point; the wall is 3 m, the point 2 m.
            Accepts(Map(box, 3 * S, (3, 3, 2 * S)));
            Rejects(Map(box, 3 * S - 2, (3, 3, 2 * S)));
            Accepts(Map(box, 3 * S + 2, (3, 3, 2 * S)));
        }

        [Test]
        public void EveryCornerOfTheFootprintCounts_ButPointsOutsideDoNot()
        {
            var box = new[] { (2, 2, 1, 1) };
            foreach (var (i, j) in new[] { (2, 2), (3, 2), (2, 3), (3, 3) })
            {
                Accepts(Map(box, 3 * S, (i, j, 2 * S)));
                Rejects(Map(box, 3 * S - 2, (i, j, 2 * S)));
            }
            // A peak just outside the footprint (grid point 4,2 and 1,3) does not matter.
            // Steep falloff keeps the footprint's own points low; the 3.4 m peaks would need a 4.4 m wall if they counted.
            Accepts(MapWithFall(box, 4 * S, 6 * S / 5, (4, 2, 34 * S / 10), (1, 3, 34 * S / 10)));
            Rejects(MapWithFall(box, 4 * S, 6 * S / 5, (3, 2, 34 * S / 10)));
        }

        [Test]
        public void MultiCellWalls_AndWallsOnTheMapBoundary()
        {
            var wide = new[] { (2, 2, 3, 1) };       // footprint grid points x 2..5, z 2..3
            Accepts(Map(wide, 3 * S, (5, 3, 2 * S)));
            Rejects(Map(wide, 3 * S - 2, (5, 3, 2 * S)));
            Accepts(MapWithFall(wide, 4 * S, 6 * S / 5, (6, 3, 34 * S / 10)));
            var edge = new[] { (7, 0, 1, 8) };       // against the east edge: grid points x 7..8
            Accepts(Map(edge, 3 * S, (8, 7, 2 * S)));
            Rejects(Map(edge, 3 * S - 2, (8, 7, 2 * S)));
        }

        [Test]
        public void FlatMapsAndMapsWithoutWalls_AreNotAffected()
        {
            Accepts(Wp23.Make(8, 8, 0, 6 * S, new[] { (2, 2, 1, 1) }, 2, null));
            Accepts(Wp23.Make(8, 8, 0, 6 * S, null, 2 * S, (i, j) => (int)(5 * S - Math.Abs(i - 4) * (S / 2))));
        }

        [Test]
        public void ReadingRejectsTheSameWalls_OnIndependentlyBuiltBytes()
        {
            var box = new[] { (2, 2, 1, 1) };
            byte[][] payloads = Payloads(WcMapWriter.Write(Map(box, 3 * S, (3, 3, 2 * S))));
            Assert.DoesNotThrow(() => WcMapReader.Read(Pack(payloads)), "the boundary wall reads");
            // Lower the wall by 2 raw (height and centre Y together, as the file contract ties them) and reseal.
            int patched = 0;
            foreach (byte[] payload in payloads)
            {
                if (payload == null) continue;
                patched += Replace(payload, BitConverter.GetBytes(3 * S), BitConverter.GetBytes(3 * S - 2));
                patched += Replace(payload, BitConverter.GetBytes(3 * S / 2), BitConverter.GetBytes((3 * S - 2) / 2));
            }
            Assert.That(patched, Is.EqualTo(2), "height and centre Y found once each");
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapReader.Read(Pack(payloads))).Message, Is.EqualTo(Code));
        }

        // ---- envelope helpers (independent of the writer, as in M2MapCodecTests) ----

        private static int Replace(byte[] payload, byte[] from, byte[] to)
        {
            int found = 0;
            for (int at = 0; at + from.Length <= payload.Length; at++)
            {
                bool same = true;
                for (int k = 0; k < from.Length && same; k++) same = payload[at + k] == from[k];
                if (!same) continue;
                Array.Copy(to, 0, payload, at, to.Length); found++; at += from.Length - 1;
            }
            return found;
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
                    {
                        byte[] value = BitConverter.GetBytes((uint)offsets[reference]);
                        Array.Copy(value, 0, payloads[10], at, 4); at += 6;
                    }
                }
                writer.Write(id); writer.Write((uint)payloads[id].Length); writer.Write(payloads[id]);
                writer.Write(sha.ComputeHash(stream.ToArray(), offsets[id], payloads[id].Length + 6));
            }
            writer.Write(sha.ComputeHash(stream.ToArray()));
            return stream.ToArray();
        }
    }
}
