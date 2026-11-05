using System.IO;
using System.Text;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class Wp13GoldenTests
    {
        [Test]
        public void EveryTickAndSnapshotMatchesBaseline()
        {
            // The output override belongs only to the manual recorder. CI always reads the committed test asset.
            string path = Wp13GoldenData.BaselinePath;
            if (!File.Exists(path))
                Assert.Fail("Golden file missing; run RecordWp13Baseline on unchanged code first. Path: " + path);

            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                int lineNumber = 0;
                foreach (string header in Wp13GoldenData.Header)
                {
                    lineNumber++;
                    Assert.That(reader.ReadLine(), Is.EqualTo(header), "WP13 header line=" + lineNumber);
                }
                Wp13GoldenData.Capture(actual =>
                {
                    lineNumber++;
                    string location = "scenario=" + actual.Scenario + " tick=" + actual.Tick + " line=" + lineNumber;
                    string line = reader.ReadLine();
                    if (line == null) Assert.Fail(location + " column=row: baseline ended before this row.");
                    string[] expected = line.Split('\t');
                    Assert.That(expected.Length, Is.EqualTo(actual.Values.Length), location + " column=count");
                    for (int i = 0; i < actual.Values.Length; i++)
                        Assert.That(actual.Values[i], Is.EqualTo(expected[i]), location + " column=" + actual.Columns[i]);
                });
                string extra = reader.ReadLine();
                Assert.That(extra, Is.Null, "WP13 column=row: unexpected baseline row after all scenarios, line="
                    + (lineNumber + 1) + "; expected row (scenario/tick or snapshot key): " + extra);
            }
        }
    }
}
