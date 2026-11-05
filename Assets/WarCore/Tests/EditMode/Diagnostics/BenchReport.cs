using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace WarCore.Tests.Diagnostics
{
    internal static class BenchReport
    {
        internal static string Number(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
        internal static double Mean(long[] values)
        {
            double sum = 0;
            for (int i = 0; i < values.Length; i++)
                sum += values[i];
            return sum / values.Length;
        }

        internal static double Median(long[] sorted) => sorted.Length % 2 == 0
            ? ((double)sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2];
        internal static long Percentile(long[] sorted, int percentile) => sorted[(sorted.Length * percentile + 99) / 100 - 1];
        internal static string Distribution(long[] values, double scale = 1)
        {
            Array.Sort(values);
            return Number(Mean(values) * scale) + "/" + Number(Median(values) * scale) + "/"
                + Number(Percentile(values, 95) * scale) + "/" + Number(values[values.Length - 1] * scale);
        }

        internal static void TickSummary(TextWriter output, string mode, long[] elapsed, AllocationSample[] bytes, int rays, int affected)
        {
            var sorted = (long[])elapsed.Clone();
            Array.Sort(sorted);
            output.WriteLine("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |",
                mode, elapsed.Length, Number(Mean(elapsed) * 1000 / Stopwatch.Frequency),
                Number(Percentile(sorted, 99) * 1000.0 / Stopwatch.Frequency), Number(sorted[sorted.Length - 1] * 1000.0 / Stopwatch.Frequency),
                AllocationSample.Summary(bytes), rays, affected);
        }

        internal static void Queries(TextWriter output, RealBenchScene scene, int ticks)
        {
            output.WriteLine("Query distributions: mean/median/p95/max; counts are per query; allocation evidence is labelled per sample.");
            output.WriteLine("| role | calls | hits | deduplicated candidates | precise entries | allocation | us | Walk | dictionary | leaf members | dedup checks | duplicate skips | LastVisitedCells |");
            output.WriteLine("|---|---:|---|---|---|---|---|---|---|---|---|---|---|");
            for (int role = 0; role < 9; role++)
            {
                int count = 0;
                for (int i = 0; i < scene.Queries.Length; i++)
                    if ((int)scene.Queries[i].Role == role)
                        count += ticks;
                if (count == 0)
                    continue;
                var fields = new long[11][];
                var allocations = new AllocationSample[count];
                for (int f = 0; f < fields.Length; f++)
                    fields[f] = new long[count];
                int cursor = 0;
                for (int i = 0; i < scene.Observations.Length; i++)
                {
                    if ((int)scene.Queries[i % scene.Queries.Length].Role != role)
                        continue;
                    QueryObservation q = scene.Observations[i];
                    fields[0][cursor] = q.Hits;
                    fields[1][cursor] = q.Candidates;
                    fields[2][cursor] = q.Precise;
                    allocations[cursor] = q.Allocation;
                    fields[4][cursor] = q.Elapsed;
                    fields[5][cursor] = q.Walks;
                    fields[6][cursor] = q.Lookups;
                    fields[7][cursor] = q.Members;
                    fields[8][cursor] = q.Dedup;
                    fields[9][cursor] = q.Duplicates;
                    fields[10][cursor] = q.Visited;
                    cursor++;
                }

                output.Write("| {0} | {1} |", (QueryRole)role, count);
                for (int f = 0; f < fields.Length; f++)
                    output.Write(" {0} |", f == 3 ? AllocationSample.Summary(allocations) : Distribution(fields[f], f == 4 ? 1000000.0 / Stopwatch.Frequency : 1));
                output.WriteLine();
            }

            output.WriteLine("Precise entries retain the product counter definition: sphere/sector enter geometry before its cheap rejects; ray entries are after the slab prefilter. The replay times sphere/sector scalar rejects separately as a nested prefilter; their precise-entry counter retains its existing meaning.");
        }
    }
}
