using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // Acceptance 13: the burst with the target memory (A3), the search (A3a), the reaction time and the sense interval in
    // one world: R = 15, K = 10, B = 3, Q = 20, memory 300, search 120. B walks in and stays in sight, so the memory and
    // the search never trigger; the three systems coexist, both allocation orders hash alike for 600 ticks and A's shots
    // follow the burst model from 61 = 46 + 15.
    public sealed class P3CombinedTests
    {
        private const string Ai = "{ \"targetMemoryTicks\": 300, \"searchTicks\": 120, \"reactionTicks\": 15, \"senseTicks\": 10, "
            + "\"burstShots\": 3, \"burstPauseTicks\": 20 }";
        internal static readonly long[] FiresA =
        {
            61, 66, 71, 91, 96, 101, 121, 126, 131, 151, 211, 216, 221, 241, 246, 251, 271, 276, 281, 301,
            361, 366, 371, 391, 396, 401, 421, 426, 431, 451, 511, 516, 521, 541, 546, 551, 571, 576, 581,
        };

        [Test]
        public void ThreeSystems_RunTogether_BothOrders()
        {
            P3.Trace forward = null, reverse = null;
            Assert.DoesNotThrow(() => forward = P3.Run(P2.Cast(), P1.Walk, Ai, 600));
            Assert.DoesNotThrow(() => reverse = P3.Run(P2.Cast(), P1.Walk, Ai, 600, true));
            Assert.That(P3.Systems(forward.W), Is.EqualTo(P2CombinedTests.Systems));
            Assert.That(P3.Systems(forward.W), Does.Contain("battle.ai-contacts|0|13;"));
            Assert.That(P3.Systems(forward.W), Does.Contain("battle.ai-fire|0|14;"));
            Assert.That(P3.Systems(forward.W), Does.Contain("battle.ai-search|0|17;"));
            for (int t = 0; t <= 600; t++) Assert.That(reverse.Hashes[t], Is.EqualTo(forward.Hashes[t]), "tick " + t);
            ulong b = forward.Id(P3.KeyB);
            for (long t = P1.T; t <= 600; t++) Assert.That(forward.A.Sel[(int)t], Is.EqualTo(b), "B stays selectable, tick " + t);
            List<long> fires = forward.A.FireTicks(600);
            Assert.That(fires, Is.EqualTo(FiresA));
            Assert.That(fires, Is.EqualTo(P3.Expect(P2TimingTests.TsA + 15, 3, 20, 600)), "the model");
            Assert.That(reverse.A.FireTicks(600), Is.EqualTo(fires), "the same shots in the reverse order");
        }
    }
}
