using System;
using System.IO;
using WarCore.Sim;
using WarCore.Verification.Fixtures.Diagnostics;

namespace WarCore.Verification
{
    internal sealed class ProfilerDenseWorkload
    {
        private readonly string name;
        private readonly SemanticBenchScene scene = new SemanticBenchScene(true, false);
        private readonly SemanticResult[] results;
        private readonly SpatialHit[][] captured;
        private readonly int[] selected;
        private readonly SemanticObserver observer;
        private ulong checksum;
        private long calls;
        internal long QueryCalls;
        internal int OperationsPerFrame => selected.Length;

        internal ProfilerDenseWorkload(string selectedName)
        {
            name = selectedName;
            int count = 0;
            for (int i = 0; i < scene.Queries.Length; i++) if (Select(scene.Queries[i])) count++;
            selected = new int[count];
            int offset = 0;
            for (int i = 0; i < scene.Queries.Length; i++) if (Select(scene.Queries[i])) selected[offset++] = i;
            if (name == "dense-signature" || name == "dense-observer")
            {
                results = new SemanticResult[count];
                captured = new SpatialHit[count][];
                for (int i = 0; i < count; i++)
                {
                    SemanticResult result = scene.Queries[selected[i]].Run(scene.Grid, scene.Hits);
                    results[i] = result;
                    captured[i] = new SpatialHit[result.Written];
                    Array.Copy(scene.Hits, captured[i], result.Written);
                    HashScenarios.Require(result.Signature(captured[i]) == scene.Expected[selected[i]].Signature, "Dense replay capture mismatch.");
                }
            }
            if (name == "dense-observer") observer = new SemanticObserver(count, new SemanticPlayerMeter());
        }

        private bool Select(SemanticQuery query)
        {
            if (name == "dense-ray-any") return query.Request.Kind == SpatialQueryKind.Ray && query.Mode == SemanticMode.Any;
            if (name == "dense-ray-nearest") return query.Request.Kind == SpatialQueryKind.Ray && query.Mode == SemanticMode.Nearest;
            if (name == "dense-sphere") return query.Role == QueryRole.Sphere;
            if (name == "dense-sector") return query.Role == QueryRole.Sector;
            return true;
        }

        internal void Step()
        {
            if (name == "dense")
            {
                scene.Sim.Step(default);
                calls += selected.Length;
                QueryCalls += selected.Length;
                return;
            }
            if (observer != null) observer.Count = 0;
            for (int i = 0; i < selected.Length; i++)
            {
                if (name == "dense-signature")
                {
                    // The actual shared signature/checksum implementation, on real captured results.
                    ulong signature = results[i].Signature(captured[i]);
                    HashScenarios.Require(signature == scene.Expected[selected[i]].Signature, "Semantic output changed.");
                    HashScenarios.Require(!results[i].Truncated, "DenseRegular capture unexpectedly truncated.");
                    checksum = SemanticResult.Mix(checksum, signature);
                }
                else if (observer != null)
                {
                    observer.Begin();
                    observer.End(selected[i], results[i], scene.Grid);
                }
                else
                {
                    SemanticResult result = scene.Queries[selected[i]].Run(scene.Grid, scene.Hits);
                    // Retain scalar outputs without full signature work in the query-only arm.
                    checksum = unchecked(checksum + (ulong)(result.Written + (result.Hit ? 1 : 0)));
                    QueryCalls++;
                }
                calls++;
            }
        }

        internal void Verify(int frames)
        {
            HashScenarios.Require(calls == (long)frames * selected.Length, "Dense bisect coverage mismatch.");
            bool replay = results != null;
            HashScenarios.Require(QueryCalls == (replay ? 0 : calls), "Replay invoked queries or query arm lost work.");
            HashScenarios.Require(scene.Sim.Clock.Tick == (name == "dense" ? frames : 0), "Dense bisect advanced an unexpected Sim.");
            if (observer != null)
            {
                HashScenarios.Require(observer.Count == selected.Length, "Dense observer coverage mismatch.");
                for (int i = 0; i < selected.Length; i++)
                    HashScenarios.Require(observer.Samples[i].Result.Written == results[i].Written, "Dense observer result mismatch.");
            }
            if (name == "dense-signature" || name == "dense")
            {
                ulong expected = 0;
                for (int frame = 0; frame < frames; frame++)
                    for (int i = 0; i < selected.Length; i++) expected = SemanticResult.Mix(expected, scene.Expected[selected[i]].Signature);
                HashScenarios.Require((name == "dense" ? scene.Checksum : checksum) == expected, "Dense output checksum mismatch.");
            }
            else if (!replay)
            {
                long expected = 0;
                for (int i = 0; i < selected.Length; i++)
                {
                    SemanticResult result = scene.Queries[selected[i]].Run(scene.Grid, scene.Hits);
                    HashScenarios.Require(result.Signature(scene.Hits) == scene.Expected[selected[i]].Signature, "Dense query-only final output mismatch.");
                    expected += result.Written + (result.Hit ? 1 : 0);
                }
                HashScenarios.Require(checksum == (ulong)(frames * expected), "Dense query-only scalar checksum mismatch.");
            }
            HashScenarios.Require(scene.Sim.Events.EventsDroppedTotal == 0, "Dense bisect dropped events.");
        }

        internal void Describe(TextWriter output)
        {
            output.WriteLine("bisect.dense|generator={0};input=0x{1:X16};selected=0x{2:X16};oracle=0x{3:X16};queries_per_frame={4};diagnostics=false;replay_grid_counters=last-setup-query;observer_buffer=reused-per-frame",
                SemanticBenchScene.Generator, scene.InputSignature, scene.OutputSignature, scene.OracleSignature, selected.Length);
        }

        internal void WriteFinal(TextWriter output)
        {
            output.WriteLine("bisect.dense.final|calls={0};queries={1};checksum=0x{2:X16};sim_tick={3};full_calls={4};full_checksum=0x{5:X16}", calls, QueryCalls, checksum, scene.Sim.Clock.Tick, scene.Calls, scene.Checksum);
            if (observer != null) output.WriteLine("bisect.observer|last_frame_samples={0};allocation_basis={1};outer_evidence=ProfilerRecorder", observer.Count, observer.Samples[0].Allocation.Basis);
        }
    }
}
