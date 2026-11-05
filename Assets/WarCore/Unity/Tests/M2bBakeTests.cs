using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    public sealed class M2bBakeTests
    {
        private const string ScenePath = "Assets/WarCore/Unity/Scenes/M2Map.unity";
        private static byte[] Bytes => File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, M2Host.MapPath));

        [Test]
        public void FreshBuild_AgreesWithSimCollisionAndNavigationOnEveryCell()
        {
            ViewMap map = ViewMap.Parse(Bytes);
            GameObject root = MapView.Build(map, null, null);
            try
            {
                Assert.That(MapView.Verify(map, SimSession.Create(DemoRecipes.M2b(), Bytes), root.transform), Is.Empty);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void BakedSceneOnDisk_MatchesTheMapFile_AndRejectsAnotherMap()
        {
            ViewMap map = ViewMap.Parse(Bytes);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform root = Object.FindAnyObjectByType<MapBake>().transform;
                Assert.That(MapView.Verify(map, SimSession.Create(DemoRecipes.M2b(), Bytes), root), Is.Empty);
                // A different map (one wall fewer) must not verify against the old bake.
                WcMap other = M2DemoMap.Build();
                var fewer = new WcMapWall[other.Walls.Count - 1];
                for (int i = 0; i < fewer.Length; i++) fewer[i] = other.Walls[i];
                byte[] otherBytes = WcMapWriter.Write(new WcMap(other.MapId, other.ContentId, other.Min, other.Max,
                    other.ChunkSize, other.FlatHeight, other.Width, other.Depth, new[] { other.Layers[0] }, fewer,
                    UnblockLast(other)));
                ViewMap changed = ViewMap.Parse(otherBytes);
                Assert.That(MapView.Verify(changed, SimSession.Create(DemoRecipes.M2b(), otherBytes), root), Is.Not.Empty);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static byte[] UnblockLast(WcMap map)
        {
            // Clear the cells of the last wall (lone block x 40..43, z 2..5) so the bitmap matches the 3 remaining walls.
            var bits = new byte[map.Passability.Count];
            for (int i = 0; i < bits.Length; i++) bits[i] = map.Passability[i];
            for (int z = 2; z < 6; z++)
                for (int x = 40; x < 44; x++)
                {
                    int index = z * (int)map.Width + x;
                    bits[index >> 3] |= (byte)(1 << (index & 7));
                }
            return bits;
        }
    }
}
