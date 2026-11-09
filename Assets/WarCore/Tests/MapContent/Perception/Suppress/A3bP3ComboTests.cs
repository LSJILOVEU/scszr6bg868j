using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 9(e) and 8 (eight fields), added after P3 (burst length and pause) merged: the suppression goes through
    // intents and is not gated by the burst pause; the automatic fire that takes over afterwards starts its burst count
    // from zero. Fixed parameters: M 300, R 15, B 2, Q 45, D 100, main fixture, B back at its start on 150.
    public sealed class A3bP3ComboTests
    {
        private const string WithSuppress = "{ \"targetMemoryTicks\": 300, \"reactionTicks\": 15, \"burstShots\": 2, \"burstPauseTicks\": 45, \"suppressTicks\": 100 }";
        private const string WithoutSuppress = "{ \"targetMemoryTicks\": 300, \"reactionTicks\": 15, \"burstShots\": 2, \"burstPauseTicks\": 45 }";
        private const string NoPause = "{ \"targetMemoryTicks\": 300, \"reactionTicks\": 15, \"burstShots\": 2, \"burstPauseTicks\": 0, \"suppressTicks\": 100 }";
        private const string ZeroSuppress = "{ \"targetMemoryTicks\": 300, \"reactionTicks\": 15, \"burstShots\": 2, \"burstPauseTicks\": 45, \"suppressTicks\": 0 }";
        // The tick-300 snapshot of WithoutSuppress, recorded on the main branch with P3 merged and no A3b code (see the PR).
        internal const int Length300 = 5722;
        internal const ulong Digest300 = 5409104777400161549UL;

        // P3's burst count of A's entry (private array indexed by entity slot).
        private static int Shots(M6World w) =>
            ((int[])typeof(ReactionFire).GetField("shots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<ReactionFire>()))[A3b.A(w).Id];
        private static List<long> Within(List<long> fires, long from, long to) => fires.FindAll(t => t >= from && t <= to);

        // (i)(ii): inside the suppression window [56, 155] the shots are those of the world without P3 ({76, 136}); the same
        // parameters without the suppression fire nothing there.
        [Test]
        public void Window_PauseDoesNotGateTheSuppression()
        {
            Assert.That(A3b.World(WithSuppress).System<ReactionFire>().StateVersion, Is.EqualTo(3));
            List<long> with = A3b.Run(A3b.World(WithSuppress), 400, t => A3b.Back(t));
            Assert.That(Within(with, 56, 155), Is.EqualTo(A3b.Ticks(76, 136)));
            List<long> plain = A3b.Run(A3b.World(A3b.Ai(300, 100, reaction: 15)), 400, t => A3b.Back(t));
            Assert.That(Within(plain, 56, 155), Is.EqualTo(A3b.Ticks(76, 136)));
            List<long> without = A3b.Run(A3b.World(WithoutSuppress), 400, t => A3b.Back(t));
            Assert.That(Within(without, 56, 155), Is.Empty);
            Assert.That(Within(without, 1, 55), Is.EqualTo(Within(with, 1, 55)));
        }

        // (iii)(iv): after B is seen again (reaction entry from 181, suppression entry gone on 186) the automatic fire
        // counts its burst from zero: 1 after the first shot (196); the second (226) empties the magazine, which P3 counts
        // as the pause and resets the count to 0; after the reload 286 and 316 count 1 and 2 and the next shot waits for
        // the pause (361 = 316 + 45); with Q = 0 it follows the weapon alone (346 = 316 + 30).
        [TestCase(WithSuppress, true)]
        [TestCase(NoPause, false)]
        public void AfterRecovery_BurstCountsFromZero(string ai, bool pause)
        {
            var shotsAfter = new Dictionary<long, int>();
            List<long> fires = A3b.Run(A3b.World(ai), 400, t => A3b.Back(t), null, (x, t) =>
            {
                if (t >= 181 && t <= 200) Assert.That(A3b.FireCount(x), Is.EqualTo(1), "reaction count, tick " + t);
                if (t == 185) Assert.That(A3b.Count(x), Is.EqualTo(1));
                if (t >= 186) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
                if (t >= 186 && A3b.Fired(x)) shotsAfter[t] = Shots(x);
            });
            List<long> after = Within(fires, 186, 400);
            Assert.That(after, Is.EqualTo(pause ? A3b.Ticks(196, 226, 286, 316, 361) : A3b.Ticks(196, 226, 286, 316, 346)));
            Assert.That(new[] { shotsAfter[196], shotsAfter[226], shotsAfter[286], shotsAfter[316] }, Is.EqualTo(new[] { 1, 0, 1, 2 }));
            Assert.That(shotsAfter[after[4]], Is.EqualTo(0));
            Assert.That(after[4] - after[3], Is.EqualTo(pause ? 45L : 30L));
            Assert.That(after[1] - after[0], Is.EqualTo(30L));
        }

        // (v): D = 0 beside P3's fields changes nothing: the same hash every tick, the same tick-300 snapshot bytes, version 3,
        // and the snapshot recorded on main with P3 and without this package.
        [Test]
        public void ZeroSuppress_ByteIdenticalToP3()
        {
            M6World off = A3b.World(WithoutSuppress), zero = A3b.World(ZeroSuppress);
            Assert.That(L4.Systems(zero), Is.EqualTo(L4.Systems(off)));
            Assert.That(L4.Systems(zero), Does.Not.Contain("battle.ai-suppress"));
            Assert.That(zero.System<ReactionFire>().StateVersion, Is.EqualTo(3));
            for (long t = 1; t <= 400; t++)
            {
                A3b.Step(off, A3b.Back(t)); A3b.Step(zero, A3b.Back(t));
                Assert.That(zero.Hash, Is.EqualTo(off.Hash), "tick " + t);
                if (t == 300)
                {
                    byte[] a = off.Sim.SaveRetainedSnapshot(), b = zero.Sim.SaveRetainedSnapshot();
                    Assert.That(b, Is.EqualTo(a));
                    Assert.That(a.Length, Is.EqualTo(Length300));
                    Assert.That(A3.Digest(a), Is.EqualTo(Digest300));
                }
            }
        }

        // (v) with K = 3: the same beside a sense length, so the zero default is byte-identical to P3 whatever the other
        // fields are; the tick-300 snapshot was recorded on the same main branch (fires 18, 48, 198, 258, 288, 333, 393).
        [Test]
        public void ZeroSuppress_WithSense_ByteIdenticalToP3()
        {
            const string off = "{ \"targetMemoryTicks\": 300, \"reactionTicks\": 15, \"senseTicks\": 3, \"burstShots\": 2, \"burstPauseTicks\": 45 }";
            const string zero = "{ \"targetMemoryTicks\": 300, \"reactionTicks\": 15, \"senseTicks\": 3, \"burstShots\": 2, \"burstPauseTicks\": 45, \"suppressTicks\": 0 }";
            M6World a = A3b.World(off), b = A3b.World(zero);
            Assert.That(L4.Systems(b), Is.EqualTo(L4.Systems(a)));
            Assert.That(L4.Systems(b), Does.Not.Contain("battle.ai-suppress"));
            var fires = new List<long>();
            for (long t = 1; t <= 400; t++)
            {
                A3b.Step(a, A3b.Back(t)); A3b.Step(b, A3b.Back(t));
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + t);
                if (A3b.Fired(a)) fires.Add(t);
                if (t == 300)
                {
                    byte[] x = a.Sim.SaveRetainedSnapshot(), y = b.Sim.SaveRetainedSnapshot();
                    Assert.That(y, Is.EqualTo(x));
                    Assert.That(x.Length, Is.EqualTo(5722));
                    Assert.That(A3.Digest(x), Is.EqualTo(12323171008582189071UL));
                }
            }
            Assert.That(fires, Is.EqualTo(A3b.Ticks(18, 48, 198, 258, 288, 333, 393)));
        }

        // 8: the eight fields of the ai section written together each read their value.
        [Test]
        public void EightFields_EachRead()
        {
            AiRules r = A3.Load(A3.PackJson(A3.Grass500, "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 300, \"searchTicks\": 60, "
                + "\"reactionTicks\": 15, \"senseTicks\": 3, \"burstShots\": 2, \"burstPauseTicks\": 45, \"suppressTicks\": 100 }")).Section<AiRules>();
            Assert.That(r.FailedSlotTicks, Is.EqualTo(7)); Assert.That(r.TargetMemoryTicks, Is.EqualTo(300)); Assert.That(r.SearchTicks, Is.EqualTo(60));
            Assert.That(r.ReactionTicks, Is.EqualTo(15)); Assert.That(r.SenseTicks, Is.EqualTo(3));
            Assert.That(r.BurstShots, Is.EqualTo(2)); Assert.That(r.BurstPauseTicks, Is.EqualTo(45)); Assert.That(r.SuppressTicks, Is.EqualTo(100));
        }
    }
}
