using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt167AimPickTests
    {
        [Test]
        public void VerticalRayInsideOuterSnapRadius_SelectsEnemy()
        {
            var shown = new FrameView(1, new[]
            {
                new ViewEntity(1, 0, 2f, 0f, 4.5f, team: 1),
                new ViewEntity(2, 0, 9f, 0f, 4.5f, team: 2)
            });
            AimTarget a = AimPick.PickRay(9.375f, 4f, 4.5f, 0f, -1f, 0f, 0f, null, shown, 1);

            Assert.That(a.Kind, Is.EqualTo(AimKind.Unit));
            Assert.That(a.UnitKey, Is.EqualTo(2UL));
            Assert.That(a.XMm, Is.EqualTo(9000));
        }

        [Test]
        public void RayAboveOneMetreBody_DoesNotSelectEnemy()
        {
            var shown = new FrameView(1, new[]
            {
                new ViewEntity(1, 0, 2f, 0f, 4.5f, team: 1),
                new ViewEntity(2, 0, 9f, 0f, 4.5f, team: 2)
            });
            // The ray crosses the cylinder from y = 1.875 m to y = 1.125 m, above its top.
            AimTarget a = AimPick.PickRay(7f, 3f, 4.5f, 0.8f, -0.6f, 0f, 0f, null, shown, 1);

            Assert.That(a.Kind, Is.EqualTo(AimKind.Plane));
            Assert.That(a.UnitKey, Is.EqualTo(0UL));
        }

        [TestCase(0f, 4.5f, 0L)]
        [TestCase(9f, 0f, 1500L)]
        public void VerticalRayOnMinimumEdge_HitsGround(float x, float z, long heightMm)
        {
            AimTarget a = AimPick.PickRay(x, 4f, z, 0f, -1f, 0f, 0f, A4.RidgeGround(), null, null);

            Assert.That(a.Kind, Is.EqualTo(AimKind.Ground));
            Assert.That(a.X, Is.EqualTo(x));
            Assert.That(a.Z, Is.EqualTo(z));
            Assert.That(a.YMm, Is.EqualTo(heightMm));
        }

        [Test]
        public void DescendingRay_StopsAtFirstRawPointBelowGround()
        {
            float fy = (float)(-1 / System.Math.Sqrt(5));
            AimTarget a = AimPick.PickRay(4f, 1f, 4.5f, 2f * fy, fy, 0f, 0f, A4.RidgeGround(), null, null);

            // With x = 2 + 2y, the first floored point below y = 0 has x one raw unit below 2 m.
            Assert.That(a.Kind, Is.EqualTo(AimKind.Ground));
            Assert.That(a.X, Is.EqualTo(2f - 1f / 65536f));
            Assert.That(a.Y, Is.EqualTo(0f));
            Assert.That(a.Z, Is.EqualTo(4.5f));
        }

        [Test]
        public void VerticalRayBetweenRawCoordinates_RoundsDown()
        {
            AimTarget a = AimPick.PickRay(9f + 0.5f / 65536f, 4f, 4.5f + 0.5f / 65536f,
                0f, -1f, 0f, 0f, A4.RidgeGround(), null, null);

            Assert.That(a.Kind, Is.EqualTo(AimKind.Ground));
            Assert.That(a.X, Is.EqualTo(9f));
            Assert.That(a.Z, Is.EqualTo(4.5f));
            Assert.That(a.Y, Is.EqualTo(1.5f));
        }
    }
}
