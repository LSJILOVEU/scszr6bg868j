using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.RespawnAi2World;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 A1, recorded on the code before RESPAWN-AI-2 (main f50a169) with only the old API. Three worlds without
    // any of the new fields; the new code changes none of the recorded values.
    //   * The fixture without a player, key 2 killed at tick 10: the pack digest, the 300-tick run (64-bit FNV-1a over the
    //     per-tick retained hashes) and the reinforcement state (version, bytes, hash).
    //   * The fixture with a player and a 90-tick respawn, the player killed at tick 10: the 300-tick run.
    //   * Map W started from its file with its content layer: the match digest, the units and the 300-tick run.
    public sealed class RespawnAi2LockTests
    {
        internal const ulong PackDigest = 16108698768566864724UL, Run300 = 12431482551001485662UL, ReinforceHash300 = 14288369332062695791UL;
        internal const ulong PlayerPackDigest = 3476380920153938054UL, PlayerRun300 = 1023478531163543167UL;
        internal const ulong ContentDigest = 1807449205778869687UL, MapRun300 = 9515986946632214725UL;

        [Test]
        public void RespawnAi2Lock_OldWorldIsUnchanged()
        {
            M6World w = World(Units(), OldPack());
            Kill(w, 10, 2);
            ulong run = Run(w, 300);
            byte[] saved = Saved(w.Reinforce);
            TestContext.Out.WriteLine($"pack {w.Recipe.Match.PolicyDigest}UL run {run}UL reinforce {HashOf(w.Reinforce)}UL bytes {saved.Length}");
            Assert.That(w.Recipe.Match.PolicyDigest, Is.EqualTo(PackDigest), "pack digest");
            Assert.That(run, Is.EqualTo(Run300), "300-tick run");
            Assert.That((w.Reinforce.StateVersion, saved.Length, HashOf(w.Reinforce)), Is.EqualTo((1, 64, ReinforceHash300)), "reinforcement state");
            Assert.That((w.Reinforce.Attempts(1), w.Reinforce.Successes(1), Reserves(w, 1), Fielded(w, 1)), Is.EqualTo((10L, 4L, 0, 6)), "ten attempts, four reserves, by the clock");
        }

        [Test]
        public void RespawnAi2Lock_OldPlayerWorldIsUnchanged()
        {
            M6World w = World(Units(), OldPack(respawn: 90), player: true);
            KillPlayer(w, 10);
            ulong run = Run(w, 300);
            TestContext.Out.WriteLine($"pack {w.Recipe.Match.PolicyDigest}UL run {run}UL");
            Assert.That(w.Recipe.Match.PolicyDigest, Is.EqualTo(PlayerPackDigest), "pack digest");
            Assert.That(run, Is.EqualTo(PlayerRun300), "300-tick run");
            Assert.That((w.Dead(Player), RespawnWorld.Controlled(w) != Player), Is.EqualTo((true, true)), "the player died and took a reserve over");
        }

        [Test]
        public void RespawnAi2Lock_OldMapStartIsUnchanged()
        {
            MapBakeResult map = MapW();
            ContentPack pack = LoadW();
            M6World w = WorldW(map, pack);
            ulong run = Run(w, 300);
            TestContext.Out.WriteLine($"content {w.Recipe.Match.PolicyDigest}UL run {run}UL units {w.Recipe.Units.Length}");
            Assert.That(w.Recipe.Match.PolicyDigest, Is.EqualTo(ContentDigest), "match digest");
            Assert.That(w.Recipe.Units.Length, Is.EqualTo(6), "the map's own units");
            Assert.That(run, Is.EqualTo(MapRun300), "300-tick run");
        }
    }
}
