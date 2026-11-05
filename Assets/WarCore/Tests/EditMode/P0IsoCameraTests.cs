using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class P0IsoCameraTests
    {
        private const float Width = 1600, Height = 900;
        private static readonly float[] Yaws = { 0, 45, 90, 137, 180, 270 };
        private static readonly float[] Pitches = { 30, 45, 60 };
        private static readonly float[] Zooms = { 8, 40 };
        private static readonly float[] Offsets = { -20f, -7f, 0f, 3f, 20f };

        // Independent of ScreenToGround: project a world point onto the screen with the camera axes.
        private static (float X, float Y) Project(IsoCamera camera, float x, float y, float z)
        {
            var p = camera.Position; var r = camera.Right; var u = camera.Up;
            float dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
            float nx = (dx * r.X + dy * r.Y + dz * r.Z) / (camera.Zoom * Width / Height);
            float ny = (dx * u.X + dy * u.Y + dz * u.Z) / camera.Zoom;
            return ((nx + 1) / 2 * Width, (ny + 1) / 2 * Height);
        }

        [Test]
        public void DefaultsAreFortyFiveByFortyFive()
        {
            var camera = new IsoCamera(12);
            Assert.That(camera.Yaw, Is.EqualTo(45f));
            Assert.That(camera.Pitch, Is.EqualTo(45f));
        }

        [Test]
        public void ScreenCentreIsTheFocusAndScreenUpIsPlusZAtYawZero()
        {
            var camera = new IsoCamera(10, yaw: 0, pitch: 45);
            camera.Follow(30f, 20f);
            GroundPick centre = camera.ScreenToGround(Width / 2, Height / 2, Width, Height);
            Assert.That(centre.X, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(centre.Z, Is.EqualTo(20f).Within(1e-3f));
            // Top edge: half-height 10 m along camera-up, which reaches the ground 10 / sin(45 deg) metres further along +z.
            GroundPick top = camera.ScreenToGround(Width / 2, Height, Width, Height);
            Assert.That(top.X, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(top.Z, Is.EqualTo(20f + 10f / MathF.Sin(MathF.PI / 4)).Within(1e-3f));
        }

        // Unity facts, written out by hand: looking along +x (yaw 90), screen-up is +x on the ground and screen-right is -z.
        [Test]
        public void YawNinetyLooksAlongPlusXWithScreenRightMinusZ()
        {
            var camera = new IsoCamera(10, yaw: 90, pitch: 45);
            camera.Follow(30f, 20f);
            GroundPick top = camera.ScreenToGround(Width / 2, Height, Width, Height);
            Assert.That(top.X, Is.EqualTo(30f + 10f / MathF.Sin(MathF.PI / 4)).Within(1e-3f));
            Assert.That(top.Z, Is.EqualTo(20f).Within(1e-3f));
            GroundPick right = camera.ScreenToGround(Width, Height / 2, Width, Height);
            Assert.That(right.X, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(right.Z, Is.EqualTo(20f - 10f * Width / Height).Within(1e-3f));
        }

        // World point -> screen (independent projection) -> pick must land back within 1 mm, on a ground that is not at y=0.
        [Test]
        public void PickRoundTripsWithinOneMillimetreAtEveryRotationPitchAndZoom()
        {
            const float ground = 0.5f;
            int checkedPoints = 0;
            foreach (float yaw in Yaws)
                foreach (float pitch in Pitches)
                    foreach (float zoom in Zooms)
                    {
                        var camera = new IsoCamera(zoom, groundY: ground, yaw: yaw, pitch: pitch);
                        camera.Follow(75f, -20f);
                        foreach (float dx in Offsets)
                            foreach (float dz in Offsets)
                            {
                                float wx = 75f + dx, wz = -20f + dz;
                                var screen = Project(camera, wx, ground, wz);
                                GroundPick pick = camera.ScreenToGround(screen.X, screen.Y, Width, Height);
                                Assert.That(Math.Abs(pick.X - wx), Is.LessThanOrEqualTo(1e-3f), $"x: yaw {yaw} pitch {pitch} zoom {zoom} at {dx},{dz}");
                                Assert.That(Math.Abs(pick.Z - wz), Is.LessThanOrEqualTo(1e-3f), $"z: yaw {yaw} pitch {pitch} zoom {zoom} at {dx},{dz}");
                                checkedPoints++;
                            }
                    }
            Assert.That(checkedPoints, Is.EqualTo(Yaws.Length * Pitches.Length * Zooms.Length * Offsets.Length * Offsets.Length));
        }

        // Rays start Zoom*cos(pitch) below the centre at the bottom edge: that start must stay above the ground over the whole range.
        [Test]
        public void EveryRayStartsAboveTheGroundAcrossThePitchAndZoomRange()
        {
            foreach (float pitch in new[] { IsoCamera.MinPitch, 30f, 45f, 60f, IsoCamera.MaxPitch })
                foreach (float zoom in new[] { IsoCamera.MinZoom, 40f, 80f, IsoCamera.MaxZoom })
                {
                    var camera = new IsoCamera(zoom, groundY: 0.5f, yaw: 45f, pitch: pitch);
                    var p = camera.Position; var u = camera.Up;
                    float bottomStartY = p.Y - u.Y * zoom;
                    Assert.That(bottomStartY, Is.GreaterThan(0.5f + 3f), $"pitch {pitch} zoom {zoom}");
                    GroundPick pick = camera.ScreenToGround(Width / 2, 0, Width, Height);
                    Assert.That(float.IsFinite(pick.X) && float.IsFinite(pick.Z), Is.True);
                }
        }

        [Test]
        public void PitchLimitsStillPick()
        {
            foreach (float pitch in new[] { IsoCamera.MinPitch, IsoCamera.MaxPitch })
            {
                var camera = new IsoCamera(20, groundY: 0f, yaw: 33, pitch: pitch);
                camera.Follow(10f, 10f);
                var screen = Project(camera, 14f, 0f, 3f);
                GroundPick pick = camera.ScreenToGround(screen.X, screen.Y, Width, Height);
                Assert.That(Math.Abs(pick.X - 14f), Is.LessThanOrEqualTo(1e-3f), $"pitch {pitch}");
                Assert.That(Math.Abs(pick.Z - 3f), Is.LessThanOrEqualTo(1e-3f), $"pitch {pitch}");
            }
        }

        [Test]
        public void PickGivesWholeMillimetres()
        {
            var camera = new IsoCamera(10, yaw: 0, pitch: 45);
            camera.Follow(1.2344f, 5f);
            GroundPick pick = camera.ScreenToGround(Width / 2, Height / 2, Width, Height);
            Assert.That(pick.XMm, Is.EqualTo(1234));
            Assert.That(pick.ZMm, Is.EqualTo(5000));
        }

        [Test]
        public void WasdFollowsTheCameraAtEveryYaw()
        {
            foreach (float yaw in Yaws)
            {
                var camera = new IsoCamera(12, yaw: yaw);
                var r = camera.Right; var u = camera.Up;
                var w = camera.MoveAxes(0, 1);
                var d = camera.MoveAxes(1, 0);
                // On screen W points straight up and D straight right: no sideways part, and the forward part is positive.
                Assert.That(w.X * r.X + w.Z * r.Z, Is.EqualTo(0f).Within(1e-5f), $"yaw {yaw}");
                Assert.That(w.X * u.X + w.Z * u.Z, Is.GreaterThan(0.1f), $"yaw {yaw}");
                Assert.That(d.X * u.X + d.Z * u.Z, Is.EqualTo(0f).Within(1e-5f), $"yaw {yaw}");
                Assert.That(d.X * r.X + d.Z * r.Z, Is.EqualTo(1f).Within(1e-5f), $"yaw {yaw}");
                var diagonal = camera.MoveAxes(1, 1);
                Assert.That(MathF.Sqrt(diagonal.X * diagonal.X + diagonal.Z * diagonal.Z), Is.EqualTo(1f).Within(1e-5f), $"yaw {yaw}");
                var none = camera.MoveAxes(0, 0);
                Assert.That(none, Is.EqualTo((0f, 0f)));
            }
        }

        [Test]
        public void YawWrapsPitchAndZoomClamp()
        {
            var camera = new IsoCamera(12);
            camera.Rotate(-100);
            Assert.That(camera.Yaw, Is.EqualTo(305f).Within(1e-4f));
            camera.Rotate(100);
            Assert.That(camera.Yaw, Is.EqualTo(45f).Within(1e-4f));
            camera.Tilt(500);
            Assert.That(camera.Pitch, Is.EqualTo(IsoCamera.MaxPitch));
            camera.Tilt(-500);
            Assert.That(camera.Pitch, Is.EqualTo(IsoCamera.MinPitch));
            camera.ZoomBy(1000);
            Assert.That(camera.Zoom, Is.EqualTo(IsoCamera.MaxZoom));
            camera.ZoomBy(1e-6f);
            Assert.That(camera.Zoom, Is.EqualTo(IsoCamera.MinZoom));
        }
    }
}
