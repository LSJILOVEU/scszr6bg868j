using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.RespawnAi1World;

namespace WarCore.Tests
{
    // RESPAWN-AI-1 A1, recorded on the code before RESPAWN-AI-1 (main 181a0be) with only the old API: the fixture with an enemy
    // standing in P's circle, no switch written and no chooser bound. The pack digest, the 300-tick run (64-bit FNV-1a over the
    // per-tick retained hashes), the retained snapshot at tick 300 (length and digest) and the reinforcement state (version,
    // bytes, hash) are as recorded: the new code without the switch and without a chooser changes none of them.
    public sealed class RespawnAi1LockTests
    {
        internal const ulong PackDigest = 16108698768566864724UL, Run300 = 4447535668409429734UL, Snapshot300Digest = 13717087973737337045UL,
            ReinforceHash300 = 13630957909965752645UL;
        internal const int Snapshot300Length = 15742;

        private static M6World Contested() => World(Units(U(Contester, 41000, 21000, 2)), OldPack(P()));

        [Test]
        public void RespawnAi1Lock_OldWorldIsUnchanged()
        {
            M6World w = Contested();
            ulong run = Run(w, 300);
            byte[] snapshot = w.Sim.SaveRetainedSnapshot();
            byte[] saved = Saved(w.Reinforce);
            TestContext.Out.WriteLine($"pack {w.Recipe.Match.PolicyDigest}UL run {run}UL snap {snapshot.Length} {A3.Digest(snapshot)}UL reinforce {HashOf(w.Reinforce)}UL bytes {saved.Length}");
            Assert.That(w.Recipe.Match.PolicyDigest, Is.EqualTo(PackDigest), "pack digest");
            Assert.That(run, Is.EqualTo(Run300), "300-tick run");
            Assert.That((snapshot.Length, A3.Digest(snapshot)), Is.EqualTo((Snapshot300Length, Snapshot300Digest)), "snapshot at 300");
            Assert.That((w.Reinforce.StateVersion, saved.Length, HashOf(w.Reinforce)), Is.EqualTo((1, 64, ReinforceHash300)), "reinforcement state");
            Assert.That((w.Reinforce.Attempts(1), w.Reinforce.Successes(1)), Is.EqualTo((10L, 6L)), "ten attempts, six reserves");
        }

        // The old rotation: with the enemy in P's circle and no switch, team 1's second and fourth reinforcements still come out at P.
        [Test]
        public void RespawnAi1Lock_WithoutTheSwitchTheContestedPointStillDeploys()
        {
            var deploys = Deploys(Contested(), 1, 4 * Interval);
            Assert.That(deploys.Count, Is.EqualTo(4));
            Assert.That(new[] { Near(deploys[0].point, B1), Near(deploys[1].point, PCentre), Near(deploys[2].point, B1), Near(deploys[3].point, PCentre) },
                Is.EqualTo(new[] { true, true, true, true }));
            Assert.That(new[] { deploys[0].key, deploys[1].key, deploys[2].key, deploys[3].key }, Is.EqualTo(new ulong[] { 11, 12, 16, 13 }), "the first reserve in Capture() order");
        }
    }
}
