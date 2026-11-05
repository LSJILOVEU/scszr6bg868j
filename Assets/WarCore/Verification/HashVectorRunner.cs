using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using WarCore.Sim;

namespace WarCore.Verification
{
    public static class HashVectorRunner
    {
        public static void Run() => Run(Argument("-g23Output", "Tools/g23/out/verification.txt"));

        public static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == name)
                {
                    if (i + 1 == args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException("Missing value for " + name);
                    return args[i + 1];
                }
            return fallback;
        }

        public static void Run(string outputPath)
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                using (var report = new Report(outputPath))
                {
                    report.Line("schema", "g23-v4;arithmetic=2;collider=3;snapshot=4;digest=fnv1a64;encoding=utf8;rowTerminator=LF");
                    report.Line("source.q32", "sha256=" + VectorData0.Sha256);
                    report.Line("source.legacy", "sha256=" + VectorData1.Sha256);
                    report.Line("source.spatial", "sha256=" + VectorData2.Sha256);
                    report.Line("source.placed", "sha256=" + VectorData3.Sha256);
                    PlacedScenarios.Run(report);
                    GameplayScenarios.Run(report);
                    HashScenarios.WholeMatch(report);
                    LayerHashScenarios.Run(report);
                    StaticColliderScenarios.Run(report);
                    HashScenarios.Heap(report, false);
                    HashScenarios.Heap(report, true);
                    HashScenarios.ShiftedHeap(report);
                    SchedulerHashV2Scenarios.Run(report);
                    MathVectors.Run(report, "math.q32", VectorData0.Text, false);
                    MathVectors.Run(report, "math.legacyNonTrig", VectorData1.Text, true);
                    SpatialVectors.Run(report);
                    Tables(report);
                    Trigonometry(report);
                    BoundaryProbes.Run(report);
                    HashScenarios.Load(report);
                    report.Line("complete", "status=ok");
                }
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        private static void Tables(Report report)
        {
            var fields = new List<FieldInfo>();
            foreach (Type type in typeof(SimFix).Assembly.GetTypes())
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (field.IsDefined(typeof(ImmutableTableAttribute), false)) fields.Add(field);
            fields.Sort((a, b) => string.CompareOrdinal(a.DeclaringType.FullName + "." + a.Name, b.DeclaringType.FullName + "." + b.Name));
            HashScenarios.Require(fields.Count > 0, "No immutable tables found; linker metadata missing?");
            foreach (FieldInfo field in fields)
            {
                var values = (long[])field.GetValue(null);
                var digest = new StateHash();
                foreach (long value in values) digest.Add(value);
                ulong expected = field.GetCustomAttribute<ImmutableTableAttribute>().Checksum;
                report.Line("table." + field.DeclaringType.Name + "." + field.Name,
                    "rows=" + values.Length + ";stateHash=" + StableDigest.Hex64(digest.Value) + ";expected=" + StableDigest.Hex64(expected));
                HashScenarios.Require(digest.Value == expected, "Table checksum mismatch: " + field.Name);
            }
        }

        private static void Trigonometry(Report report)
        {
            var sin = new StableDigest();
            var cos = new StableDigest();
            var atan = new StableDigest();
            var edges = new StableDigest();
            for (int phase = 0; phase < 65536; phase++)
            {
                SimFix angle = SimFix.FromRaw(phase);
                SimFix y = SimMath.Sin(angle);
                SimFix x = SimMath.Cos(angle);
                sin.Row(phase + ":" + y.Raw);
                cos.Row(phase + ":" + x.Raw);
                atan.Row(phase + ":" + SimMath.Atan2(y, x).Raw);
                // Also preserve the extreme-input sweep used by S1TrigTests.
                edges.Row(phase + ":" + SimMath.Atan2(SimFix.FromRaw(long.MinValue + phase), SimFix.FromRaw(long.MaxValue - phase)).Raw);
            }
            report.Digest("trig.sin", sin);
            report.Digest("trig.cos", cos);
            report.Digest("trig.atan2Circle", atan);
            report.Digest("trig.atan2Edges", edges);
        }
    }
}
