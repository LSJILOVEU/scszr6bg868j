using System;
using System.IO;
using WarCore.Sim;
using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Verification
{
    public static class StableIdentityRunner
    {
        public static void Run(string mode, string path)
        {
            using (var output = new StreamWriter(path))
            {
#if !STABLE_BASELINE
                // Constants come from Tools/stable-identity-vectors.py, an independent byte encoder.
                var sim = new Simulation(123,identityMode:IdentityMode.StableWorld);
                sim.Entities.Create(StableEntityKey.Placed(0,0));
                StableIdentityBench.CheckKey(0xD55AF3A9B2827FD4UL,sim.StateHashStable());
                var scheduler = new SimScheduler(4,IdentityMode.StableWorld);
                scheduler.ScheduleStable(new TaskKey(5,1,new StableId(9),0,7),new StableSimEvent(new EventTypeId(100),tag:3));
                StableIdentityBench.CheckKey(0x1E79EB5A9C9A126AUL,scheduler.StateHashStable());
                StableIdentityBench.CheckKey(0xC8B486766E9FAEAEUL,StableEntityKey.Placed(0,0).Id.Value);
                StableIdentityBench.CheckKey(0x9340AFD4448D0199UL,StableEntityKey.Placed(ulong.MaxValue,1UL<<63).Id.Value);
                StableIdentityBench.CheckKey(0xF2E5F858A0D8F621UL,StableEntityKey.Spawned(new StableId(1UL<<63),ulong.MaxValue,7).Id.Value);
                StableIdentityBench.CheckKey(0x382A95B626962FBBUL,StableEntityKey.World(9,300,4).Id.Value);
                output.WriteLine("stable-vectors|state-v3=0x{0:X16}; scheduler-v3=0x{1:X16}; independent-oracle=passed",sim.StateHashStable(),scheduler.StateHashStable());
#endif
                StableIdentityBench.Run(mode,output);
            }
            Console.WriteLine(File.ReadAllText(path));
        }
    }
}
