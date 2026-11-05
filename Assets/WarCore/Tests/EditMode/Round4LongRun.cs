using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public static class Round4LongRun
    {
        private sealed class Scene
        {
            internal const int Living = 200;
            internal const int Churn = Living / 20;
            internal readonly Simulation Sim = Simulation.CreateRetainedWorld(913,
                new SimConfig(entityCapacity:Living,timerCapacity:Churn,eventRetainedTicks:4,eventInitialCapacity:16,eventHardLimit:16));
            private readonly EntityId[] entities = new EntityId[Living];
            private int offset;
            internal Scene()
            {
                for (int i = 0; i < Living; i++)
                    entities[i] = Sim.Entities.Create(StableEntityKey.World(1,0,(ulong)i));
            }
            internal void Step()
            {
                for (int i = 0; i < Churn; i++)
                {
                    EntityId entity = entities[(offset+i)%Living];
                    TaskKey task = Sim.Scheduler.ScheduleFor(Sim.Clock.Tick+1,0,Sim.Entities.StableOf(entity),1,new StableSimEvent(new EventTypeId(100)));
                    Require(Sim.Scheduler.CancelStable(task),"Long-run task was not canceled.");
                    Sim.Entities.Destroy(entity);
                }
                Sim.CompleteSpatialBoundary();
                for (int i = 0; i < Churn; i++)
                    entities[(offset+i)%Living] = Sim.Entities.Create(StableEntityKey.World(2,Sim.Clock.Tick,(ulong)i));
                offset = (offset+Churn)%Living;
                Sim.Step(default);
                Require(Sim.Entities.ActiveEntityCount == Living,"Long-run living population changed.");
            }
        }
        public static void Run(TextWriter output, int cycles = 1000000)
        {
            if (cycles < 1000000 || cycles % 100000 != 0)
                throw new ArgumentException("Long-run evidence requires a multiple of 100000 and at least one million cycles.");
            var scene = new Scene();
            for (int tick = 0; tick < 80; tick++)
                scene.Step();
            var meter = new AllocationMeter();
            Stopwatch.GetTimestamp();
            meter.Difference(meter.Read());
            for (int i = 0; i < 16; i++)
                scene.Sim.StateHashRetained();
            output.WriteLine("MF4-long-run|seed=913; alive=200; churn_per_tick=10; churn_percent=5; retention=32; identity_capacity=800; cycles="+cycles);
            output.WriteLine(meter.DescribeProbe());
            int snapshotBytes = scene.Sim.SaveRetainedSnapshot().Length;
            int records = scene.Sim.Entities.RegisteredIdentityCount;
            long payload = scene.Sim.Entities.IdentityPayloadBytes;
            long firstMemory = GC.GetTotalMemory(true);
            var hashTimes = new long[51];
            long allBytes = 0;
            long hashBytes = 0;
            ulong checksum = 0;
            int completed = 0;
            for (int block = 0; block <= cycles/100000; block++)
            {
                if (block != 0)
                {
                    AllocationSample before = meter.Read();
                    for (int tick = 0; tick < 10000; tick++)
                        scene.Step();
                    AllocationSample sample = meter.Difference(before);
                    if (sample.IsDirect)
                    {
                        Require(sample.Bytes == 0,"Long-run direct allocation was not zero.");
                        allBytes += sample.Bytes;
                    }
                    completed += 100000;
                }
                Require(scene.Sim.Entities.RegisteredIdentityCount == records,"Retired identity count grew after steady state.");
                Require(scene.Sim.Entities.IdentityPayloadBytes == payload,"Identity storage expanded.");
                int bytes = scene.Sim.SaveRetainedSnapshot().Length;
                Require(bytes == snapshotBytes,"Snapshot bytes grew with historical churn.");
                for (int i = 0; i < hashTimes.Length; i++)
                {
                    AllocationSample before = meter.Read();
                    long start = Stopwatch.GetTimestamp();
                    checksum ^= scene.Sim.StateHashRetained();
                    hashTimes[i] = Stopwatch.GetTimestamp()-start;
                    AllocationSample sample = meter.Difference(before);
                    if (sample.IsDirect)
                    {
                        Require(sample.Bytes == 0,"Long-run hash direct allocation was not zero.");
                        hashBytes += sample.Bytes;
                    }
                }
                Array.Sort(hashTimes);
                long memory = GC.GetTotalMemory(true);
                output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "long-run-point|cycles={0}; identities={1}; snapshot_bytes={2}; identity_payload_bytes={3}; post_gc_managed_bytes={4}; hash_median_ms={5:F6}; hash_p99_ms={6:F6}",
                    completed,records,bytes,payload,memory,hashTimes[25]*1000.0/Stopwatch.Frequency,hashTimes[50]*1000.0/Stopwatch.Frequency));
            }
            output.WriteLine("long-run-complete|cycles="+completed+"; basis="+meter.Basis+"; churn_allocated_bytes="
                +(meter.Basis == AllocationBasis.Direct ? allBytes.ToString(CultureInfo.InvariantCulture) : "unverified")
                +"; hash_allocated_bytes="+(meter.Basis == AllocationBasis.Direct ? hashBytes.ToString(CultureInfo.InvariantCulture) : "unverified")
                +"; first_post_gc_managed_bytes="+firstMemory+"; checksum=0x"+checksum.ToString("X16"));
        }
        internal static void Require(bool valid, string message)
        {
            if (!valid)
                throw new InvalidOperationException(message);
        }
    }
}
