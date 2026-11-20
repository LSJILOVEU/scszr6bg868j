using System;
using System.Collections.Generic;
using System.Globalization;

namespace WarCore.Presentation
{
    // ML-10: render mesh of one floor plate, engine-free. Four vertices of its own per face (flat shading). First the top of
    // every solid cell in row order (cell (i, j) of the plate, j then i), so the c-th solid cell's top is vertices 4c..4c+3 in
    // the order (i, j), (i, j+1), (i+1, j+1), (i+1, j); then the bottoms in the same cell order; then the sides, one quad
    // for each cell edge with no solid cell of the plate beyond it. Y is converted from the int raw once, as TerrainMesh.
    // Every face runs clockwise seen from outside (cross(v1 - v0, v2 - v0) points out), the front face in Unity.
    public sealed class PlateMesh
    {
        public const float Tolerance = 1e-3f;

        private readonly float[] positions;
        private readonly int[] triangles;

        public int Layer { get; }
        public int SolidCells { get; }
        public int VertexCount => positions.Length / 3;
        // x, y, z per vertex.
        public IReadOnlyList<float> Positions { get; }
        // Three vertex indices per triangle.
        public IReadOnlyList<int> Triangles { get; }

        private PlateMesh(int layer, int solidCells, float[] positions, int[] triangles)
        {
            Layer = layer; SolidCells = solidCells;
            this.positions = positions; this.triangles = triangles;
            Positions = Array.AsReadOnly(positions);
            Triangles = Array.AsReadOnly(triangles);
        }

        public static PlateMesh Build(ViewPlate plate)
        {
            if (plate == null) throw new ArgumentNullException(nameof(plate));
            var p = new List<float>();
            var t = new List<int>();
            int solid = 0;
            for (int j = 0; j < plate.Depth; j++)
                for (int i = 0; i < plate.Width; i++)
                    if (plate.IsSolid(i, j))
                    {
                        solid++;
                        Quad(p, t, V(plate, i, j, true), V(plate, i, j + 1, true), V(plate, i + 1, j + 1, true), V(plate, i + 1, j, true), (0, 1, 0));
                    }
            for (int j = 0; j < plate.Depth; j++)
                for (int i = 0; i < plate.Width; i++)
                    if (plate.IsSolid(i, j))
                        Quad(p, t, V(plate, i, j, false), V(plate, i + 1, j, false), V(plate, i + 1, j + 1, false), V(plate, i, j + 1, false), (0, -1, 0));
            for (int j = 0; j < plate.Depth; j++)
                for (int i = 0; i < plate.Width; i++)
                {
                    if (!plate.IsSolid(i, j)) continue;
                    if (Open(plate, i - 1, j)) Side(p, t, plate, (i, j), (i, j + 1), (-1, 0, 0));
                    if (Open(plate, i + 1, j)) Side(p, t, plate, (i + 1, j), (i + 1, j + 1), (1, 0, 0));
                    if (Open(plate, i, j - 1)) Side(p, t, plate, (i, j), (i + 1, j), (0, 0, -1));
                    if (Open(plate, i, j + 1)) Side(p, t, plate, (i, j + 1), (i + 1, j + 1), (0, 0, 1));
                }
            return new PlateMesh(plate.Layer, solid, p.ToArray(), t.ToArray());
        }

        // Empty string = the drawn positions (x, y, z per vertex) are this mesh's, each coordinate within Tolerance; else the
        // first problem: the vertex count, or the first vertex off (named by its cell when it is a top vertex).
        public string Compare(IReadOnlyList<float> drawn)
        {
            if (drawn == null) return Line("no mesh drawn");
            if (drawn.Count != positions.Length)
                return Line((drawn.Count / 3).ToString(CultureInfo.InvariantCulture) + " vertices drawn, "
                    + VertexCount.ToString(CultureInfo.InvariantCulture) + " expected");
            for (int v = 0; v < VertexCount; v++)
                for (int c = 0; c < 3; c++)
                    if (!(Math.Abs(drawn[3 * v + c] - positions[3 * v + c]) <= Tolerance))
                        return Line("vertex " + v.ToString(CultureInfo.InvariantCulture) + (v < 4 * SolidCells
                            ? " (top of solid cell " + (v / 4).ToString(CultureInfo.InvariantCulture) + ")" : "") + " is off");
            return "";
        }

        private string Line(string what) => "plate " + Layer.ToString(CultureInfo.InvariantCulture) + ": " + what;

        // No solid cell of the plate there (outside the plate or a cutout).
        private static bool Open(ViewPlate plate, int i, int j) => i < 0 || j < 0 || i >= plate.Width || j >= plate.Depth || !plate.IsSolid(i, j);

        private static (float, float, float) V(ViewPlate plate, int i, int j, bool top) =>
            (plate.MinX + i, top ? plate.Top(i, j) : plate.Bottom(i, j), plate.MinZ + j);

        private static void Side(List<float> p, List<int> t, ViewPlate plate, (int I, int J) a, (int I, int J) b, (int, int, int) normal) =>
            Quad(p, t, V(plate, a.I, a.J, false), V(plate, b.I, b.J, false), V(plate, b.I, b.J, true), V(plate, a.I, a.J, true), normal);

        // A quad from a ring of four points, turned so that it faces along the normal.
        private static void Quad(List<float> p, List<int> t, (float X, float Y, float Z) a, (float X, float Y, float Z) b,
            (float X, float Y, float Z) c, (float X, float Y, float Z) d, (int X, int Y, int Z) n)
        {
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
            double facing = (uy * vz - uz * vy) * n.X + (uz * vx - ux * vz) * n.Y + (ux * vy - uy * vx) * n.Z;
            if (facing < 0) { var s = b; b = d; d = s; }
            int first = p.Count / 3;
            foreach (var q in new[] { a, b, c, d }) { p.Add(q.X); p.Add(q.Y); p.Add(q.Z); }
            t.Add(first); t.Add(first + 1); t.Add(first + 2);
            t.Add(first); t.Add(first + 2); t.Add(first + 3);
        }
    }
}
