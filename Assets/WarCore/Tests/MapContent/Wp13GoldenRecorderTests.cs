using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class Wp13GoldenRecorderTests
    {
        [Category("Manual"), Test, Explicit("Record only when the dispatch authorises a re-record (WP-21 did); never regenerate to accept a regression.")]
        public void RecordWp13Baseline()
        {
            string output = Environment.GetEnvironmentVariable("WARCORE_WP13_GOLDEN_OUT");
            string path = Path.GetFullPath(string.IsNullOrWhiteSpace(output) ? Wp13GoldenData.BaselinePath : output);
            Assert.That(File.Exists(path), Is.False, "Refusing to overwrite a baseline: " + path);

            // Complete all scenarios before creating the file, so a simulation failure writes no partial baseline.
            var lines = new List<string>(Wp13GoldenData.Header);
            Wp13GoldenData.Capture(row => lines.Add(row.Line));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.NewLine = "\n";
                foreach (string line in lines) writer.WriteLine(line);
            }
            TestContext.Out.WriteLine("WP13 baseline recorded: " + path);
        }
    }
}
