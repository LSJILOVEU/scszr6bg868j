using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // What the Editor bake needs, in bytes and strings, so the Editor assembly depends on this one only.
    public static class M2Bake
    {
        public static byte[] DemoMapBytes() => DemoRecipes.M2bMapBytes();

        public static byte[] MatchMapBytes(bool m7 = false) => m7 ? DemoRecipes.M7MapBytes() : DemoRecipes.M6MapBytes();

        public static string VerifyMatch(byte[] mapBytes, Transform root, bool m7 = false)
            => MapView.Verify(ViewMap.Parse(mapBytes), SimSession.Create(m7 ? DemoRecipes.M7() : DemoRecipes.M6(), mapBytes), root);

        public static GameObject Build(byte[] mapBytes, Material ground, Material wall)
            => MapView.Build(ViewMap.Parse(mapBytes), ground, wall);

        // Empty = visible objects, Sim collision and navigation all agree with the map bytes.
        public static string Verify(byte[] mapBytes, Transform root)
            => MapView.Verify(ViewMap.Parse(mapBytes), SimSession.Create(DemoRecipes.M2b(), mapBytes), root);

        public static string Describe(byte[] mapBytes)
        {
            ViewMap map = ViewMap.Parse(mapBytes);
            return $"{map.Walls.Length} walls, {map.Width * map.Depth} cells agree (map {map.FileHash.Substring(0, 16)})";
        }
    }
}
