using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // ML-1 acceptance 1: the four maps that existed before walls could have a raised bottom bake to the same .wcmap bytes
    // and write back the same description bytes. Hashes recorded at the base commit (130ec55) before any ML-1 change.
    public sealed class Ml1LegacyBytesTests
    {
        [TestCase("lanes-playtest",
            "33a8358f00dbd38bb1428b309dba39722e16fdd790a795af5800cba2343aeac3",
            "28b1acb6be0a965dd8b32e6644f230ba9c24d5173699f69a288d7eb42d1fcf13")]
        [TestCase("m6-demo",
            "189e589fc51766d12cc81af880f88e564ee25fc2c8c8440f517c3334645a5658",
            "49909c57915f4459a9d25228e10fb8ef68dc9cf687aabda7f70196c980c05969")]
        [TestCase("m7-demo",
            "e8eb6d83cacc79bba7be02f19e29d9b993688ee5d9ccaa9eb6c47fa70a9c4bbf",
            "98dcca20190e3ea85e8f68efa87a810cf07a98f073d1e09c3f8fe9daf5ca1ff3")]
        [TestCase("terrain-demo",
            "58b2207fc0b492f32ac7af901859350018b79df2a9b0f0294436e45361d6b882",
            "a884819907386103d318a69a1e04c34c6653b14362af9000d7351640af24664d")]
        public void OldMapsBakeAndWriteBackUnchanged(string name, string wcMapSha, string descSha)
        {
            byte[] bytes = File.ReadAllBytes("Assets/WarCore/Maps/" + name + ".map.json");
            MapBakeResult bake = MapBake.Run(bytes);
            byte[] written = MapDescJson.Write(MapDescJson.Read(bytes));
            TestContext.Out.WriteLine($"ML1_LEGACY {name} wcmap={Hex(bake.WcMapBytes)} desc={Hex(written)}");
            Assert.That(MapDescJson.Read(bytes).Version, Is.EqualTo(1));
            Assert.That(Hex(bake.WcMapBytes), Is.EqualTo(wcMapSha));
            Assert.That(Hex(written), Is.EqualTo(descSha));
            Assert.That(written, Is.EqualTo(bytes));
        }

        private static string Hex(byte[] bytes)
        {
            Func<SHA256> factory = SHA256.Create;
            using SHA256 sha = factory();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
