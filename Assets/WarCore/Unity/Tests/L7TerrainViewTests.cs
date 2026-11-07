using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // L7: the terrain mesh view, and a full-output lock on the flat-map view it must leave unchanged.
    public sealed class L7TerrainViewTests
    {
        // (a) The demo terrain view holds exactly the height-field mesh; 49 x 33 = 1617 vertices fit 16-bit indices.
        [Test]
        public void DemoTerrain_GroundMatchesHeightField_UInt16()
        {
            byte[] bytes = TerrainBake.DemoMapBytes();
            GameObject root = TerrainBake.Build(bytes, null, null);
            try
            {
                Mesh mesh = GroundMesh(root);
                Assert.That(mesh.vertexCount, Is.EqualTo(1617));
                Assert.That(mesh.indexFormat, Is.EqualTo(IndexFormat.UInt16));
                Assert.That(TerrainBake.VerifyGround(bytes, root.transform), Is.Empty);
            }
            finally { DestroyView(root); }
        }

        // (d) VerifyGround is not vacuous: each broken view reports something.
        [Test]
        public void VerifyGround_VertexMoved_Reports()
        {
            byte[] bytes = TerrainBake.DemoMapBytes();
            GameObject root = TerrainBake.Build(bytes, null, null);
            try
            {
                Mesh mesh = GroundMesh(root);
                Vector3[] vertices = mesh.vertices;
                vertices[800].y += 1f;
                mesh.vertices = vertices;
                Assert.That(TerrainBake.VerifyGround(bytes, root.transform), Is.Not.Empty);
            }
            finally { DestroyView(root); }
        }

        [Test]
        public void VerifyGround_IndexSwapped_Reports()
        {
            byte[] bytes = TerrainBake.DemoMapBytes();
            GameObject root = TerrainBake.Build(bytes, null, null);
            try
            {
                Mesh mesh = GroundMesh(root);
                int[] triangles = mesh.triangles;
                (triangles[3], triangles[4]) = (triangles[4], triangles[3]);
                mesh.triangles = triangles;
                Assert.That(TerrainBake.VerifyGround(bytes, root.transform), Is.Not.Empty);
            }
            finally { DestroyView(root); }
        }

        [Test]
        public void VerifyGround_GroundMissing_Reports()
        {
            byte[] bytes = TerrainBake.DemoMapBytes();
            GameObject root = TerrainBake.Build(bytes, null, null);
            try
            {
                DestroyGroundMesh(root);
                UnityEngine.Object.DestroyImmediate(root.transform.Find("Ground").gameObject);
                Assert.That(TerrainBake.VerifyGround(bytes, root.transform), Is.Not.Empty);
            }
            finally { DestroyView(root); }
        }

        [Test]
        public void VerifyGround_FlatMap_Reports()
        {
            byte[] bytes = M2Bake.MatchMapBytes(true);
            GameObject root = MapView.Build(ViewMap.Parse(bytes), null, null);
            try
            {
                Assert.That(TerrainBake.VerifyGround(bytes, root.transform), Is.Not.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Mesh GroundMesh(GameObject root)
        {
            Transform ground = root.transform.Find("Ground");
            Assert.That(ground, Is.Not.Null);
            MeshFilter filter = ground.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            Assert.That(filter.sharedMesh, Is.Not.Null);
            return filter.sharedMesh;
        }

        // The terrain mesh is created by MapView, not an asset; destroy it with the view.
        private static void DestroyGroundMesh(GameObject root)
        {
            Transform ground = root.transform.Find("Ground");
            MeshFilter filter = ground == null ? null : ground.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name != "Cube")
                UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
        }

        private static void DestroyView(GameObject root)
        {
            DestroyGroundMesh(root);
            UnityEngine.Object.DestroyImmediate(root);
        }

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
