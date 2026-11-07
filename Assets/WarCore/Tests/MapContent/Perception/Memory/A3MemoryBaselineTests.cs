using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1(a), recorded on the code before A3: fixture D of A2 (A2Fixture.cs D: damage 6554, walls, grass without
    // cover) with the A2 baseline layer. The system table, the 600-tick run (A2's recorded constants) and the retained
    // snapshots at ticks 300 and 600 (length and 64-bit FNV-1a digest) must stay byte for byte the same.
    public sealed class A3MemoryBaselineTests
    {
        internal const string Systems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        internal const int Snapshot300Length = 6648, Snapshot600Length = 6648;
        internal const ulong Snapshot300Digest = 6830075355491636465UL, Snapshot600Digest = 4684838110205563524UL;

        [Test]
        public void NoKey_LayerIsA2s()
        {
            Assert.That(A3.PackJson("", (int?)null), Is.EqualTo(A2.PackJson()));
        }

        [TestCase(null)]
        public void FixtureD_Unchanged(int? memory)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = A2.D(A3.Pack("", memory)));
            Assert.That(L4.Systems(w), Is.EqualTo(Systems));
            int fires = 0;
            ulong fold = 14695981039346656037UL;
            for (int tick = 0; tick <= 600; tick++)
            {
                if (tick > 0) { w.Step(); fires += w.Combat.Fires.Length; }
                fold = (fold ^ w.Hash) * 1099511628211UL;
                if (tick == 300 || tick == 600)
                {
                    byte[] snapshot = w.Sim.SaveRetainedSnapshot();
                    Assert.That(snapshot.Length, Is.EqualTo(tick == 300 ? Snapshot300Length : Snapshot600Length), "length, tick " + tick);
                    Assert.That(A3.Digest(snapshot), Is.EqualTo(tick == 300 ? Snapshot300Digest : Snapshot600Digest), "digest, tick " + tick);
                }
            }
            Assert.That(fold, Is.EqualTo(A2BaselineTests.DRun600));
            Assert.That(fires, Is.EqualTo(A2BaselineTests.DFires600));
        }
    }
}
