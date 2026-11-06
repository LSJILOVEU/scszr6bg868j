using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // Recorded on the base commit before the map description tools existed. The values are hand-copied literals.
    public sealed class E1aRecordTests
    {
        internal const int TerrainBytesLength = 8963;
        internal const string TerrainBytesSha = "58B2207FC0B492F32AC7AF901859350018B79DF2A9B0F0294436E45361D6B882";
        internal const int M6BytesLength = 1398;
        internal const string M6BytesSha = "189E589FC51766D12CC81AF880F88E564EE25FC2C8C8440F517C3334645A5658";
        internal const ulong M6PolicyDigest = 0x526DC7AB0741E51AUL;
        internal const string M6UnitsSha = "542D841E3AA8B3C18504535205575B841A9372D55A831C067F40AA1706BCCC04";

        internal static string Sha(byte[] bytes)
        {
            Func<SHA256> factory = SHA256.Create;
            using SHA256 sha = factory();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        // One line per unit: key,xMm,zMm,team,squad,reserve,weapon followed by a line feed.
        internal static string UnitLine(ulong key, long xMm, long zMm, int team, int squad, bool reserve, int weapon) =>
            string.Join(",", key.ToString(CultureInfo.InvariantCulture), xMm.ToString(CultureInfo.InvariantCulture),
                zMm.ToString(CultureInfo.InvariantCulture), team.ToString(CultureInfo.InvariantCulture),
                squad.ToString(CultureInfo.InvariantCulture), reserve ? "true" : "false", weapon.ToString(CultureInfo.InvariantCulture)) + "\n";

        [Test]
        public void TerrainDemoBytesMatchTheRecording()
        {
            byte[] bytes = WcMapWriter.Write(TerrainDemoMap.Build());
            Assert.That(bytes.Length, Is.EqualTo(TerrainBytesLength));
            Assert.That(Sha(bytes), Is.EqualTo(TerrainBytesSha));
        }

        [Test]
        public void M6DemoBytesMatchTheRecording()
        {
            byte[] bytes = M6DemoMap.Bytes();
            Assert.That(bytes.Length, Is.EqualTo(M6BytesLength));
            Assert.That(Sha(bytes), Is.EqualTo(M6BytesSha));
        }

        [Test]
        public void M6MatchPolicyDigestMatchesTheRecording()
        {
            Assert.That(RwrMatch.CreateM6().PolicyDigest, Is.EqualTo(M6PolicyDigest));
        }

        [Test]
        public void M6UnitsMatchTheRecording()
        {
            var text = new StringBuilder();
            int count = 0, reserves = 0;
            foreach (UnitSpawn u in DemoRecipes.M6().Units)
            {
                text.Append(UnitLine(u.Key, u.XMm, u.ZMm, u.Team, u.Squad, u.Reserve, u.Weapon));
                count++;
                if (u.Reserve) reserves++;
            }
            Assert.That(count, Is.EqualTo(80));
            Assert.That(reserves, Is.EqualTo(60));
            Assert.That(Sha(Encoding.UTF8.GetBytes(text.ToString())), Is.EqualTo(M6UnitsSha));
        }
    }
}
