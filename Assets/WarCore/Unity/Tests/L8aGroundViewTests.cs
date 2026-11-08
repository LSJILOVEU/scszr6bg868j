using System;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // L8a: control points, trails and shot lines on the ground. (c) is the flat-map lock recorded before the change:
    // without a ground sampler every height is the old constant, bit for bit. (a)(b) stand them on a checker height field.
    public sealed class L8aGroundViewTests
    {
        private static readonly Color[] Palette = { Color.white, Color.yellow, Color.blue };

        private static FrameView PointFrame() => new FrameView(0, Array.Empty<ViewEntity>(), default,
            new[] { new ViewPoint(1, 0.5f, 0.5f, 1f, 0, 1, 0.5f), new ViewPoint(2, 1.5f, 0.5f, 1f, 0, 1, 0.5f) });

        private static FrameView UnitFrame() => new FrameView(0, new[] { new ViewEntity(7, 0, 1.5f, 0f, 0.5f, 0, 1) });

        private static FrameView ShotFrame() => new FrameView(0, Array.Empty<ViewEntity>(),
            new[] { new ViewFire(0.5f, 0.5f, 1.5f, 0.5f, 1, false) });

        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        // (c) Flat lock: disc centre 0.02f, every ring point 0.05f.
        [Test]
        public void Flat_PointDiscAndRingKeepConstantHeights()
        {
            var root = new GameObject("L8a points");
            try
            {
                new PointViews(root.transform).Apply(PointFrame());
                int rings = 0;
                for (int id = 1; id <= 2; id++)
                {
                    Transform disc = root.transform.Find("Point " + id);
                    Assert.That(disc, Is.Not.Null);
                    Assert.That(Bits(disc.position.y), Is.EqualTo(Bits(0.02f)), "disc " + id);
                    LineRenderer ring = root.transform.Find("Ring " + id).GetComponent<LineRenderer>();
                    Assert.That(ring.positionCount, Is.EqualTo(33));
                    for (int k = 0; k < ring.positionCount; k++, rings++)
                        Assert.That(Bits(ring.GetPosition(k).y), Is.EqualTo(Bits(0.05f)), "ring " + id + " point " + k);
                }
                Assert.That(rings, Is.EqualTo(66));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // (c) Flat lock: a trail point sits at the trail height.
        [Test]
        public void Flat_TrailKeepsConstantHeight()
        {
            var root = new GameObject("L8a trails");
            try
            {
                new UnitTrails(root.transform, Palette, 1.2f).Add(UnitFrame());
                LineRenderer line = root.transform.Find("Trail 7").GetComponent<LineRenderer>();
                Assert.That(line.positionCount, Is.EqualTo(1));
                Assert.That(Bits(line.GetPosition(0).y), Is.EqualTo(Bits(1.2f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // (c) Flat lock: both ends of a shot line sit at the shot height.
        [Test]
        public void Flat_ShotLineKeepsConstantHeight()
        {
            var root = new GameObject("L8a shots");
            try
            {
                new ShotLines(root.transform, Palette, 1.5f).Add(ShotFrame());
                LineRenderer line = root.transform.Find("Shot").GetComponent<LineRenderer>();
                Assert.That(line.positionCount, Is.EqualTo(2));
                Assert.That(Bits(line.GetPosition(0).y), Is.EqualTo(Bits(1.5f)));
                Assert.That(Bits(line.GetPosition(1).y), Is.EqualTo(Bits(1.5f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        // (a)(b) use the 8 x 8 checker: centre of cell (0,0) is -0.25 m, of cell (1,0) +0.25 m (h00 of each cell).
        private static ViewGround Checker() => ViewGround.For(ViewMap.Parse(TerrainBake.CheckerBytes(8)));

        // (a) Disc centres stand on the ground; every ring point is 0.05 above the ground under it.
        [Test]
        public void Terrain_PointDiscAndRingFollowTheGround()
        {
            ViewGround ground = Checker();
            var root = new GameObject("L8a points terrain");
            try
            {
                new PointViews(root.transform, ground).Apply(PointFrame());
                Assert.That(Bits(root.transform.Find("Point 1").position.y), Is.EqualTo(Bits(-0.25f + 0.02f)));
                Assert.That(Bits(root.transform.Find("Point 2").position.y), Is.EqualTo(Bits(0.25f + 0.02f)));
                int checkedPoints = 0;
                for (int id = 1; id <= 2; id++)
                {
                    LineRenderer ring = root.transform.Find("Ring " + id).GetComponent<LineRenderer>();
                    Assert.That(ring.positionCount, Is.EqualTo(33));
                    for (int k = 0; k < ring.positionCount; k++, checkedPoints++)
                    {
                        Vector3 at = ring.GetPosition(k);
                        Assert.That(Bits(at.y), Is.EqualTo(Bits(ground.HeightAt(at.x, at.z, 0f) + 0.05f)), "ring " + id + " point " + k);
                    }
                }
                Assert.That(checkedPoints, Is.EqualTo(66));
                // Ring 2, k = 0: (1.5 + cos 0, 0.5) = (2.5, 0.5), centre of cell (2,0), h00 even: -0.25.
                Vector3 first = root.transform.Find("Ring 2").GetComponent<LineRenderer>().GetPosition(0);
                Assert.That(first.x, Is.EqualTo(2.5f));
                Assert.That(Bits(first.y), Is.EqualTo(Bits(-0.25f + 0.05f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // (b) A trail point and both ends of a shot line stand at their height above the ground under them.
        [Test]
        public void Terrain_TrailFollowsTheGround()
        {
            var root = new GameObject("L8a trails terrain");
            try
            {
                new UnitTrails(root.transform, Palette, 1.2f, Checker()).Add(UnitFrame());
                LineRenderer line = root.transform.Find("Trail 7").GetComponent<LineRenderer>();
                Assert.That(line.positionCount, Is.EqualTo(1));
                Assert.That(Bits(line.GetPosition(0).y), Is.EqualTo(Bits(0.25f + 1.2f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void Terrain_ShotLineEndsFollowTheGround()
        {
            var root = new GameObject("L8a shots terrain");
            try
            {
                new ShotLines(root.transform, Palette, 1.5f, Checker()).Add(ShotFrame());
                LineRenderer line = root.transform.Find("Shot").GetComponent<LineRenderer>();
                Assert.That(line.positionCount, Is.EqualTo(2));
                Assert.That(Bits(line.GetPosition(0).y), Is.EqualTo(Bits(-0.25f + 1.5f)));
                Assert.That(Bits(line.GetPosition(1).y), Is.EqualTo(Bits(0.25f + 1.5f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
