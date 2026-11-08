using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1(b), recorded on the code before A3a: the A3a fixture with the target memory on (M = 60) and the search
    // left out. The system table, the 600-tick run (64-bit FNV-1a over the per-tick hashes) and the retained snapshots at
    // ticks 300 and 600 must stay byte for byte the same with "searchTicks" left out and written as 0 (1(b)).
    public sealed class A3aBaselineTests
    {
        internal const string Systems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "battle.ai-contacts|0|13;interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        internal const ulong Run600 = 10947695167728215057UL;
        internal const int Snapshot300Length = 5583, Snapshot600Length = 5583;
        internal const ulong Snapshot300Digest = 4501634821204233972UL, Snapshot600Digest = 11017044853079304422UL;

        internal static void Check(string ai)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = A3a.World(A3a.BStart, ai));
            Assert.That(L4.Systems(w), Is.EqualTo(Systems));
            ulong fold = 14695981039346656037UL;
            for (long tick = 0; tick <= 600; tick++)
            {
                if (tick > 0) w.Sim.Step(A3a.Drive(w, A3a.Script(tick, A3a.BHide)));
                fold = (fold ^ w.Hash) * 1099511628211UL;
                if (tick == 300 || tick == 600)
                {
                    byte[] snapshot = w.Sim.SaveRetainedSnapshot();
                    Assert.That(snapshot.Length, Is.EqualTo(tick == 300 ? Snapshot300Length : Snapshot600Length), "length, tick " + tick);
                    Assert.That(A3a.Digest(snapshot), Is.EqualTo(tick == 300 ? Snapshot300Digest : Snapshot600Digest), "digest, tick " + tick);
                }
            }
            Assert.That(fold, Is.EqualTo(Run600));
        }

        [Test]
        public void NoSearchKey_Unchanged() => Check(A3a.Ai(60));

        [Test]
        public void SearchZero_Unchanged() => Check(A3a.Ai(60, 0));

        // 1(c): a search length without the memory adds nothing: A2's fixture D runs exactly as recorded by A2.
        [Test]
        public void SearchWithoutMemory_FixtureDUnchanged()
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = A2.D(A3.Load(A3.PackJson("", "{ \"searchTicks\": 60 }"))));
            Assert.That(L4.Systems(w), Is.EqualTo(A3MemoryBaselineTests.Systems));
            int fires = 0;
            ulong fold = 14695981039346656037UL;
            for (int tick = 0; tick <= 600; tick++)
            {
                if (tick > 0) { w.Step(); fires += w.Combat.Fires.Length; }
                fold = (fold ^ w.Hash) * 1099511628211UL;
            }
            Assert.That(fold, Is.EqualTo(A2BaselineTests.DRun600));
            Assert.That(fires, Is.EqualTo(A2BaselineTests.DFires600));
        }
    }
}
