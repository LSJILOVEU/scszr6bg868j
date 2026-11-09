using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // C1c (9) snapshots and (10) the gate contract.
    public sealed class C1cSaveAndGateTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void C1cSave_InsideTheWindow_RestoresToTheSameRun(bool reverse)
        {
            var original = C1c.World(C1c.ShooterUnits(), C1c.Pack(10));
            original.Step(8);
            byte[] saved = original.Sim.SaveRetainedSnapshot();
            var restored = C1c.World(C1c.ShooterUnits(), C1c.Pack(10), reverse: reverse);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: reverse);
            Assert.That(restored.Hash, Is.EqualTo(original.Hash));
            for (long tick = 9; tick <= 28; tick++)
            {
                original.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(original.Hash), "tick " + tick);
                Assert.That(restored.Dead(C1c.Reserve), Is.EqualTo(original.Dead(C1c.Reserve)), "tick " + tick);
                Assert.That(restored.Dead(C1c.Reserve), Is.EqualTo(tick >= 16), "tick " + tick);
            }
        }

        private static byte[] SavedAt8(int guard)
        {
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(guard));
            w.Step(8);
            return w.Sim.SaveRetainedSnapshot();
        }

        // On and off differ in the respawn system's state version, which the snapshot manifest checks first.
        [TestCase(10, 0, "v2", "v1")]
        [TestCase(0, 10, "v1", "v2")]
        public void C1cSave_GuardOnAndOff_AreRefusedByTheManifest(int saved, int into, string snapshotVersion, string registeredVersion)
        {
            byte[] bytes = SavedAt8(saved);
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(into));
            var e = Assert.Throws<SimSnapshotException>(() => w.Sim.RestoreRetainedSnapshot(bytes));
            StringAssert.Contains("battle.respawn", e.Message);
            StringAssert.Contains("snapshot=battle.respawn " + snapshotVersion, e.Message);
            StringAssert.Contains("registered=battle.respawn " + registeredVersion, e.Message);
        }

        // Two guards that are both on share the state version; the pack digest refuses the restore.
        [Test]
        public void C1cSave_AnotherGuardLength_IsRefusedByThePackDigest()
        {
            byte[] bytes = SavedAt8(10);
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(11));
            var e = Assert.Throws<InvalidOperationException>(() => w.Sim.RestoreRetainedSnapshot(bytes));
            Assert.That(e.Message, Is.EqualTo("Battle configuration differs from snapshot."));
        }

        private sealed class Never : IDamageGuard { public bool Shields(StableId victim, long tick) => false; }

        private static object Gate(M6World w) =>
            typeof(CombatSystem).GetField("guard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w.Combat);

        [Test]
        public void C1cGate_NullAndASecondBinding_AreRefused_TheFirstStays()
        {
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(10));
            object first = Gate(w);
            Assert.That(first, Is.InstanceOf<RespawnSystem>());
            Assert.Throws<ArgumentNullException>(() => w.Combat.SetDamageGuard(null));
            Assert.Throws<InvalidOperationException>(() => w.Combat.SetDamageGuard(new Never()));
            Assert.That(Gate(w), Is.SameAs(first));
            w.Step(C1c.Switch);
            Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve), Is.EqualTo(new[] { 0L }), "the first binding still shields");
        }

        [Test]
        public void C1cGate_NothingIsShieldedBeforeTheFirstSwitch()
        {
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(10));
            var guard = (IDamageGuard)Gate(w);
            for (long tick = 1; tick <= C1c.Switch - 1; tick++)
            {
                w.Step();
                foreach (ulong key in new[] { Player, Ally, Enemy, C1c.Reserve, C1c.Shooter })
                    Assert.That(guard.Shields(w.Sim.Entities.StableOf(w.Unit(key)), tick), Is.False, "key " + key + ", tick " + tick);
            }
        }

        [Test]
        public void C1cGate_NoGuard_NothingIsBound()
        {
            Assert.That(Gate(C1c.World(C1c.ShooterUnits(), C1c.Pack(0))), Is.Null);
            Assert.That(Gate(RespawnWorld.World(Units(U(C1c.Reserve, 0, 0, reserve: true)), Pack(C1c.PointA(), C1c.BaseA(), C1c.D))), Is.Null);
        }
    }
}
