using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;
namespace WarCore.Verification
{
    // Public facade keeps the Unity assembly independent of Sim and internal fixtures.
    public sealed class M0c2FrameWorkload
    {
        public static string[] FullGroups => M0c2FullFixture.Names;
        private readonly M0c2FullFixture fixture;
        private readonly Simulation sim;
        public M0c2FrameWorkload(bool full,int group)
        {
            fixture=full?new M0c2FullFixture(group):null;
            sim=full?fixture.Sim:M0c2Fixture.Create(group%2==0?0:ulong.MaxValue);
        }
        public void Step()=>sim.Step(default);
        public ulong Hash()=>sim.StateHashRetained();
        public void Restore()=>sim.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
        public long Tick=>sim.Clock.Tick;
        public int Queries=>fixture==null?0:fixture.QueryCount;
        public int Rays=>fixture==null?0:fixture.RayCount;
        public int Affected=>fixture==null?0:fixture.Affected;
        public int Alive=>fixture==null?0:fixture.Alive;
        public int Truncated=>fixture==null?0:fixture.Truncated;
        public long Created=>fixture?.Churn==null?0:fixture.Churn.Created;
        public long Notices=>fixture?.Churn?.References==null?0:fixture.Churn.References.Notices;
    }
}
