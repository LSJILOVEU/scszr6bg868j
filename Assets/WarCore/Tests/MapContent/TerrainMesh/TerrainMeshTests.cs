using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L7: the render mesh of a height field matches the Sim HeightField point order, diagonal and integer samples.
    public sealed class TerrainMeshTests
    {
        private const long S = SimFix.Scale;

        // (2) Every triangle sampled once at a point strictly inside it (A: (u, v) = (5/8, 1/8), B: (1/8, 5/8)); both points
        // are also strictly inside one triangle of the anti-diagonal cut, so a mis-cut mesh interpolates the other plane.
        // The mesh y is interpolated in double from the mesh's own three vertices; floor(y * 65536) must equal the
        // HeightField sample at the same point. Exact while |raw| < 2^24: vertex y is raw / 2^16 exactly, the barycentric
        // weights are multiples of 1/8 (integer vertex x and z, so the denominator is +-1), and every product and sum is a
        // multiple of 2^-19 below 2^27 in magnitude, which a double holds exactly.
        internal static (int Samples, int Diffs) SampleAgainstHeightField(byte[] bytes)
        {
            WcMap map = WcMapReader.Read(bytes);
            var points = new int[map.HeightPoints.Count];
            for (int i = 0; i < points.Length; i++) points[i] = map.HeightPoints[i];
            int w = (int)map.Width, d = (int)map.Depth;
            var field = new HeightField(w, d, map.Min.X, map.Min.Z, points);
            TerrainMesh mesh = TerrainMesh.Build(ViewMap.Parse(bytes));
            Assert.That(mesh.Triangles.Count, Is.EqualTo(6 * w * d));
            int samples = 0, diffs = 0;
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                    for (int half = 0; half < 2; half++)
                    {
                        long u = half == 0 ? 5 * S / 8 : S / 8, v = half == 0 ? S / 8 : 5 * S / 8;
                        int t = 3 * (2 * (z * w + x) + half);
                        long rx = map.Min.X.Raw + x * S + u, rz = map.Min.Z.Raw + z * S + v;
                        double y = MeshY(mesh, t, rx / (double)S, rz / (double)S);
                        Assert.That(field.TryHeightAt(SimFix.FromRaw(rx), SimFix.FromRaw(rz), out SimFix h), Is.True);
                        samples++;
                        if (Math.Floor(y * S) != h.Raw) diffs++;
                    }
            return (samples, diffs);
        }

        // Barycentric interpolation of triangle t (index into Triangles) at (px, pz), in double.
        internal static double MeshY(TerrainMesh mesh, int t, double px, double pz)
        {
            IReadOnlyList<float> p = mesh.Positions;
            int a = 3 * mesh.Triangles[t], b = 3 * mesh.Triangles[t + 1], c = 3 * mesh.Triangles[t + 2];
            double x0 = p[a], z0 = p[a + 2], x1 = p[b], z1 = p[b + 2], x2 = p[c], z2 = p[c + 2];
            double den = (z1 - z2) * (x0 - x2) + (x2 - x1) * (z0 - z2);
            double l0 = ((z1 - z2) * (px - x2) + (x2 - x1) * (pz - z2)) / den;
            double l1 = ((z2 - z0) * (px - x2) + (x0 - x2) * (pz - z2)) / den;
            double l2 = 1.0 - l0 - l1;
            return l0 * p[a + 1] + l1 * p[b + 1] + l2 * p[c + 1];
        }

        // (1) Hand fixture: 3 x 2 cells, Min = (-2, -1, 3), Max = (1, 1, 5). Point heights raw, row j, columns i = 0..3.
        // Cell (0,0) is a saddle: h00 + h11 - h10 - h01 = 0 + 32768 + 16384 + 16384 = 65536 raw. Cell (2,0) has h00 = -3.
        internal static readonly int[] FixtureRaw =
        {
            0, -16384, -3, 0,
            -16384, 32768, 0, 0,
            -65536, 0, 16384, 32768,
        };

        internal static byte[] FixtureBytes()
        {
            const int w = 3, d = 2;
            var bits = new byte[(w * d + 7) / 8];
            for (int key = 0; key < w * d; key++) bits[key >> 3] |= (byte)(1 << (key & 7));
            SimFix floor = SimFix.FromInt(-1);
            return WcMapWriter.Write(new WcMap("l7-fixture", "none", new SimVec3(SimFix.FromInt(-2), floor, SimFix.FromInt(3)),
                new SimVec3(SimFix.FromInt(1), SimFix.FromInt(1), SimFix.FromInt(5)), SimFix.FromInt(3), floor,
                w, d, new[] { new WcMapLayer("walls", 1) }, new WcMapWall[0], bits, FixtureRaw, null, null));
        }

        [Test]
        public void Fixture_TrianglesAndVertices_AreExact()
        {
            TerrainMesh mesh = TerrainMesh.Build(ViewMap.Parse(FixtureBytes()));
            Assert.That(mesh.VertexCount, Is.EqualTo(12));
            Assert.That(mesh.Triangles.Count, Is.EqualTo(36));
            int[] expected = { 0, 5, 1, 0, 4, 5, 1, 6, 2, 1, 5, 6, 2, 7, 3, 2, 6, 7, 4, 9, 5, 4, 8, 9, 5, 10, 6, 5, 9, 10, 6, 11, 7, 6, 10, 11 };
            for (int k = 0; k < expected.Length; k++) Assert.That(mesh.Triangles[k], Is.EqualTo(expected[k]), "index " + k);
            for (int j = 0; j <= 2; j++)
                for (int i = 0; i <= 3; i++)
                {
                    int p = 3 * (j * 4 + i);
                    Assert.That(Bits(mesh.Positions[p]), Is.EqualTo(Bits(-2 + i)), $"x ({i},{j})");
                    Assert.That(Bits(mesh.Positions[p + 1]), Is.EqualTo(Bits((float)FixtureRaw[j * 4 + i] / 65536f)), $"y ({i},{j})");
                    Assert.That(Bits(mesh.Positions[p + 2]), Is.EqualTo(Bits(3 + j)), $"z ({i},{j})");
                }
        }

        [Test]
        public void Fixture_EveryTriangleSample_MatchesHeightField()
        {
            (int samples, int diffs) = SampleAgainstHeightField(FixtureBytes());
            Assert.That(samples, Is.EqualTo(12));
            Assert.That(diffs, Is.EqualTo(0));
        }

        // Four hand-worked samples. Cell (0,0), (h00, h10, h01, h11) = (0, -16384, -16384, 32768):
        //   A at (5/8, 1/8): h00 + (h10 - h00) * 5/8 + (h11 - h10) / 8 = -10240 + 6144 = -4096
        //   B at (1/8, 5/8): h00 + (h11 - h01) / 8 + (h01 - h00) * 5/8 = 6144 - 10240 = -4096
        //   the anti-diagonal cut puts both points in triangle (v00, v10, v01): h00 + (h10 - h00) u + (h01 - h00) v
        //   = -10240 - 2048 = -12288 at A and -2048 - 10240 = -12288 at B; the difference is 65536 / 8 = 8192 raw.
        // Cell (2,0), (h00, h10, h01, h11) = (-3, 0, 0, 0): A = -3 + 3 * 5/8 = -1.125, B = -3 + 3 * 5/8 = -1.125;
        //   floor gives -2 (truncation would give -1), and HeightField floors toward negative infinity too; the other cut
        //   gives -3 + 3 * 5/8 + 3 / 8 = -0.75, floor -1, at both points.
        [TestCase(0, 0, -4096, -12288)]
        [TestCase(0, 1, -4096, -12288)]
        [TestCase(2, 0, -2, -1)]
        [TestCase(2, 1, -2, -1)]
        public void Fixture_HandWorkedSamples(int cellX, int half, long expectedRaw, long antiDiagonalRaw)
        {
            byte[] bytes = FixtureBytes();
            TerrainMesh mesh = TerrainMesh.Build(ViewMap.Parse(bytes));
            WcMap map = WcMapReader.Read(bytes);
            var field = new HeightField(3, 2, map.Min.X, map.Min.Z, FixtureRaw);
            double u = half == 0 ? 0.625 : 0.125, v = half == 0 ? 0.125 : 0.625;
            double y = MeshY(mesh, 3 * (2 * cellX + half), -2 + cellX + u, 3 + v);
            Assert.That(Math.Floor(y * S), Is.EqualTo(expectedRaw));
            Assert.That(field.TryHeightAt(SimFix.FromRaw(map.Min.X.Raw + cellX * S + (long)(u * S)),
                SimFix.FromRaw(map.Min.Z.Raw + (long)(v * S)), out SimFix h), Is.True);
            Assert.That(h.Raw, Is.EqualTo(expectedRaw));
            // The same point on the other cut, from the mesh's own corner heights: (v00, v10, v01) since u + v < 1.
            IReadOnlyList<float> p = mesh.Positions;
            double h00 = p[3 * cellX + 1], h10 = p[3 * (cellX + 1) + 1], h01 = p[3 * (cellX + 4) + 1];
            Assert.That(Math.Floor((h00 + (h10 - h00) * u + (h01 - h00) * v) * S), Is.EqualTo(antiDiagonalRaw));
        }

        [Test]
        public void Checker512_EveryTriangleSample_MatchesHeightField()
        {
            (int samples, int diffs) = SampleAgainstHeightField(TerrainDemo.CheckerBytes(512));
            Assert.That(samples, Is.EqualTo(524288));
            Assert.That(diffs, Is.EqualTo(0));
        }

        [Test]
        public void Fixture_VertexCoordinates_AreBitExact() => AssertVertexBits(FixtureBytes());

        [Test]
        public void Fixture_EveryTriangleFacesUp() => AssertFacesUp(FixtureBytes());

        [Test]
        public void DemoTerrain_EveryTriangleSample_MatchesHeightField()
        {
            (int samples, int diffs) = SampleAgainstHeightField(TerrainDemo.MapBytes());
            Assert.That(samples, Is.EqualTo(3072));
            Assert.That(diffs, Is.EqualTo(0));
        }

        // (3) Y is the int raw converted to float once; X and Z are MinX / MinZ plus the integer index.
        [Test]
        public void DemoTerrain_VertexCoordinates_AreBitExact() => AssertVertexBits(TerrainDemo.MapBytes());

        internal static void AssertVertexBits(byte[] bytes)
        {
            ViewMap map = ViewMap.Parse(bytes);
            TerrainMesh mesh = TerrainMesh.Build(map);
            Assert.That(mesh.VertexCount, Is.EqualTo((map.Width + 1) * (map.Depth + 1)));
            Assert.That(mesh.Positions.Count, Is.EqualTo(3 * mesh.VertexCount));
            int bad = 0, halfMetres = 0;
            for (int j = 0; j <= map.Depth; j++)
                for (int i = 0; i <= map.Width; i++)
                {
                    int p = 3 * (j * (map.Width + 1) + i);
                    int raw = map.VertexRaw(i, j);
                    if (raw % 65536 != 0) halfMetres++;
                    if (Bits(mesh.Positions[p]) != Bits(map.MinX + i)
                        || Bits(mesh.Positions[p + 1]) != Bits((float)raw / 65536f)
                        || Bits(mesh.Positions[p + 2]) != Bits(map.MinZ + j)) bad++;
                }
            Assert.That(halfMetres, Is.GreaterThan(0), "fixture has heights that are not whole metres");
            Assert.That(bad, Is.EqualTo(0));
        }

        // (4) Every triangle faces up: cross(v1 - v0, v2 - v0).y > 0.
        [Test]
        public void DemoTerrain_EveryTriangleFacesUp() => AssertFacesUp(TerrainDemo.MapBytes());

        [Test]
        public void FlatHeightField_EveryTriangleFacesUp() => AssertFacesUp(FlatHeightFieldBytes());

        internal static void AssertFacesUp(byte[] bytes)
        {
            TerrainMesh mesh = TerrainMesh.Build(ViewMap.Parse(bytes));
            IReadOnlyList<float> p = mesh.Positions;
            int down = 0;
            for (int t = 0; t < mesh.Triangles.Count; t += 3)
            {
                int a = 3 * mesh.Triangles[t], b = 3 * mesh.Triangles[t + 1], c = 3 * mesh.Triangles[t + 2];
                double ax = p[b] - p[a], az = p[b + 2] - p[a + 2], bx = p[c] - p[a], bz = p[c + 2] - p[a + 2];
                if (!(az * bx - ax * bz > 0)) down++;
            }
            Assert.That(mesh.Triangles.Count, Is.EqualTo(6 * mesh.Width * mesh.Depth));
            Assert.That(down, Is.EqualTo(0));
        }

        // (5) A map without a height field has no terrain mesh.
        [Test]
        public void MapWithoutHeightField_Throws()
        {
            ViewMap map = ViewMap.Parse(M7DemoMap.Bytes());
            Assert.That(map.HasHeightField, Is.False);
            Assert.Throws<InvalidOperationException>(() => TerrainMesh.Build(map));
        }

        // (6) The demo bytes read back as the 48 x 32 terrain demo.
        [Test]
        public void DemoBytes_ReadBackAsTheTerrainDemo()
        {
            ViewMap map = ViewMap.Parse(TerrainDemo.MapBytes());
            Assert.That(map.MapId, Is.EqualTo("terrain-demo"));
            Assert.That(map.Width, Is.EqualTo(48));
            Assert.That(map.Depth, Is.EqualTo(32));
            Assert.That(map.HasHeightField, Is.True);
        }

        // 4 x 3 cells, every height point 0, origin (5, -7): a height field that is flat.
        private static byte[] FlatHeightFieldBytes()
        {
            const int w = 4, d = 3;
            var bits = new byte[(w * d + 7) / 8];
            for (int key = 0; key < w * d; key++) bits[key >> 3] |= (byte)(1 << (key & 7));
            return WcMapWriter.Write(new WcMap("flat-terrain", "none", new SimVec3(SimFix.FromInt(5), default, SimFix.FromInt(-7)),
                new SimVec3(SimFix.FromInt(5 + w), SimFix.FromInt(1), SimFix.FromInt(-7 + d)), SimFix.FromInt(w), default,
                w, d, new[] { new WcMapLayer("walls", 1) }, new WcMapWall[0], bits, new int[(w + 1) * (d + 1)], null, null));
        }

        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
    }
}
