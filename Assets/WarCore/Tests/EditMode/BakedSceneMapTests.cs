using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WarCore.Tests
{
    // The hosts refuse to run when a scene's MapBake.fileHash differs from the map file's trailing SHA-256.
    // Read both from disk so a map change without a re-bake fails here, without Unity.
    public sealed class BakedSceneMapTests
    {
        [TestCase("WarCore/Unity/Scenes/M2Map.unity", "StreamingAssets/maps/M2Demo.wcmap")]
        [TestCase("WarCore/Unity/Scenes/M6Map.unity", "StreamingAssets/maps/M6Demo.wcmap")]
        [TestCase("WarCore/Unity/Scenes/M7Map.unity", "StreamingAssets/maps/M7Demo.wcmap")]
        public void SceneFileHash_EqualsMapTrailer(string scene, string map)
        {
            string root = ArchitectureTests.AssetRoot();
            MatchCollection hashes = Regex.Matches(File.ReadAllText(Path.Combine(root, scene)), @"^\s*fileHash: (\S*)\s*$", RegexOptions.Multiline);
            Assert.That(hashes.Count, Is.EqualTo(1), scene);
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, map));
            string trailer = BitConverter.ToString(bytes, bytes.Length - 32, 32).Replace("-", "");
            Assert.That(hashes[0].Groups[1].Value, Is.EqualTo(trailer), $"{scene}: run the matching WarCore Bake Scene");
        }
    }
}
