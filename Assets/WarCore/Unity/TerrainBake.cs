using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // What the L7 Editor menu and Unity tests need, in bytes, strings and Unity types, so neither reads Presentation or
    // Sim types directly (the Editor assembly depends on this one only).
    public static class TerrainBake
    {
        public static byte[] DemoMapBytes() => TerrainDemo.MapBytes();

        public static GameObject Build(byte[] mapBytes, Material ground, Material wall)
            => MapView.Build(ViewMap.Parse(mapBytes), ground, wall);

        public static string VerifyGround(byte[] mapBytes, Transform root) => MapView.VerifyGround(ViewMap.Parse(mapBytes), root);

        // "map <first 16 hex digits of the file hash>": the label of a static terrain picture (no tick, no state hash).
        public static string Label(byte[] mapBytes) => "map " + ViewMap.Parse(mapBytes).FileHash.Substring(0, 16);

        // Default 45/45 IsoCamera on the centre of everything rendered under root, zoomed so all of it fits the aspect.
        public static void Frame(Camera camera, Transform root, float aspect)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            var iso = new IsoCamera(IsoCamera.MinZoom, bounds.center.y);
            iso.Follow(bounds.center.x, bounds.center.z);
            var right = iso.Right; var up = iso.Up;
            float halfWidth = 0f, halfHeight = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 d = new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y, (corner & 4) == 0 ? bounds.min.z : bounds.max.z) - bounds.center;
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(d.x * right.X + d.y * right.Y + d.z * right.Z));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(d.x * up.X + d.y * up.Y + d.z * up.Z));
            }
            iso.ZoomBy(1.05f * Mathf.Max(halfHeight, halfWidth / aspect) / iso.Zoom);
            IsoCameraInput.Apply(iso, camera);
        }
    }
}
