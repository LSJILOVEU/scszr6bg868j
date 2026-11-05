// Managed direct counter tests are deliberately excluded from Unity discovery.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2"),Category("M0c2S2Allocation")]
    public sealed class M0c2S2AllocationTests
    {
        [Test] public void FirstRestoredStatusTickAndHashAllocateZeroWithPositiveControl()
        {
            var source=M0c2S2Fixture.Create();for(int tick=0;tick<40;tick++)M0c2S2Fixture.Step(source);
            Assert.That(source.GameplayStatusCount,Is.GreaterThan(0));
            var restored=M0c2S2Fixture.Create(false,true,1);restored.RestoreRetainedSnapshot(source.SaveRetainedSnapshot(),true);
            long before=GC.GetAllocatedBytesForCurrentThread();ulong digest=M0c2S2Fixture.Step(restored);
            long first=GC.GetAllocatedBytesForCurrentThread()-before;
            before=GC.GetAllocatedBytesForCurrentThread();
            for(int tick=0;tick<320;tick++)digest^=M0c2S2Fixture.Step(restored);
            long steady=GC.GetAllocatedBytesForCurrentThread()-before;
            long[] controls=new long[3];int[] sizes={16,64,1024};
            for(int i=0;i<sizes.Length;i++)
            {before=GC.GetAllocatedBytesForCurrentThread();var bytes=new byte[sizes[i]];controls[i]=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(bytes);}
            Assert.That(first,Is.Zero,"first status tick and retained hash after restore");Assert.That(steady,Is.Zero,"320 complete status ticks plus retained hashes");
            for(int i=0;i<sizes.Length;i++)Assert.That(controls[i],Is.GreaterThanOrEqualTo(sizes[i]),"direct counter positive control "+sizes[i]);
            GC.KeepAlive(digest);
            TestContext.WriteLine("S2 Mono direct allocation: first restored Step+Hash={0} B; 320 Step+Hash={1} B; controls16/64/1024={2}/{3}/{4} B",first,steady,controls[0],controls[1],controls[2]);
        }
    }
}
#endif
