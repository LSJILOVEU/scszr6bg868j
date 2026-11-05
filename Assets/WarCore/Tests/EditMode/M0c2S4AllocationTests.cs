#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4"),Category("M0c2S4Allocation")]
    public sealed class M0c2S4AllocationTests
    {
        [Test] public void FirstRestoredLifeTickAndHashAllocateZeroWithPositiveControl()
        {
            ulong digest=0;
            for(int package=0;package<2;package++)
            {
                var warm=M0c2S4Fixture.Create(package!=0);for(int i=0;i<50;i++)digest^=M0c2S4Fixture.Replay(warm);
                foreach(int checkpoint in new[]{1,6,9,10})
                {
                    var source=M0c2S4Fixture.Create(package!=0);while(source.Clock.Tick<checkpoint)M0c2S4Fixture.Replay(source);
                    var restored=M0c2S4Fixture.Create(package!=0,true);restored.RestoreRetainedSnapshot(source.SaveRetainedSnapshot(),true);
                    long before=GC.GetAllocatedBytesForCurrentThread();digest^=M0c2S4Fixture.Replay(restored);long first=GC.GetAllocatedBytesForCurrentThread()-before;
                    before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<320;i++)digest^=M0c2S4Fixture.Replay(restored);long steady=GC.GetAllocatedBytesForCurrentThread()-before;
                    Assert.That(first,Is.Zero,"first restored life Step+Hash");Assert.That(steady,Is.Zero,"steady life Step+Hash");
                    TestContext.WriteLine("S4 package={0} checkpoint={1}: first={2} B, 320 ticks={3} B",package,checkpoint,first,steady);
                }
            }
            foreach(int size in new[]{16,64,1024})
            {long before=GC.GetAllocatedBytesForCurrentThread();var bytes=new byte[size];long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Assert.That(allocated,Is.GreaterThanOrEqualTo(size),"direct counter positive control "+size);GC.KeepAlive(bytes);TestContext.WriteLine("S4 control payload={0}: {1} B",size,allocated);}
            GC.KeepAlive(digest);
        }
    }
}
#endif
