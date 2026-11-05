using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Wp18MapperTests
    {
        [TestCase(0f, 0, 1000, 12250L, 40892L)]
        [TestCase(90f, 1000, 0, 16392L, 16750L)]
        [TestCase(180f, 0, -1000, -7750L, 12608L)]
        [TestCase(270f, -1000, 0, -11892L, 36750L)]
        public void FixedCamera_AbsoluteMillimetreExpectations(float yaw, int mx, int mz, long ax, long az)
        {
            var camera = new IsoCamera(10, groundY: 7, yaw: yaw, pitch: 45);
            camera.Follow(2.25f, 26.75f);
            PlayerInput input = new PlayerInputMapper().Map(camera, 0, 1, 750, 500, 1000, 500, true, true);
            Assert.That(input.MoveXMilli, Is.EqualTo(mx)); Assert.That(input.MoveZMilli, Is.EqualTo(mz));
            Assert.That(input.AimXMm, Is.EqualTo(ax)); Assert.That(input.AimZMm, Is.EqualTo(az));
            Assert.That(input.Primary, Is.True);
        }

        [Test]
        public void DefaultDiagonal_TruncatesTowardZero_AndAimUsesGroundPickRounding()
        {
            var camera = new IsoCamera(10);
            camera.Follow(1.2346f, -2.3456f);
            var mapper = new PlayerInputMapper();
            var up = mapper.Map(camera, 0, 1, 500, 250, 1000, 500, true, false);
            Assert.That(up.MoveXMilli, Is.EqualTo(707)); Assert.That(up.MoveZMilli, Is.EqualTo(707));
            Assert.That(up.AimXMm, Is.EqualTo(1235)); Assert.That(up.AimZMm, Is.EqualTo(-2346));
            var down = mapper.Map(camera, 0, -1, 500, 250, 1000, 500, true, false);
            Assert.That(down.MoveXMilli, Is.EqualTo(-707)); Assert.That(down.MoveZMilli, Is.EqualTo(-707));
        }

        [TestCase(0.1239f, -0.9879f, 123, -987)]
        [TestCase(-0.0009f, 0.0009f, 0, 0)]
        [TestCase(100f, 0f, 1000, 0)]
        [TestCase(-100f, 0f, -1000, 0)]
        [TestCase(0f, 100f, 0, 1000)]
        [TestCase(3f, 4f, 600, 800)]
        [TestCase(1f, 1f, 707, 707)]
        public void Movement_NormalizesClampsAndTruncates(float ix, float iy, int mx, int mz)
        {
            var input = new PlayerInputMapper().Map(new IsoCamera(10, yaw: 0), ix, iy,
                0, 0, 100, 100, false, false);
            Assert.That(input.MoveXMilli, Is.EqualTo(mx)); Assert.That(input.MoveZMilli, Is.EqualTo(mz));
            Assert.That(input.MoveXMilli, Is.InRange(-1000, 1000));
            Assert.That(input.MoveZMilli, Is.InRange(-1000, 1000));
        }

        [Test]
        public void RotateCamera_WRemainsScreenUp()
        {
            var camera = new IsoCamera(10); var mapper = new PlayerInputMapper();
            foreach (float rotation in new[] { 0f, 90f, 37f, 180f })
            {
                camera.Rotate(rotation);
                var input = mapper.Map(camera, 0, 1, 50, 50, 100, 100, true, false);
                var right = camera.Right; var up = camera.Up;
                float screenX = input.MoveXMilli * right.X + input.MoveZMilli * right.Z;
                float screenY = input.MoveXMilli * up.X + input.MoveZMilli * up.Z;
                Assert.That(screenX, Is.EqualTo(0).Within(1));
                Assert.That(screenY, Is.GreaterThan(700));
            }
        }

        [TestCase(false, 800f, 600f)]
        [TestCase(true, 0f, 600f)]
        [TestCase(true, 800f, 0f)]
        [TestCase(true, -1f, 600f)]
        [TestCase(true, 800f, -1f)]
        public void InvalidPick_RetainsLastValidAim_ButStillMapsMovementAndPrimary(bool inside, float width, float height)
        {
            var camera = new IsoCamera(10); var mapper = new PlayerInputMapper();
            var first = mapper.Map(camera, 0, 0, 520, 380, 800, 600, true, false);
            camera.Follow(30, 40); camera.Rotate(90);
            var next = mapper.Map(camera, 0, 1, -900, 999, width, height, inside, true);
            Assert.That(next.AimXMm, Is.EqualTo(first.AimXMm));
            Assert.That(next.AimZMm, Is.EqualTo(first.AimZMm));
            Assert.That(next.MoveXMilli, Is.EqualTo(707)); Assert.That(next.MoveZMilli, Is.EqualTo(-707));
            Assert.That(next.Primary, Is.True);
        }

        [Test]
        public void BeforeAnyValidPick_DefaultTracksFocusGroundPoint()
        {
            var camera = new IsoCamera(10, groundY: 30); var mapper = new PlayerInputMapper();
            camera.Follow(1.2346f, -2.3456f);
            var outside = mapper.Map(camera, 0, 0, 0, 0, 800, 600, false, false);
            Assert.That(outside.AimXMm, Is.EqualTo(1235)); Assert.That(outside.AimZMm, Is.EqualTo(-2346));
            camera.Follow(-12, 34);
            var invalid = mapper.Map(camera, 0, 0, 0, 0, 0, 0, true, false);
            Assert.That(invalid.AimXMm, Is.EqualTo(-12000)); Assert.That(invalid.AimZMm, Is.EqualTo(34000));
        }
    }
}
