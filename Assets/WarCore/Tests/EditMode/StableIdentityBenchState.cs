#if !STABLE_BASELINE
using System;
using System.Diagnostics;
using System.IO;
using WarCore.Sim;

namespace WarCore.Tests
{
    public static partial class StableIdentityBench
    {
        private sealed class RestoreObserver : IStableRestoreObserver
        {
            internal readonly long[] Stamps = new long[6];
            public void Reached(StableRestorePhase phase) => Stamps[(int)phase] = Stopwatch.GetTimestamp();
        }
        private static void IdentityGroups(int samples, AllocationMeter meter, TextWriter output)
        {
            var createTimes = new long[samples];
            var sortTimes = new long[samples];
            var combined = new long[samples];
            var lookupTimes = new long[samples];
            var allocations = new AllocationSample[samples];
            var sortAllocations = new AllocationSample[samples];
            var lookupAllocations = new AllocationSample[samples];
            var keys = new StableEntityKey[StableIdentityBenchScene.Count];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = StableEntityKey.Placed(913,(ulong)((i*7919+913)%keys.Length));
            long payload = 0;
            long reservedBytes = 0;
            for (int sample = -4; sample < samples; sample++)
            {
                // Reserve outside the budget window, as required by the identity-layer build budget.
                AllocationSample reserveBefore = meter.Read();
                var store = new EntityStore(keys.Length,IdentityMode.StableWorld,identityCapacity:keys.Length);
                var stage = new EntityStage(store);
                var queryContext = new GridQueryContext(keys.Length);
                reservedBytes = meter.Difference(reserveBefore).Bytes;
                AllocationSample before = meter.Read();
                long start = Stopwatch.GetTimestamp();
                foreach (StableEntityKey key in keys)
                    store.Create(key);
                long created = Stopwatch.GetTimestamp();
                long creationTime = created-start;
                AllocationSample creation = meter.Difference(before);
                before = meter.Read();
                EntityStage.View view = stage.Capture();
                long sorted = Stopwatch.GetTimestamp();
                AllocationSample sorting = meter.Difference(before);
                ulong previous = 0;
                int count = 0;
                foreach (EntityId entity in view)
                {
                    ulong key = store.StableOf(entity).Value;
                    if (count > 0 && key <= previous)
                        throw new InvalidOperationException("Stable view is not strictly ordered.");
                    previous = key;
                    count++;
                }
                CheckKey((ulong)keys.Length,(ulong)count);
                before = meter.Read();
                start = Stopwatch.GetTimestamp();
                foreach (StableEntityKey key in keys)
                {
                    if (!store.TryResolve(key.Id,out EntityRef entity))
                        throw new InvalidOperationException("Stable lookup missed an active entity.");
                    CheckKey(key.Id.Value,store.StableOf(entity).Value);
                }
                long lookup = Stopwatch.GetTimestamp()-start;
                AllocationSample lookupAllocation = meter.Difference(before);
                if (sample >= 0)
                {
                    createTimes[sample] = creationTime;
                    combined[sample] = creationTime+sorted-created;
                    allocations[sample] = creation;
                    sortAllocations[sample] = sorting;
                    lookupAllocations[sample] = lookupAllocation;
                    sortTimes[sample] = sorted-created;
                    lookupTimes[sample] = lookup;
                }
                payload = store.IdentityPayloadBytes+(long)keys.Length*24;
                GC.KeepAlive(queryContext);
            }
            Report(output,"identity.create-map.10000",createTimes,allocations,true);
            Report(output,"identity.sort.10000",sortTimes,sortAllocations,true);
            Report(output,"identity.build-sum.10000",combined,allocations,true);
            Report(output,"identity.lookup.10000",lookupTimes,lookupAllocations,true);
            output.WriteLine("identity-reserve|whole-store-plus-one-stage-plus-query-context-allocated-bytes="+reservedBytes+"; basis="+meter.Basis+"; includes-base-store-and-headers; retained-identity-upper-bound-on-direct-only");
            output.WriteLine("identity-memory|active-payload-with-one-stage-and-query-sort="+payload+"; budget_bytes=2097152; object_headers=excluded; source-record=80; record-order=4; slot-key=8; slot-record=4; spawn-value/presence/pending=10; stage-entry=16; query-sort-key=8; buckets=32768*4; retired-extra=84/record+table-growth; stable-event-links=32/event; task-key=40/task");
            // Both index banks reserve a slot permutation for each of their two partitions.
            long indexOrderPayload = keys.LongLength*2*2*4;
            output.WriteLine("identity-memory-query-order|reserved-derived-order="+indexOrderPayload
                +"; total-with-identity-stage-query-sort="+(payload+indexOrderPayload)+"; budget_bytes=2097152; object_headers=excluded; banks=2; partitions=2; bytes-per-slot=4");
        }
        private static void StateGroups(StableIdentityBenchScene scene, int samples, AllocationMeter meter, TextWriter output)
        {
            var hash = new long[samples];
            var tick = new long[samples];
            var snapshot = new long[samples];
            var restore = new long[samples];
            var hashAllocations = new AllocationSample[samples];
            var tickAllocations = new AllocationSample[samples];
            var snapshotAllocations = new AllocationSample[samples];
            var restoreAllocations = new AllocationSample[samples];
            var phaseTimes = new long[5][];
            for (int phase = 0; phase < phaseTimes.Length; phase++)
                phaseTimes[phase] = new long[samples];
            var observer = new RestoreObserver();
            for (int warm = 0; warm < 32; warm++)
            {
                scene.Sim.Step(default);
                scene.Sim.StateHashStable();
            }
            int snapshotBytes = 0;
            for (int i = 0; i < samples; i++)
            {
                AllocationSample before = meter.Read();
                long start = Stopwatch.GetTimestamp();
                scene.Sim.Step(default);
                tick[i] = Stopwatch.GetTimestamp()-start;
                tickAllocations[i] = meter.Difference(before);
                before = meter.Read();
                start = Stopwatch.GetTimestamp();
                ulong expected = scene.Sim.StateHashStable();
                hash[i] = Stopwatch.GetTimestamp()-start;
                hashAllocations[i] = meter.Difference(before);
                before = meter.Read();
                start = Stopwatch.GetTimestamp();
                byte[] data = scene.Sim.SaveStableSnapshot();
                snapshot[i] = Stopwatch.GetTimestamp()-start;
                snapshotAllocations[i] = meter.Difference(before);
                snapshotBytes = data.Length;
                before = meter.Read();
                start = Stopwatch.GetTimestamp();
                scene.Sim.RestoreStableSnapshot(data,reverseAllocation:i%2==0,observer:observer);
                restore[i] = Stopwatch.GetTimestamp()-start;
                restoreAllocations[i] = meter.Difference(before);
                for (int phase = 0; phase < phaseTimes.Length; phase++)
                    phaseTimes[phase][i] = observer.Stamps[phase+1]-observer.Stamps[phase];
                CheckKey(expected,scene.Sim.StateHashStable());
            }
            Report(output,"state.hash.10000",hash,hashAllocations,true);
            Report(output,"state.tick-static.10000",tick,tickAllocations,true);
            Report(output,"state.snapshot-cold.10000",snapshot,snapshotAllocations,false);
            Report(output,"state.restore-cold.10000",restore,restoreAllocations,false);
            string[] names = { "map-and-header", "bind-components", "bind-systems", "rebuild-index", "warmup" };
            for (int phase = 0; phase < names.Length; phase++)
                Report(output,"restore."+names[phase],phaseTimes[phase],Array.Empty<AllocationSample>(),false);
            output.WriteLine("state-final-hash=0x"+scene.Sim.StateHashStable().ToString("X16"));
            output.WriteLine("snapshot-bytes="+snapshotBytes+"; restore-phase-allocation=not-separated; tick-fixture=10000-static-state,no-AI; whole-game-tick-budget=not-evaluated");
        }
    }
}
#endif
