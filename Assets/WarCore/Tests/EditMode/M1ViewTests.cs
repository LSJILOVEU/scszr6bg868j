using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class M1ViewTests
    {
        [TestCase(0f)]
        [TestCase(0.25f)]
        [TestCase(0.999999f)]
        public void M1_Interpolation_LerpsAllAxesAddsBirthsRemovesDeathsAndSnapsTeleports(float alpha)
        {
            var previous = new FrameView(3, new[]
            {
                new ViewEntity(1, 0, 1, 2, 3),
                new ViewEntity(2, 0, 9, 9, 9),
                new ViewEntity(4, 0, 0, 0, 0),
                new ViewEntity(5, 0, 0, 0, 0),
                new ViewEntity(6, 0, 0, 0, 0)
            });
            var current = new FrameView(4, new[]
            {
                new ViewEntity(1, 7, 2, 1, 4),
                new ViewEntity(3, 3, -2, 8, 1),
                new ViewEntity(4, 4, 1.5f, 0, 1.5f),
                new ViewEntity(5, 5, 2, 0, 0),
                new ViewEntity(6, 6, 0, 2.0001f, 0)
            });
            FrameView view = FrameView.Interpolate(previous, current, alpha);
            Assert.That(view.Tick, Is.EqualTo(4));
            Assert.That(view.Entities.Length, Is.EqualTo(5));
            Assert.That(view.TryGet(1, out ViewEntity moved), Is.True);
            Assert.That(moved.Archetype, Is.EqualTo(7));
            Assert.That(moved.X, Is.EqualTo(1 + alpha));
            Assert.That(moved.Y, Is.EqualTo(2 - alpha));
            Assert.That(moved.Z, Is.EqualTo(3 + alpha));
            Assert.That(view.TryGet(2, out _), Is.False);
            foreach (ulong key in new ulong[] { 3, 4, 6 })
            {
                Assert.That(view.TryGet(key, out ViewEntity snapped), Is.True);
                Assert.That(current.TryGet(key, out ViewEntity target), Is.True);
                Assert.That(snapped, Is.EqualTo(target));
            }
            Assert.That(view.TryGet(5, out ViewEntity threshold), Is.True);
            Assert.That(threshold.X, Is.EqualTo(2 * alpha), "Exactly two metres still interpolates.");
        }

        [TestCase(-0.1f)]
        [TestCase(1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void M1_Interpolation_RejectsInvalidAlpha(float alpha)
        {
            var view = new FrameView(0, Array.Empty<ViewEntity>());
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameView.Interpolate(view, view, alpha));
        }

        [Test]
        public void M1_FrameView_OwnsItsDataAndUsesStableKeys()
        {
            var source = new[] { new ViewEntity(9, 0, 1, 2, 3), new ViewEntity(1, 0, 4, 5, 6) };
            var view = new FrameView(0, source);
            source[0] = new ViewEntity(7, 0, 9, 9, 9);
            Assert.That(view.Entities[0].Key, Is.EqualTo(1));
            Assert.That(view.TryGet(9, out ViewEntity original), Is.True);
            Assert.That(original.X, Is.EqualTo(1));
            Assert.That(view.TryGet(7, out _), Is.False);
            Assert.Throws<ArgumentException>(() => new FrameView(0, new[] { original, original }));
        }
    }
}
