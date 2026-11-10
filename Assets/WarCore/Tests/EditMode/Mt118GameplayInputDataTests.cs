using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Input")]
    public sealed class Mt118GameplayInputDataTests
    {
        [Test] public void AuthorityRejectsDuplicateActorsAtFirstSortedPair()
        {
            Assert.Throws<SimBoundaryException>(()=>new GameplayInputAuthority(1,
                new[]{M0c2S3Fixture.Actor.Id,M0c2S3Fixture.Actor.Id}));
        }
    }
}
