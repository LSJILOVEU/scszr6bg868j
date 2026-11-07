using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Recorded on the code before match.respawnTicks existed: a pack without the field (or with 0) keeps these digests.
    public sealed class RespawnDigestLockTests
    {
        // The content layer and the direct constructor describe the same rules, so both give this one value.
        internal const ulong Digest = 2502466717544704909UL;

        internal const string MatchBody =
            "\"match\": { \"captureAdvantageNum\": 2, \"captureAdvantageDen\": 1, \"captureStrict\": false, \"captureTicks\": 300, " +
            "\"interrupt\": \"reset\", \"decayPerTick\": 1, \"intervalTicks\": 90, \"thinkInterval\": 30, \"maxTicks\": 9000, " +
            "\"sight\": 35, \"gridColumns\": 4, \"gridRows\": 4, \"gridSpacing\": 1.5";

        internal static ControlPointDef[] Points() => new[] {
            new ControlPointDef(1, SimVec3.Metres(30, 0, 20), SimFix.FromInt(6), initialOwner: 1, hasSpawn: true, spawn: SimVec3.Metres(28, 0, 20)),
            new ControlPointDef(2, SimVec3.Metres(50, 0, 20), SimFix.FromInt(6))
        };
        internal static SpawnAnchor[] Bases() => new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(74, 0, 20)) };

        internal static MatchPack Direct() => new MatchPack(Points(), Bases(), 2, 1, false, 300, InterruptMode.Reset, 1, 90, 30, 9000,
            SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2));

        internal static ContentPack Load(string matchExtra)
        {
            string json = "{ \"id\": \"respawn-lock\", \"extends\": \"rwr\", " + MatchBody + matchExtra + " } }";
            return ContentLoader.Load(new FixedLocationSource(new[] { new KeyValuePair<string, string>("respawn-lock", json) }), "respawn-lock");
        }

        [Test]
        public void DirectPackDigestIsRecorded() => Assert.That(Direct().PolicyDigest, Is.EqualTo(Digest));

        [Test]
        public void ContentPackWithoutFieldDigestIsRecorded() =>
            Assert.That(Load("").BuildMatch(Points(), Bases()).PolicyDigest, Is.EqualTo(Digest));
    }
}
