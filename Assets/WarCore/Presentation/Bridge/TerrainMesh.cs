using System;
using System.Collections.Generic;

namespace WarCore.Presentation
{
    // Render mesh of a .wcmap height field, engine-free. One vertex per height point, (W+1) x (D+1), index j*(W+1)+i like
    // the Sim HeightField. Vertex (i, j) = (MinX + i, raw(i, j) / 65536f, MinZ + j): Y is converted from the int raw once,
    // so it is exact while |raw| < 2^24 (256 m); above that (Sim allows up to 2^29) a vertex rounds by at most 2^4 raw.
    // Two triangles per cell, cell key z*W + x, triangle A at 2*key and B at 2*key+1, cut along the HeightField diagonal
    // (x,z) -> (x+1,z+1): A = (v00, v11, v10) is the u >= v half, B = (v00, v01, v11) the u < v half. Seen from +Y with X
    // to the right and Z up the screen both run clockwise, which is the front face in Unity: for a flat cell,
    // cross(v1 - v0, v2 - v0).y = 1 for A ((1,0,1) x (1,0,0)) and for B ((0,0,1) x (1,0,1)), so normals point up.
    public sealed class TerrainMesh
    {
        private readonly float[] positions;
        private readonly int[] triangles;

        public int Width { get; }
        public int Depth { get; }
        public int VertexCount => positions.Length / 3;
        // x, y, z per vertex.
        public IReadOnlyList<float> Positions { get; }
        // Three vertex indices per triangle.
        public IReadOnlyList<int> Triangles { get; }

        private TerrainMesh(int width, int depth, float[] positions, int[] triangles)
        {
            Width = width; Depth = depth;
            this.positions = positions;
            this.triangles = triangles;
            Positions = Array.AsReadOnly(positions);
            Triangles = Array.AsReadOnly(triangles);
        }

        public static TerrainMesh Build(ViewMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!map.HasHeightField) throw new InvalidOperationException("Map has no height field.");
            int width = map.Width, depth = map.Depth, row = width + 1;
            var positions = new float[row * (depth + 1) * 3];
            for (int j = 0; j <= depth; j++)
                for (int i = 0; i <= width; i++)
                {
                    int p = 3 * (j * row + i);
                    positions[p] = map.MinX + i;
                    positions[p + 1] = (float)map.VertexRaw(i, j) / 65536f;
                    positions[p + 2] = map.MinZ + j;
                }
            var triangles = new int[6 * width * depth];
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    int t = 6 * (z * width + x);
                    int v00 = z * row + x, v10 = v00 + 1, v01 = v00 + row, v11 = v01 + 1;
                    triangles[t] = v00; triangles[t + 1] = v11; triangles[t + 2] = v10;
                    triangles[t + 3] = v00; triangles[t + 4] = v01; triangles[t + 5] = v11;
                }
            return new TerrainMesh(width, depth, positions, triangles);
        }
    }
}
