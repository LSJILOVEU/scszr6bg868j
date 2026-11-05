using NUnit.Framework;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4LongRun")]
    public sealed class Round4LongRunTests
    {
        [Test, Timeout(900000)]
        public void OneMillionCyclesKeepRecordsSnapshotsAndDirectAllocationBounded()
        {
            Round4LongRun.Run(TestContext.Out);
        }
    }
}
