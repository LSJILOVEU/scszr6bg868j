using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ML-10 acceptance 7 (headless half): the render mesh of a plate and its comparison with a drawn mesh.
    public sealed class Ml10MeshTests
    {
        private static readonly ViewPlates Plates = ViewPlates.From(WcMapReader.Read(PlateDemo.MapBytes()));

        // The floor: 20 solid cells (6 x 4 less the 4 cut over the stair); 22 open edges by hand (south 6, west 4, east 4,
        // north 2, round the cutout 4 + 1 + 1). The first solid cell's top comes first, in the documented order.
        [Test]
        public void TheFloorMeshByHand()
        {
            PlateMesh mesh = PlateMesh.Build(Plates.OfLayer(2));
            Assert.That((mesh.Layer, mesh.SolidCells, mesh.VertexCount, mesh.Triangles.Count), Is.EqualTo((2, 20, (20 + 20 + 22) * 4, (20 + 20 + 22) * 6)));
            Assert.That(mesh.Positions.Take(12), Is.EqualTo(new float[] { 9, 3, 6, 9, 3, 7, 10, 3, 7, 10, 3, 6 }));
            Assert.That(mesh.Positions.Skip(20 * 12).Take(12), Is.EqualTo(new float[] { 9, 2.5f, 6, 10, 2.5f, 6, 10, 2.5f, 7, 9, 2.5f, 7 }), "the first bottom");
            PlateMesh stair = PlateMesh.Build(Plates.OfLayer(1));
            Assert.That((stair.SolidCells, stair.VertexCount), Is.EqualTo((4, (4 + 4 + 10) * 4)));
            Assert.That(stair.Positions.Take(12), Is.EqualTo(new float[] { 10, 0, 9, 10, 0, 10, 11, 0.75f, 10, 11, 0.75f, 9 }));
        }

        // Every triangle faces out: a step back along its normal from its centroid is inside the plate's slab, a step forward
        // outside; tops face up and bottoms down.
        [Test]
        public void EveryFaceFacesOut()
        {
            for (int layer = 1; layer <= 2; layer++)
            {
                ViewPlate p = Plates.OfLayer(layer);
                PlateMesh mesh = PlateMesh.Build(p);
                for (int t = 0; t < mesh.Triangles.Count / 3; t++)
                {
                    var v = new double[3][];
                    for (int k = 0; k < 3; k++)
                    {
                        int i = mesh.Triangles[3 * t + k];
                        v[k] = new double[] { mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2] };
                    }
                    double ux = v[1][0] - v[0][0], uy = v[1][1] - v[0][1], uz = v[1][2] - v[0][2];
                    double wx = v[2][0] - v[0][0], wy = v[2][1] - v[0][1], wz = v[2][2] - v[0][2];
                    double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx, len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    Assert.That(len, Is.GreaterThan(0), "layer " + layer + " triangle " + t);
                    double cx = (v[0][0] + v[1][0] + v[2][0]) / 3, cy = (v[0][1] + v[1][1] + v[2][1]) / 3, cz = (v[0][2] + v[1][2] + v[2][2]) / 3;
                    const double step = 0.01;
                    Assert.That(Inside(p, cx - nx / len * step, cy - ny / len * step, cz - nz / len * step), Is.True, "behind, layer " + layer + " triangle " + t);
                    Assert.That(Inside(p, cx + nx / len * step, cy + ny / len * step, cz + nz / len * step), Is.False, "in front, layer " + layer + " triangle " + t);
                    if (t < 2 * mesh.SolidCells) Assert.That(ny, Is.GreaterThan(0), "a top");
                    else if (t < 4 * mesh.SolidCells) Assert.That(ny, Is.LessThan(0), "a bottom");
                }
            }
        }

        [Test]
        public void TheComparisonNamesTheFirstProblem()
        {
            PlateMesh mesh = PlateMesh.Build(Plates.OfLayer(2));
            float[] drawn = mesh.Positions.ToArray();
            Assert.That(mesh.Compare(drawn), Is.EqualTo(""));
            drawn[1] += 0.0009f;
            Assert.That(mesh.Compare(drawn), Is.EqualTo(""), "within 1e-3");
            drawn[1] += 0.0011f;
            Assert.That(mesh.Compare(drawn), Is.EqualTo("plate 2: vertex 0 (top of solid cell 0) is off"));
            drawn = mesh.Positions.ToArray();
            drawn[3 * 80] += 0.5f;
            Assert.That(mesh.Compare(drawn), Is.EqualTo("plate 2: vertex 80 is off"), "the first bottom vertex is not a top");
            Assert.That(mesh.Compare(drawn.Take(drawn.Length - 3).ToArray()), Is.EqualTo("plate 2: 247 vertices drawn, 248 expected"));
            Assert.That(mesh.Compare(null), Is.EqualTo("plate 2: no mesh drawn"));
        }

        // Strictly inside a solid cell and strictly between the cell's bottom and top planes.
        private static bool Inside(ViewPlate p, double x, double y, double z)
        {
            double u = x - p.MinX, v = z - p.MinZ;
            if (!(u > 0 && u < p.Width && v > 0 && v < p.Depth)) return false;
            int i = (int)Math.Floor(u), j = (int)Math.Floor(v);
            if (!p.IsSolid(i, j) || !p.TryPlaneTop((float)x, (float)z, out float top)) return false;
            return y < top && y > top - p.Thick;
        }
    }
}
