using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt056RayCellTraversalTests
    {
        [Test]
        public void ZeroRangePassesLimitGuardAndVisitsOnlyOriginCell()
        {
            var request = SpatialQueries.Ray(SimVec3.Metres(1, 1, 1), SimVec3.Metres(1, 0, 0), default);
            // Invoke the conditional guard directly so release builds also check the zero endpoint.
            Assert.DoesNotThrow(() => BoundarySiteTests.Call("RayCellTraversal", "AssertLimits", request));

            var traversal = new RayCellTraversal(in request, 4 * SimFix.Scale);
            Assert.That(traversal.MoveNext(out RayCellEvent cell), Is.True);
            Assert.That((cell.X, cell.Y, cell.Z), Is.EqualTo((0L, 0L, 0L)));
            Assert.That((cell.Numerator, cell.Denominator), Is.EqualTo((0UL, 1UL)));
            Assert.That(traversal.MoveNext(out _), Is.False);
        }
    }
}
