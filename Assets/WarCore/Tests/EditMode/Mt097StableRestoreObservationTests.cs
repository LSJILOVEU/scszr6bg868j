using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt097StableRestoreObservationTests
    {
        [Test]
        public void PhasesHaveDistinctOrderedDiagnosticIndices()
        {
            var indices = new[]
            {
                (int)StableRestorePhase.Begin,
                (int)StableRestorePhase.IdentityMapped,
                (int)StableRestorePhase.ComponentsBound,
                (int)StableRestorePhase.ReferencesBound,
                (int)StableRestorePhase.SpatialRebuilt,
                (int)StableRestorePhase.Complete
            };
            Assert.That(indices, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
        }
    }
}
