using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt094StableInputTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void BoundInputPreservesDistinctFields(bool restoreSnapshot)
        {
            var sim = StableSnapshotTests.Scene(false);
            SimVec3 move = SimVec3.Metres(1,0,0);
            SimVec3 aim = SimVec3.Metres(0,0,1);
            sim.StepStable(new StableInputFrame(new InputFrame(move,aim,
                primaryAttack:true,block:false,@throw:true)));
            if (restoreSnapshot)
                sim.RestoreStableSnapshot(sim.SaveStableSnapshot(),reverseAllocation:true);

            InputFrame input = sim.Context.Input;
            Assert.That(input.Move,Is.EqualTo(move));
            Assert.That(input.Aim,Is.EqualTo(aim));
            Assert.That(input.PrimaryAttack,Is.True);
            Assert.That(input.Block,Is.False);
            Assert.That(input.Throw,Is.True);
        }
    }
}
