using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt224StableInputTests
    {
        [Test]
        public void StepStablePreservesSquadNumber()
        {
            var sim = StableSnapshotTests.Scene(false);
            var command = new SquadCommand(SquadCommandKind.Hold,7,default,default);

            sim.StepStable(new StableInputFrame(new InputFrame(default,default,squad:command)));

            Assert.That(sim.Context.Input.Squad.Value.Squad,Is.EqualTo(7));
        }

        [Test]
        public void StepStablePreservesMovementAndAim()
        {
            var sim = StableSnapshotTests.Scene(false);
            SimVec3 move = SimVec3.Metres(1,0,0);
            SimVec3 aim = SimVec3.Metres(0,0,1);

            sim.StepStable(new StableInputFrame(new InputFrame(move,aim)));

            Assert.That(sim.Context.Input.Move,Is.EqualTo(move));
            Assert.That(sim.Context.Input.Aim,Is.EqualTo(aim));
        }
    }
}
