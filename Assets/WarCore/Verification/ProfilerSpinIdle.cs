using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WarCore.Verification
{
    // All parsing, sorting and buffers are prepared before the recorder starts.
    public sealed class ProfilerSpinIdle
    {
        private readonly long[] targets;
        private readonly string description;
        public long BudgetMilliseconds { get; }
        public int Frames => targets.Length;

        private ProfilerSpinIdle(long[] targets, string description)
        {
            this.targets = targets;
            this.description = description;
            long total = 0;
            foreach (long target in targets)
            {
                if (target < 0 || target > Stopwatch.Frequency)
                    throw new ArgumentException("Spin targets must be between 0 and 1000000 microseconds per frame.");
                total = checked(total + target);
            }
            BudgetMilliseconds = checked(total * 1000 / Stopwatch.Frequency);
        }

        public static ProfilerSpinIdle Fixed(long micros)
        {
            if (micros < 0 || micros > 1000000)
                throw new ArgumentException("-g23SpinMicros requires 0..1000000.");
            var targets = new long[ProfilerAllocationRun.WarmupTicks + ProfilerAllocationRun.BaselineFrames];
            for (int i = 0; i < targets.Length; i++) targets[i] = checked(micros * Stopwatch.Frequency) / 1000000;
            return new ProfilerSpinIdle(targets, "kind=fixed;micros=" + micros.ToString(CultureInfo.InvariantCulture));
        }

        public static ProfilerSpinIdle FromArguments(string mode)
        {
            string micros = HashVectorRunner.Argument("-g23SpinMicros", "none");
            string csv = HashVectorRunner.Argument("-g23SpinCsv", "none");
            if (mode != "spin-idle")
            {
                if (micros != "none" || csv != "none") throw new ArgumentException("Spin options require -g23Bisect spin-idle.");
                return null;
            }
            if ((micros == "none") == (csv == "none"))
                throw new ArgumentException("spin-idle requires exactly one of -g23SpinMicros or -g23SpinCsv.");
            if (csv != "none") return FromCsv(csv);
            if (!long.TryParse(micros, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                throw new ArgumentException("Invalid -g23SpinMicros.");
            return Fixed(value);
        }

        public static string Fingerprint(string csv)
        {
            using (var sha = SHA256.Create())
            {
                // Both timing metadata and frame rows belong to the replay contract.
                byte[] value = Encoding.UTF8.GetBytes(File.ReadAllText(csv.Substring(0, csv.Length - 4)) + File.ReadAllText(csv));
                return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant();
            }
        }

        public static ProfilerSpinIdle FromCsv(string path)
        {
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".txt.csv", StringComparison.Ordinal)) throw new ArgumentException("Expected allocations.txt.csv with its sibling report.");
            string report = File.ReadAllText(path.Substring(0, path.Length - 4));
            string name = Field(report, "bisect|", "name");
            if (name == "spin-idle" || !ProfilerBisectWorkload.IsValid(name)) throw new ArgumentException("Replay requires a Sim bisect mode.");
            if (Field(report, "injection|", "enabled") != "False") throw new ArgumentException("Do not replay an injected timing source.");
            long frequency = long.Parse(Field(report, "frame_time|", "frequency"), CultureInfo.InvariantCulture);
            if (frequency <= 0) throw new ArgumentException("Invalid timing frequency.");
            var selected = new List<long>();
            var idle = new List<long>();
            var probe = new List<long>();
            using (var reader = new StreamReader(path))
            {
                string[] columns = reader.ReadLine().Split(',');
                int profile = Array.IndexOf(columns, "profile"), phase = Array.IndexOf(columns, "phase");
                int duration = Array.IndexOf(columns, "frame_duration_ticks"), tick = Array.IndexOf(columns, "tick");
                if (profile < 0 || phase < 0 || duration < 0 || tick < 0) throw new ArgumentException("CSV lacks timing/layout columns.");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] fields = line.Split(',');
                    if (fields.Length != columns.Length) throw new ArgumentException("Invalid CSV row width.");
                    long value = long.Parse(fields[duration], CultureInfo.InvariantCulture);
                    if (value < 0) throw new ArgumentException("Negative frame duration.");
                    if (fields[profile] == "Bisect-" + name)
                    {
                        int ordinal = selected.Count + 1;
                        if (int.Parse(fields[tick], CultureInfo.InvariantCulture) != ordinal
                            || fields[phase] != (ordinal <= ProfilerAllocationRun.WarmupTicks ? "warmup" : "steady"))
                            throw new ArgumentException("Timing replay frame order changed.");
                        selected.Add(value);
                    }
                    else if (fields[phase] == "steady" && fields[profile] == "Idle") idle.Add(value);
                    else if (fields[phase] == "steady" && fields[profile] == "ProbeOnly") probe.Add(value);
                }
            }
            if (selected.Count != ProfilerAllocationRun.WarmupTicks + ProfilerAllocationRun.BaselineFrames
                || idle.Count != ProfilerAllocationRun.BaselineFrames || probe.Count != ProfilerAllocationRun.BaselineFrames)
                throw new ArgumentException("Timing replay requires full 32+1200 frames and both 1200-frame idle baselines.");
            idle.AddRange(probe);
            idle.Sort();
            // Whole callback intervals already include engine overhead. Remove the observed idle median
            // before inserting busy work; compare actual resulting intervals again after the run.
            long overhead = idle[idle.Count / 2];
            var targets = new long[selected.Count];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = checked((long)Math.Round(Math.Max(0L, selected[i] - overhead) * (double)Stopwatch.Frequency / frequency));
            return new ProfilerSpinIdle(targets, "kind=csv;source_profile=Bisect-" + name + ";source_sha256=" + Fingerprint(path)
                + ";source_frequency=" + frequency + ";idle_overhead_ticks=" + overhead
                + ";source_csv_b64=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(path)));
        }

        private static string Field(string report, string prefix, string key)
        {
            foreach (string line in report.Split('\n'))
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    foreach (string field in line.Substring(prefix.Length).Split(';'))
                    {
                        string item = field.Trim();
                        if (item.StartsWith(key + "=", StringComparison.Ordinal)) return item.Substring(key.Length + 1);
                    }
            throw new ArgumentException("Timing report lacks " + prefix + key);
        }

        public void Spin(int index)
        {
            long start = Stopwatch.GetTimestamp();
            long target = targets[index];
            while (Stopwatch.GetTimestamp() - start < target) { }
        }

        public void Describe(TextWriter output)
        {
            output.WriteLine("spin_idle|version=1;{0};frames={1};frequency={2};wait=Stopwatch.GetTimestamp-poll;sim_calls=0;managed_allocation_sites=0;actual_timing_match=external-compare-required", description, targets.Length, Stopwatch.Frequency);
        }
    }
}
