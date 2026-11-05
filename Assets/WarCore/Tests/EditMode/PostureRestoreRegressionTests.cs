using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S35")]
    public sealed class PostureRestoreRegressionTests
    {
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PostureWithoutSpatialComponentsRestores(bool missingPosition, bool missingCollider)
        {
            var f = new PostureFixture();
            if (missingPosition)
                f.Sim.Entities.Remove(f.Positions, f.Unit);
            if (missingCollider)
                f.Sim.Entities.Remove(f.Colliders, f.Unit);
            ulong hash = f.Sim.StateHash();
            byte[] snapshot = f.Sim.SaveSnapshot();
            Assert.DoesNotThrow(() => f.Sim.RestoreSnapshot(snapshot));
            Assert.That(f.Sim.StateHash(), Is.EqualTo(hash));
        }

        [TestCase("missing")]
        [TestCase("id")]
        [TestCase("tick")]
        [TestCase("type")]
        [TestCase("entity")]
        [TestCase("target")]
        public void BrokenTransitionTaskRelationshipIsRejectedOnRestore(string mismatch)
        {
            var f = new PostureFixture();
            EntityId other = f.Add(10, 0, 1);
            long id = f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 5);
            f.Sim.Scheduler.Cancel(id);
            if (mismatch != "missing")
            {
                id = f.Sim.Scheduler.Schedule(mismatch == "tick" ? 6 : 5,
                    new SimEvent(mismatch == "type" ? new EventTypeId(999) : f.TransitionType,
                        target: mismatch == "entity" ? other : f.Unit, tag: mismatch == "target" ? 3 : 2));
                f.Sim.Entities.Set(f.Postures, f.Unit, new Posture(1, 2, 5, mismatch == "id" ? id + 1 : id));
            }
            byte[] snapshot = f.Sim.SaveSnapshot();
            Assert.Throws<SimSnapshotException>(() => f.Sim.RestoreSnapshot(snapshot));
            Assert.Throws<InvalidOperationException>(() => f.Sim.Step(default));
        }

        [Test]
        public void ValidTransitionRestoresThenCompletesAtItsOriginalDeadline()
        {
            var f = new PostureFixture();
            f.System.BeginTransition(f.Sim.Context, f.Unit, 2, 5);
            byte[] snapshot = f.Sim.SaveSnapshot();
            f.Sim.RestoreSnapshot(snapshot);
            for (int tick = 1; tick <= 5; tick++)
            {
                f.Sim.Step(default);
                Assert.That(f.Sim.Entities.Get(f.Postures, f.Unit).CurrentId, Is.EqualTo(tick < 5 ? 1 : 2));
            }
        }
    }
}
