using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // The hosts refuse to run when the scene's MapBake.fileHash differs from session.Map.FileHash.
    // Read both from disk so a map change without a re-bake fails here, without Unity.
    public sealed class BakedSceneMapTests
    {
        private static readonly string[][] Baked =
        {
            new[] { "WarCore/Unity/Scenes/M2Map.unity", "StreamingAssets/maps/M2Demo.wcmap" },
            new[] { "WarCore/Unity/Scenes/M6Map.unity", "StreamingAssets/maps/M6Demo.wcmap" },
            new[] { "WarCore/Unity/Scenes/M7Map.unity", "StreamingAssets/maps/M7Demo.wcmap" },
        };

        private static string MapBakeGuid()
        {
            string meta = File.ReadAllText(Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Unity/MapBake.cs.meta"));
            return Regex.Match(meta, @"^guid: ([0-9a-f]{32})\s*$", RegexOptions.Multiline).Groups[1].Value;
        }

        private static string ScriptRef(string guid) => "m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}";

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MapBakeFileHash_EqualsParsedMapHash(int index)
        {
            string root = ArchitectureTests.AssetRoot();
            string scene = Baked[index][0];
            string guid = MapBakeGuid();
            Assert.That(guid, Is.Not.Empty);
            // A scene file is a list of "--- !u!" object blocks; keep the MapBake ones.
            string[] blocks = Regex.Split(File.ReadAllText(Path.Combine(root, scene)), @"^--- !u!", RegexOptions.Multiline)
                .Where(b => b.Contains(ScriptRef(guid))).ToArray();
            Assert.That(blocks.Length, Is.EqualTo(1), scene);
            MatchCollection hashes = Regex.Matches(blocks[0], @"^\s*fileHash: (\S*)\s*$", RegexOptions.Multiline);
            Assert.That(hashes.Count, Is.EqualTo(1), scene);
            // Parse verifies the body digest, as SimSession.Create does at startup.
            ViewMap map = ViewMap.Parse(File.ReadAllBytes(Path.Combine(root, Baked[index][1])));
            Assert.That(hashes[0].Groups[1].Value, Is.EqualTo(map.FileHash), $"{scene}: run the matching WarCore Bake Scene");
        }

        [Test]
        public void EveryMapBakeScene_IsListed()
        {
            string root = ArchitectureTests.AssetRoot();
            string script = ScriptRef(MapBakeGuid());
            string[] found = Directory.GetFiles(root, "*.unity", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains(script))
                .Select(f => f.Substring(root.Length + 1).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Assert.That(found, Is.EqualTo(Baked.Select(b => b[0]).OrderBy(f => f, StringComparer.Ordinal).ToArray()));
        }
    }
}
