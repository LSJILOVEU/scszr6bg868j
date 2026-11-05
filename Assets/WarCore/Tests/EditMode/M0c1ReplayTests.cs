using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture, Category("M0c1"), Category("M0c1Replay")]
    public sealed class M0c1ReplayTests
    {
        private static StableEntityKey Parent => M0c1Fixture.Parent;
        [TestCase(false),TestCase(true)]
        public void EveryCheckpointContinuesForThreeHundredTicks(bool melee)
        {
            var data=melee?M0c1Package.Melee:M0c1Package.Ranged;
            var baseline=M0c1Fixture.Create(out var system,32,package:data);system.Auto=true;baseline.Entities.Create(Parent);
            const int checkpoints=300, continuation=300;
            var hashes=new ulong[checkpoints+continuation+1];var snapshots=new byte[checkpoints+1][];
            hashes[0]=baseline.StateHashRetained();snapshots[0]=baseline.SaveRetainedSnapshot();
            for(int tick=1;tick<hashes.Length;tick++)
            {baseline.Step(default);hashes[tick]=baseline.StateHashRetained();if(tick<=checkpoints)snapshots[tick]=baseline.SaveRetainedSnapshot();}
            for(int point=0;point<=checkpoints;point++)
            {
                var resumed=M0c1Fixture.Create(out _,32,package:data);resumed.RestoreRetainedSnapshot(snapshots[point],true);
                Assert.That(resumed.StateHashRetained(),Is.EqualTo(hashes[point]),"restore "+point);
                for(int tick=point+1;tick<=point+continuation;tick++)
                {resumed.Step(default);Assert.That(resumed.StateHashRetained(),Is.EqualTo(hashes[tick]),"checkpoint "+point+" tick "+tick);}
            }
            TestContext.WriteLine("checkpoint-sweep|package="+data.Name+"; checkpoints=301; continuation=300; per_tick_hash_matches=90300");
        }
    }
}
