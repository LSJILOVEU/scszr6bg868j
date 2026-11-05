using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // The pure IsoCamera maths against the real Unity camera it drives: the headless tests only prove it agrees with itself.
    public sealed class P0CameraParityTests
    {
        private const int Width = 1600, Height = 900;
        private const float Ground = 0.5f;

        [Test]
        public void PickMatchesUnityScreenPointToRayOnTheGroundPlane()
        {
            var host = new GameObject("P0 parity camera", typeof(Camera));
            var target = new RenderTexture(Width, Height, 16);
            try
            {
                Camera unityCamera = host.GetComponent<Camera>();
                unityCamera.targetTexture = target;
                var plane = new Plane(Vector3.up, new Vector3(0f, Ground, 0f));
                foreach (float yaw in new[] { 0f, 45f, 90f, 137f, 180f, 270f })
                    foreach (float pitch in new[] { IsoCamera.MinPitch, 45f, IsoCamera.MaxPitch })
                        foreach (float zoom in new[] { 8f, 40f, IsoCamera.MaxZoom })
                        {
                            var camera = new IsoCamera(zoom, Ground, yaw, pitch);
                            camera.Follow(75f, -20f);
                            IsoCameraInput.Apply(camera, unityCamera);
                            foreach (var point in new[] { (0f, 0f), ((float)Width, 0f), (0f, (float)Height), ((float)Width, (float)Height), (Width / 2f, Height / 2f), (1234f, 321f) })
                            {
                                Ray ray = unityCamera.ScreenPointToRay(new Vector3(point.Item1, point.Item2, 0f));
                                Assert.That(plane.Raycast(ray, out float distance), Is.True, $"yaw {yaw} pitch {pitch}");
                                Vector3 hit = ray.GetPoint(distance);
                                GroundPick pick = camera.ScreenToGround(point.Item1, point.Item2, Width, Height);
                                string where = $"yaw {yaw} pitch {pitch} zoom {zoom} at {point}";
                                Assert.That(pick.X, Is.EqualTo(hit.x).Within(0.01f), where);
                                Assert.That(pick.Z, Is.EqualTo(hit.z).Within(0.01f), where);
                            }
                        }
            }
            finally
            {
                Object.DestroyImmediate(host);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
