using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp22Envelope;

namespace WarCore.Tests
{
    public sealed class Wp22FormatTests
    {
        private const long S = SimFix.Scale;

        // 16 x 16 with a gentle hill and two surfaces: small enough to flip every byte.
        private static WcMap Small(Func<int, int, int> height = null, byte[] surfaces = null, string[] names = null, bool withHeights = true, bool withSurfaces = true)
        {
            const int n = 16;
            int[] pts = null;
            if (withHeights)
            {
                pts = new int[(n + 1) * (n + 1)];
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++) pts[j * (n + 1) + i] = height != null ? height(i, j) : (i + j) * (int)(S / 8);
            }
            var cells = surfaces ?? new byte[n * n];
            if (surfaces == null) for (int k = 0; k < cells.Length; k++) cells[k] = (byte)(k % 5 == 0 ? 1 : 0);
            var bits = new byte[n * n / 8];
            for (int k = 0; k < bits.Length; k++) bits[k] = 255;
            return new WcMap("small", "c", default, SimVec3.Metres(n, 12, n), SimFix.FromInt(n), default, n, n,
                new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), bits,
                pts, withSurfaces ? (names ?? new[] { "grass", "mud" }) : null, withSurfaces ? cells : null);
        }

        [Test]
        public void DemoMap_RoundTripsAndIsDeterministic()
        {
            WcMap demo = TerrainDemoMap.Build();
            byte[] first = WcMapWriter.Write(demo), second = WcMapWriter.Write(demo);
            Assert.That(second, Is.EqualTo(first));
            WcMap back = WcMapReader.Read(first);
            Assert.That(back.HeightPoints, Is.EqualTo(demo.HeightPoints));
            Assert.That(back.SurfaceNames, Is.EqualTo(demo.SurfaceNames));
            Assert.That(back.SurfaceCells, Is.EqualTo(demo.SurfaceCells));
            Assert.That(back.Passability, Is.EqualTo(demo.Passability));
            Assert.That(back.Walls.Count, Is.EqualTo(1));
            Assert.That(WcMapWriter.Write(back), Is.EqualTo(first));
            Assert.That(first.Length, Is.LessThan(200 * 1024));
        }

        [Test]
        public void FlatMap_BytesAreUnchanged()
        {
            Assert.That(WcMapWriter.Write(M2MapFixture.Model()), Is.EqualTo(M2MapFixture.GoldenBytes));
            WcMap read = WcMapReader.Read(M2MapFixture.GoldenBytes);
            Assert.That(read.HasHeightField, Is.False); Assert.That(read.HasSurfaceTable, Is.False); Assert.That(read.HasTerrain, Is.False);
        }

        // The compatibility matrix: the new reader accepts all four combinations and writes them back unchanged.
        // The profile bytes the old reader demands to be zero are non-zero exactly when the new content is present.
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ProfileMatrix(bool heights, bool surfaces)
        {
            WcMap map = Small(withHeights: heights, withSurfaces: surfaces);
            byte[] bytes = WcMapWriter.Write(map);
            WcMap back = WcMapReader.Read(bytes);
            Assert.That(back.HasHeightField, Is.EqualTo(heights)); Assert.That(back.HasSurfaceTable, Is.EqualTo(surfaces));
            Assert.That(WcMapWriter.Write(back), Is.EqualTo(bytes));
            byte[][] payloads = Payloads(bytes);
            Assert.That(payloads[3][16], Is.EqualTo(heights ? 1 : 0)); Assert.That(payloads[3][17], Is.Zero);
            Assert.That(payloads[6][0], Is.Zero); Assert.That(payloads[6][1], Is.EqualTo(surfaces ? 1 : 0));
        }

        [Test]
        public void FlatHeightWithSurfaceTable_IsNotDowngraded()
        {
            WcMap back = WcMapReader.Read(WcMapWriter.Write(Small(withHeights: false)));
            Assert.That(back.HasSurfaceTable, Is.True);
            Assert.That(back.SurfaceCells.Count, Is.EqualTo(256));
        }

        [TestCase(0x01)]
        [TestCase(0xFF)]
        public void EveryByteFlipped_IsRejected_WithoutRehashing(int mask)
        {
            byte[] original = WcMapWriter.Write(Small());
            Assert.That(original.Length, Is.LessThan(20 * 1024));
            for (int i = 0; i < original.Length; i++)
            {
                byte[] copy = (byte[])original.Clone();
                copy[i] ^= (byte)mask;
                Assert.Throws<InvalidDataException>(() => WcMapReader.Read(copy), "byte " + i);
            }
        }

        // ---- semantic validation after re-hashing ----

        private static byte[][] DemoPayloads() => Payloads(WcMapWriter.Write(TerrainDemoMap.Build()));
        // payload[3]: chunk(8) height(8) diagonal(1) flags(1) pointsX(2) pointsZ(2) then Int32 points.
        private const int PointsAt = 22;
        // payload[6]: id(1) flags(1) count(1) then names (u16 length + bytes) then cells.

        [Test]
        public void ProfileValues_AreExact()
        {
            byte[][] p = DemoPayloads(); p[3][16] = 2; Reject(Pack(p), "HEIGHT_PROFILE");
            p = DemoPayloads(); p[3][17] = 1; Reject(Pack(p), "HEIGHT_PROFILE");
            p = DemoPayloads(); p[6][0] = 1; Reject(Pack(p), "SURFACE");
            p = DemoPayloads(); p[6][1] = 2; Reject(Pack(p), "SURFACE");
        }

        [Test]
        public void HeightPointRange()
        {
            // Max.Y of the demo is 10 m; |h| must stay below 2^29 and inside [Min.Y, Max.Y].
            byte[][] p = DemoPayloads(); Put(p[3], PointsAt, w => w.Write(-1)); Reject(Pack(p), "HEIGHT_RANGE");
            p = DemoPayloads(); Put(p[3], PointsAt, w => w.Write((int)(10 * S) + 1)); Reject(Pack(p), "HEIGHT_RANGE");
            p = DemoPayloads(); Put(p[3], PointsAt, w => w.Write(1 << 29)); Reject(Pack(p), "HEIGHT_RANGE");
        }

        [Test]
        public void HeightPointLimit_Is2Pow29MinusOne()
        {
            int top = (1 << 29) - 1;
            WcMap Flat(int h) => new WcMap("tall", "c", SimVec3.Metres(0, 0, 0), new SimVec3(SimFix.FromInt(1), SimFix.FromRaw(1L << 30), SimFix.FromInt(1)),
                SimFix.FromInt(1), default, 1, 1, new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), new byte[] { 1 }, new[] { h, h, h, h }, null, null);
            Assert.DoesNotThrow(() => WcMapReader.Read(WcMapWriter.Write(Flat(top))));
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(Flat(top + 1))).Message, Is.EqualTo("WCMAP_HEIGHT_RANGE"));
        }

        [Test]
        public void HeightPoint_EqualToMaxY_IsLegal()
        {
            WcMap AtTop(int h) => new WcMap("top", "c", SimVec3.Metres(0, 0, 0), SimVec3.Metres(1, 2, 1), SimFix.FromInt(1), default, 1, 1,
                new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), new byte[] { 1 }, new[] { h, h, h, h }, null, null);
            Assert.DoesNotThrow(() => WcMapReader.Read(WcMapWriter.Write(AtTop((int)(2 * S)))));
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(AtTop((int)(2 * S) + 1))).Message, Is.EqualTo("WCMAP_HEIGHT_RANGE"));
            Assert.DoesNotThrow(() => WcMapReader.Read(WcMapWriter.Write(AtTop(0))));
        }

        private static WcMap Cell(int h00, int h10, int h01, int h11) => new WcMap("cell", "c", SimVec3.Metres(0, 0, 0), SimVec3.Metres(1, 10, 1),
            SimFix.FromInt(1), default, 1, 1, new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), new byte[] { 1 }, new[] { h00, h10, h01, h11 }, null, null);

        [Test]
        public void Slope_AtMaxSlopeTan_Passes_OneRawOver_IsRejected_BothWays()
        {
            int two = (int)(2 * S);
            Assert.DoesNotThrow(() => WcMapReader.Read(WcMapWriter.Write(Cell(0, two, 0, two))));
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(Cell(0, two + 1, 0, two))).Message, Is.EqualTo("WCMAP_HEIGHT_SLOPE"));
            // The plane that is steeper than either axial edge: a = b = 2 m.
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(Cell(0, two, two, 2 * two))).Message, Is.EqualTo("WCMAP_HEIGHT_SLOPE"));
            // Reader side: bake a legal file, then raise one point and re-hash.
            byte[][] p = Payloads(WcMapWriter.Write(Cell(0, two, 0, two)));
            Put(p[3], PointsAt + 4, w => w.Write(two + 1));
            Reject(Pack(p), "HEIGHT_SLOPE");
        }

        [Test]
        public void HeightSize_MustMatchTheNavigationGrid()
        {
            // Writer: a point list one column too long.
            var bad = new WcMap("cell", "c", SimVec3.Metres(0, 0, 0), SimVec3.Metres(1, 10, 1), SimFix.FromInt(1), default, 1, 1,
                new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), new byte[] { 1 }, new int[6], null, null);
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapWriter.Write(bad)).Message, Is.EqualTo("WCMAP_HEIGHT_SIZE"));
            // Reader: a self-consistent 3 x 2 point block on a 1 x 1 grid.
            byte[][] p = Payloads(WcMapWriter.Write(Cell(0, 0, 0, 0)));
            using (var s = new MemoryStream())
            using (var w = new BinaryWriter(s))
            {
                w.Write(p[3], 0, 18); w.Write((ushort)3); w.Write((ushort)2);
                for (int i = 0; i < 6; i++) w.Write(0);
                p[3] = s.ToArray();
            }
            Reject(Pack(p), "HEIGHT_SIZE");
        }

        [Test]
        public void SurfaceTable_Rules()
        {
            Func<string[], byte[], WcMap> make = (names, cells) => Small(names: names, surfaces: cells);
            string Code(Action a) => Assert.Throws<InvalidDataException>(() => a()).Message;
            Assert.That(Code(() => WcMapWriter.Write(make(new[] { "mud", "grass" }, null))), Is.EqualTo("WCMAP_SURFACE_ORDER"));
            Assert.That(Code(() => WcMapWriter.Write(make(new[] { "a", "a" }, null))), Is.EqualTo("WCMAP_SURFACE_ORDER"));
            // Ordinal, case sensitive: "B" < "a".
            Assert.DoesNotThrow(() => WcMapWriter.Write(make(new[] { "B", "a" }, null)));
            Assert.That(Code(() => WcMapWriter.Write(make(new[] { "a", "B" }, null))), Is.EqualTo("WCMAP_SURFACE_ORDER"));
            Assert.That(Code(() => WcMapWriter.Write(make(Enumerable.Range(0, 65).Select(i => "s" + i.ToString("D3")).ToArray(), null))), Is.EqualTo("WCMAP_SURFACE_COUNT"));
            Assert.That(Code(() => WcMapWriter.Write(make(new string[0], null))), Is.EqualTo("WCMAP_SURFACE_COUNT"));
            Assert.DoesNotThrow(() => WcMapWriter.Write(make(Enumerable.Range(0, 64).Select(i => "s" + i.ToString("D3")).ToArray(), null)));
            Assert.That(Code(() => WcMapWriter.Write(make(null, new byte[255]))), Is.EqualTo("WCMAP_SURFACE_LENGTH"));
            var indexTooHigh = new byte[256]; indexTooHigh[3] = 2;
            Assert.That(Code(() => WcMapWriter.Write(make(null, indexTooHigh))), Is.EqualTo("WCMAP_SURFACE_INDEX"));
            Assert.That(Code(() => WcMapWriter.Write(make(new[] { "grass", "" }, null))), Is.EqualTo("WCMAP_STRING"));
        }

        [Test]
        public void SurfaceTable_ReaderSemantics()
        {
            // Demo table: grass, mud, road; payload[6] = id flags count, then 3 names, then 48 * 32 cells.
            byte[][] p = DemoPayloads(); p[6][p[6].Length - 1] = 3; Reject(Pack(p), "SURFACE_INDEX");
            p = DemoPayloads(); p[6][2] = 0; Reject(Pack(p), "SURFACE_COUNT");
            p = DemoPayloads(); p[6][2] = 65; Reject(Pack(p), "SURFACE_COUNT");
            p = DemoPayloads(); p[6][5] = (byte)'z'; Reject(Pack(p), "SURFACE_ORDER");      // "zrass" sorts after "mud"
            p = DemoPayloads(); p[6] = p[6].Take(p[6].Length - 1).ToArray(); Reject(Pack(p), "SURFACE_LENGTH");
            p = DemoPayloads(); p[6] = p[6].Concat(new byte[] { 0 }).ToArray(); Reject(Pack(p), "PAYLOAD_LENGTH");
        }

        [Test]
        public void ViewMap_ExposesRawHeightsAndSurfaces()
        {
            WcMap demo = TerrainDemoMap.Build();
            ViewMap view = ViewMap.Parse(WcMapWriter.Write(demo));
            Assert.That(view.HasHeightField, Is.True); Assert.That(view.HasSurfaceTable, Is.True);
            for (int j = 0; j <= demo.Depth; j++)
                for (int i = 0; i <= demo.Width; i++) Assert.That(view.VertexRaw(i, j), Is.EqualTo(demo.VertexRaw(i, j)));
            Assert.That(view.SurfaceCount, Is.EqualTo(3));
            Assert.That(view.SurfaceName(2), Is.EqualTo("road"));
            Assert.That(view.SurfaceIndex(30, 15), Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => ViewMap.Parse(M2MapFixture.GoldenBytes).VertexRaw(0, 0));
        }
    }
}
