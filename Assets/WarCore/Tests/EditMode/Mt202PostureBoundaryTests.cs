using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt202PostureBoundaryTests
    {
        [TestCase(-1, 2)]
        [TestCase(1, -1)]
        [TestCase(1, 1)]
        public void StableTransitionRejectsInvalidPostureIds(int currentId, int targetId)
        {
            var task = new TaskKey(1, 1, new StableId(1), 0, 1);
            Assert.Throws<SimBoundaryException>(() => new Posture(currentId, targetId, task));
        }

        [Test]
        public void StableTransitionRejectsDefaultTask()
        {
            Assert.Throws<SimBoundaryException>(() => new Posture(1, 2, default(TaskKey)));
        }

        [Test]
        public void StableWriterRejectsLegacyTransition()
        {
            var writer = new SimStateWriter(new EntityStore(1, IdentityMode.StableWorld), materialize: true);
            var value = new Posture(1, 2, 3, 4);
            Assert.Throws<SimBoundaryException>(() => new PostureSerializer().Write(writer, value));
        }
    }
}
