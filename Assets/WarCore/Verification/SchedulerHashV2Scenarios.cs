using WarCore.Sim;
using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;
using Fixture = WarCore.Verification.Fixtures.Fixtures;

namespace WarCore.Verification
{
    internal static class SchedulerHashV2Scenarios
    {
        internal static void Run(Report report)
        {
            Simulation baseline = Fixture.Create(123);
            for (int tick = 1; tick <= 510; tick++)
                baseline.Step(Fixture.Input(tick));
            byte[] saved = baseline.SaveSnapshot();
            ulong savedHash = baseline.StateHashV2();
            var expected = new ulong[490];
            for (int tick = 511; tick <= 1000; tick++)
            {
                baseline.Step(Fixture.Input(tick));
                expected[tick - 511] = baseline.StateHashV2();
            }
            HashScenarios.Require(expected[489] == 0x498903006F99EE22UL, "V2 whole-match known vector mismatch.");
            HashScenarios.Require(baseline.StateHash() == 0x8166D6848CDEB66CUL, "V1 whole-match vector changed.");
            report.Line("hashV2.whole.final", "hash=" + StableDigest.Hex64(expected[489]));
            foreach (bool fresh in new[] { false, true })
            {
                Simulation restored = fresh ? Fixture.Create(999) : baseline;
                restored.RestoreSnapshot(saved);
                HashScenarios.Require(restored.StateHashV2() == savedHash, "V2 restored pending cache mismatch.");
                var digest = new StableDigest();
                for (int tick = 511; tick <= 1000; tick++)
                {
                    restored.Step(Fixture.Input(tick));
                    ulong value = restored.StateHashV2();
                    HashScenarios.Require(value == expected[tick - 511], "V2 continuation mismatch at " + tick);
                    digest.Row(tick + ":" + StableDigest.Hex64(value));
                }
                report.Digest(fresh ? "hashV2.whole.fresh" : "hashV2.whole.inplace", digest);
            }
            foreach (bool reverse in new[] { false, true })
            {
                Simulation sim = SnapshotFixtures.A3(reverse);
                var hash = new StateHash();
                sim.Scheduler.WriteStateV2(hash);
                HashScenarios.Require(hash.Value == 0xEBB41DEBE5CC13ADUL, "V2 scheduler known vector mismatch.");
                HashScenarios.Require(Fixture.Hash(sim.Scheduler) == 0x22CB551F19EC00FEUL, "V1 scheduler vector changed.");
                report.Line(reverse ? "hashV2.heap.reverse" : "hashV2.heap.forward", "hash=" + StableDigest.Hex64(hash.Value));
            }
        }
    }
}
