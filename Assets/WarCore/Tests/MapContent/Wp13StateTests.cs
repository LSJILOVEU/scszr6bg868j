using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp13StateTests
    {
        [TestCase(20, false)]
        [TestCase(20, true)]
        [TestCase(100, false)]
        [TestCase(100, true)]
        public void ControlledSnapshotMagazineAndReload_RestoreReverseAndContinue(int saveTick, bool reverseAllocation)
        {
            var a = new Wp13TestWorld(); var b = new Wp13TestWorld(reverse: true);
            a.Hold(); b.Hold();
            Assert.That(b.Unit(0), Is.Not.EqualTo(a.Unit(0)), "physical slot order must differ");
            for (int i = 0; i < saveTick; i++) a.Step();
            if (saveTick == 20) Assert.That(a.State(0).Ammo, Is.InRange(1, 29));
            else
            {
                Assert.That(a.State(0).Ammo, Is.EqualTo(30));
                Assert.That(a.State(0).DueMilli, Is.GreaterThan((saveTick + 50) * 1000));
            }
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            Wp13IntentTests.Seed(b, FireIntent.Aimed(b.Id(0), b.Point(1)), saveTick + 1);
            b.Control.Selected = b.Id(1);
            b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: reverseAllocation);
            int first = b.Id(0).CompareTo(b.Id(1)) < 0 ? 0 : 1;
            Assert.That(b.Unit(first).Id, Is.EqualTo(reverseAllocation ? 1 : 0), "restored slot follows stable identity order");
            Assert.That(b.Unit(1 - first).Id, Is.EqualTo(reverseAllocation ? 0 : 1));
            Wp13IntentTests.Empty(b); Assert.That(b.Control.Current, Is.EqualTo(b.Unit(0)));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()));
            Assert.That(b.Combat.Fires.Length, Is.Zero);
            for (int tick = saveTick + 1; tick <= 450; tick++)
            {
                a.Step(); b.Step();
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + tick);
                Wp13TestWorld.EqualCombat(a, b);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ControlModeSnapshotsRejectedBothDirections(bool sourceControlled)
        {
            var source = new Wp13TestWorld(controlled: sourceControlled);
            var target = new Wp13TestWorld(controlled: !sourceControlled, reverse: true);
            source.Step();
            Assert.Catch<InvalidOperationException>(() => target.Sim.RestoreRetainedSnapshot(source.Sim.SaveRetainedSnapshot()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullControlSaveStateAndDirectWriteStateMatchPreWp13Encoding(bool walls)
        {
            var pack = RwrPack.Create();
            var w = new Wp13TestWorld(pack: pack, controlled: false, map: walls ? M2DemoMap.Build() : null);
            // The first eight bytes are the WP-17 policy digest (0x8BE7D7DFA58533CD) in little-endian order; the rest of the
            // layout is unchanged from the old CombatSystem encoding. Hashes follow StateHash's FNV-1a byte protocol
            // and were recalculated independently, not taken from SaveState itself.
            var expected = new byte[] { 0xcd, 0x33, 0x85, 0xa5, 0xdf, 0xd7, 0xe7, 0x8b,
                (byte)(walls ? 2 : 0), 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 };
            ulong expectedHash = walls ? 0xE12007D7952A7AC3UL : 0x4323DC476F004A41UL;
            Assert.That(pack.PolicyDigest, Is.EqualTo(0x8BE7D7DFA58533CDUL));
            Assert.That(w.Combat.StateVersion, Is.EqualTo(1));
            for (int i = 0; i < 2; i++)
            {
                var writer = new SimStateWriter(); w.Combat.SaveState(writer);
                Assert.That(writer.ToArray(), Is.EqualTo(expected));
                Assert.That(Wp13TestWorld.Hash(w.Combat), Is.EqualTo(expectedHash));
                var reader = new SimStateReader(expected); w.Combat.LoadState(reader); reader.End();
                if (i == 0) w.Step();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingIntentDoesNotChangeDirectWriteState(bool controlled)
        {
            var w = new Wp13TestWorld(controlled: controlled);
            ulong before = Wp13TestWorld.Hash(w.Combat);
            w.Produce = c =>
            {
                w.Target(c, 0, 1);
                Assert.That((int)Wp13IntentTests.Field("intentCount").GetValue(w.Combat), Is.EqualTo(1));
                Assert.That(Wp13TestWorld.Hash(w.Combat), Is.EqualTo(before), "direct WriteState excludes pending intent");
                w.Aim(c, 0, w.Point(0) + SimVec3.Metres(0, 0, 1));
                Assert.That(Wp13TestWorld.Hash(w.Combat), Is.EqualTo(before), "overwrite also stays transient");
            };
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(default(StableId)));
            Assert.That(Wp13TestWorld.Hash(w.Combat), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LoadStateClearsInjectedPendingIntentAndTicks(bool invalidSnapshot)
        {
            var w = new Wp13TestWorld(); var writer = new SimStateWriter(); w.Combat.SaveState(writer);
            Wp13IntentTests.Seed(w, FireIntent.AtTarget(w.Id(0), w.Id(1)), 1);
            byte[] bytes = writer.ToArray(); if (invalidSnapshot) bytes[0] ^= 1;
            if (invalidSnapshot) Assert.Throws<InvalidOperationException>(() => w.Combat.LoadState(new SimStateReader(bytes)));
            else { var reader = new SimStateReader(bytes); w.Combat.LoadState(reader); reader.End(); }
            Wp13IntentTests.Empty(w);
            w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero, "load cannot leave a current-tick shot behind");
        }

        [Test]
        public void PLACEHOLDER_ScriptReplayRepeatedAndReverseCreation_EveryTickHash()
        {
            var a = new Wp13TestWorld(); var b = new Wp13TestWorld(); var reverse = new Wp13TestWorld(reverse: true);
            Script(a); Script(b); Script(reverse);
            Assert.That(reverse.Unit(0), Is.Not.EqualTo(a.Unit(0)));
            int shots = 0;
            for (int tick = 1; tick <= 600; tick++)
            {
                a.Step(); b.Step(); reverse.Step(); shots += a.Combat.Fires.Length;
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "repeat tick=" + tick);
                Assert.That(reverse.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "reverse tick=" + tick);
                Wp13TestWorld.EqualCombat(a, b); Wp13TestWorld.EqualCombat(a, reverse);
            }
            Assert.That(shots, Is.GreaterThan(30));
        }

        private static void Script(Wp13TestWorld w)
        {
            w.Produce = c =>
            {
                switch (c.Clock.Tick % 7)
                {
                    case 0: w.Aim(c, 0, w.Point(1)); break;
                    case 1: w.Aim(c, 0, w.Point(0) + SimVec3.Metres(0, 0, 1)); break;
                    case 2: w.Target(c, 0, 1); break;
                    case 3: w.Aim(c, 0, w.Point(0)); break;
                    case 4: w.Target(c, 0, 1); w.Aim(c, 0, w.Point(0)); break;
                    case 5: w.Aim(c, 0, w.Point(0)); w.Target(c, 0, 1); break;
                }
            };
        }
    }
}
