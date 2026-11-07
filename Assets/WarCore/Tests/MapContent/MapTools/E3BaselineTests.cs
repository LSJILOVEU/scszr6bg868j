using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;

namespace WarCore.Tests
{
    // Recorded on the base before MapEdit existed: every E3 fixture is a description Read accepts, its canonical bytes
    // read back to the same bytes, and the canonical bytes of each are pinned.
    public sealed class E3BaselineTests
    {
        private static readonly string[] Expected =
        {
            "88aa026e4ea26a527cd1142980ea4fbc9ea0af4a9f2dce31368d0d1a9f354b13",
            "51be5eb5adba783fe35f51afafa2f1dbd6ea0f97da37a7dbb96bf1ce0a1d0ab8",
            "f66142725482c01b5eefa1b31317131e27b31bada824f0052aa762764fb587ed",
            "3cba3b192ff21cbd817f58f261efb6c42cb45069585f5d3cae4a27fc0afbabd1",
            "a7a88fcf4513677772566ff2c20027d681edeab8804677422ddb1b3005f37092",
            "20849bcfadce4a6fd57da1845ef924fc88355286005e6498286a97865f08a89a",
            "a1e9a5c07438e3d7b689abee4f53d4b05790ba21d4bfcff211f86132b6b1e20c",
            "d5ccab1a1675c64715224a80f2fc948d5aa431c6e0cf5ff9f3e1540d2a0e33f6",
            "f8b5d089e1d0d2136316024256ed4b68545cc399f5f25caf4a84d77dff48195e",
            "7afe4f7f46a075923b69b70476bbc25feef79923e2c8a2ab3c528dad0c05c915",
            "181b5ad98985b88192a865d799fca199945b567492a043fc72e8bfee3cc47766",
        };

        [Test]
        public void FixturesReadAndWriteBackCanonical()
        {
            Assert.That(All.Length, Is.EqualTo(11));
            for (int n = 0; n < All.Length; n++)
            {
                byte[] once = MapDescJson.Write(Read(All[n]));
                Assert.That(MapDescJson.Write(MapDescJson.Read(once)), Is.EqualTo(once), "fixture " + n);
            }
        }

        [Test]
        public void FixtureBytesArePinned()
        {
            var hashes = new string[All.Length];
            for (int n = 0; n < All.Length; n++) hashes[n] = Hex(MapDescJson.Write(Read(All[n])));
            Assert.That(hashes, Is.EqualTo(Expected), string.Join("\n", hashes));
        }

        internal static string Hex(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
