using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    public sealed class Mt155MatchFeatureTests
    {
        [Test]
        public void OneTickRespawnSwitchesToReserveOnNextTick()
        {
            const ulong reserve = 3;
            var w = World(Units(U(reserve, 0, 0, reserve: true)),
                Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), 1));
            Wp16TestWorld.KillAt(w, 1, Player);

            w.Step();
            Assert.That(w.Dead(Player), Is.True);
            Assert.That(Controlled(w), Is.EqualTo(Player));

            w.Step();
            Assert.That(Controlled(w), Is.EqualTo(reserve));
            Assert.That(Wp16TestWorld.Switches(w), Is.EqualTo(1));
        }

        [TestCase("failedSlotTicks", "", TestName = "OneTickFailedSlotMemoryChangesSnapshotManifest")]
        [TestCase("targetMemoryTicks", "", TestName = "OneTickTargetMemoryChangesSnapshotManifest")]
        [TestCase("searchTicks", "\"targetMemoryTicks\": 2, ", TestName = "OneTickTargetSearchChangesSnapshotManifest")]
        [TestCase("suppressTicks", "\"targetMemoryTicks\": 2, ", TestName = "OneTickSuppressiveFireChangesSnapshotManifest")]
        public void OneTickAiSystemRejectsDisabledSnapshot(string field, string prerequisite)
        {
            M6World enabled = L4.A(10, "{ " + prerequisite + "\"" + field + "\": 1 }");
            M6World disabled = L4.A(10, "{ " + prerequisite + "\"" + field + "\": 0 }");
            byte[] snapshot = disabled.Sim.SaveRetainedSnapshot();

            var error = Assert.Throws<SimSnapshotException>(() => enabled.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("System registration manifest count mismatch."));
        }
    }
}
