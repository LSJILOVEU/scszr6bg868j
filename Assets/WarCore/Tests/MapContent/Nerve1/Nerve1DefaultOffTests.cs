using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests.Nerve1
{
    // NERVE-1 N1: a world without combat.status keeps every byte. The values below were recorded on the trunk before any
    // NERVE-1 change: the AI-S3 battle (seed 41, fair decisions, the S3 pressure rules), every tick's retained state hash
    // folded into one number over the first 600 ticks and again over 1200 (nobody fires before tick 600 in this battle, the
    // first shots come before tick 900), the last tick's hash, and how many numbers the combat.spread stream drew.
    public sealed class Nerve1DefaultOffTests
    {
        private const string S3 = "{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, "
            + "\"boundMinRangeMm\": 12000, \"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, "
            + "\"pressureNearShock\": 1000, \"pressureDecayPermille\": 12, \"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, "
            + "\"pressurePinnedAt\": 2000, \"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300 }";
        internal const int Ticks = 1200, FirstTicks = 600;
        internal const ulong RecordedFold600 = 17597267719018084143UL, RecordedFold = 6557270533606239120UL, RecordedLast = 16544931010556045423UL;
        internal const long RecordedSpreadDraws = 407;

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["s3-test"] = "{ \"id\": \"s3-test\", \"extends\": \"rwr\", \"ai\": " + ai + " }"
        }, FileContentSource.Rwr()), "s3-test");

        internal static M6World Battle() =>
            new M6World(FairEFixture.Recipe("control", 41).WithPack(Pack(FairEFixture.Ai(S3, true))), FairEFixture.Map("control"));

        // How many numbers a stream drew since the world began: SplitMix64 adds one constant per number, and an int below
        // 65536 is one number (the rejection threshold of a power of two is 0).
        internal static long Draws(SimRandom rng, string stream)
        {
            const ulong Golden = 0x9E3779B97F4A7C15UL;
            ulong seed = (ulong)typeof(SimRandom).GetField("seed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rng);
            var fresh = new SimRandom(seed);
            fresh.Stream(stream);
            ulong start = State(fresh, stream), now = State(rng, stream);
            for (long n = 0; n < 10_000_000; n++)
            {
                if (start == now) return n;
                unchecked { start += Golden; }
            }
            return -1;
        }

        private static ulong State(SimRandom rng, string stream)
        {
            var streams = (IDictionary)typeof(SimRandom).GetField("streams", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rng);
            object state = streams[stream];
            return (ulong)state.GetType().GetProperty("SavedState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
        }

        internal static (ulong Fold600, ulong Fold, ulong Last, long Draws) Run(M6World w)
        {
            var fold = new StateHash();
            ulong last = 0, first = 0;
            for (int i = 0; i < Ticks; i++)
            {
                w.Step();
                last = w.Hash;
                fold.Add(last);
                if (i == FirstTicks - 1) first = fold.Value;
            }
            return (first, fold.Value, last, Draws(w.Sim.Rng, "combat.spread"));
        }

        [Test]
        public void N1_TheS3BattleKeepsEveryTickHashAndItsSpreadDraws()
        {
            M6World w = Battle();
            Assert.That(w.System<Pressure>(), Is.Not.Null, "the battle has the pressure system");
            var (fold600, fold, last, draws) = Run(w);
            Assert.That(fold600, Is.EqualTo(RecordedFold600), "fold600 " + fold600 + " fold " + fold + " last " + last + " draws " + draws);
            Assert.That(fold, Is.EqualTo(RecordedFold));
            Assert.That(last, Is.EqualTo(RecordedLast));
            Assert.That(draws, Is.EqualTo(RecordedSpreadDraws));
            Assert.That(w.Combat.HasNerveSource, Is.False, "without combat.status NerveFeature binds nothing");
            Assert.That(w.Combat.Pack.Status, Is.Null);
        }
    }
}
