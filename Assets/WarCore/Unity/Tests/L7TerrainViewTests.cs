using System;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // L7: the terrain mesh view, and a full-output lock on the flat-map view it must leave unchanged.
    public sealed class L7TerrainViewTests
    {
        // (c) Flat maps (no height field): children, order, names, local position and scale are bit-exact against an
        // expectation written here independently of MapView (same float expressions, same order as MapView.Build), the
        // ground is the built-in cube, and the existing MapView.Verify still returns empty.
        [Test]
        public void FlatM2b_ViewOutputIsUnchanged()
        {
            byte[] bytes = M2Bake.DemoMapBytes();
            GameObject root = MapView.Build(ViewMap.Parse(bytes), null, null);
            try
            {
                AssertFlatOutput(ViewMap.Parse(bytes), root.transform);
                Assert.That(M2Bake.Verify(bytes, root.transform), Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void FlatM6_ViewOutputIsUnchanged() => FlatMatch(false);

        [Test]
        public void FlatM7_ViewOutputIsUnchanged() => FlatMatch(true);

        private static void FlatMatch(bool m7)
        {
            byte[] bytes = M2Bake.MatchMapBytes(m7);
            GameObject root = MapView.Build(ViewMap.Parse(bytes), null, null);
            try
            {
                AssertFlatOutput(ViewMap.Parse(bytes), root.transform);
                Assert.That(M2Bake.VerifyMatch(bytes, root.transform, m7), Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void AssertFlatOutput(ViewMap map, Transform root)
        {
            Assert.That(map.HasHeightField, Is.False, "flat fixture");
            Assert.That(root.position, Is.EqualTo(Vector3.zero));
            Assert.That(root.rotation, Is.EqualTo(Quaternion.identity));
            ReadOnlySpan<ViewWall> walls = map.Walls;
            Assert.That(walls.Length, Is.GreaterThan(0), "fixture has walls");
            Assert.That(root.childCount, Is.EqualTo(1 + walls.Length), "child count");

            Transform ground = root.GetChild(0);
            Assert.That(ground.name, Is.EqualTo("Ground"));
            float width = map.Width, depth = map.Depth;
            AssertBits(ground.localPosition, map.MinX + width / 2, map.FloorY - 0.05f, map.MinZ + depth / 2, "Ground position");
            AssertBits(ground.localScale, width, 0.1f, depth, "Ground scale");
            MeshFilter filter = ground.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Assert.That(filter.sharedMesh.name, Is.EqualTo("Cube"));

            for (int i = 0; i < walls.Length; i++)
            {
                ViewWall w = walls[i];
                Transform t = root.GetChild(1 + i);
                string name = $"Wall {w.Chunk}:{w.Placement}";
                Assert.That(t.name, Is.EqualTo(name), "child " + (1 + i));
                AssertBits(t.localPosition, w.CenterX, w.CenterY, w.CenterZ, name + " position");
                AssertBits(t.localScale, w.SizeX, w.SizeY, w.SizeZ, name + " scale");
            }
        }

        private static void AssertBits(Vector3 actual, float x, float y, float z, string what)
        {
            Assert.That(BitConverter.SingleToInt32Bits(actual.x), Is.EqualTo(BitConverter.SingleToInt32Bits(x)), what + " x");
            Assert.That(BitConverter.SingleToInt32Bits(actual.y), Is.EqualTo(BitConverter.SingleToInt32Bits(y)), what + " y");
            Assert.That(BitConverter.SingleToInt32Bits(actual.z), Is.EqualTo(BitConverter.SingleToInt32Bits(z)), what + " z");
        }
    }
}
