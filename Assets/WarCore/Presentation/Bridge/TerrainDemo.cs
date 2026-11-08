using WarCore.Content;

namespace WarCore.Presentation
{
    // Height-field map bytes for the terrain view (headless tests and the Unity side share these; Unity reads no Sim types).
    public static class TerrainDemo
    {
        // The WP-22 demo terrain: 48 x 32 m, ridge, road, mud, cliff band and plateau.
        public static byte[] MapBytes() => WcMapWriter.Write(TerrainDemoMap.Build());

        // The side x side checker of TerrainCheckerMap (heights +-0.25 m alternating by (i + j) mod 2).
        public static byte[] CheckerBytes(int side) => TerrainCheckerMap.Bytes(side);
    }
}
