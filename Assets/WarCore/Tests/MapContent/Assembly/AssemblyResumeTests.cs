using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F2a: a snapshot written by the builder before it was split must restore into the split builder and then run on,
    // tick by tick, through the same state hashes. The snapshot and the hashes are recorded values, not a second implementation.
    public sealed class AssemblyResumeTests
    {
        internal const string GoldenPath = "Assets/WarCore/Tests/MapContent/Assembly/Golden/m6-t300.txt";
        internal const int SnapshotTick = 300, ResumeTicks = 300;

        private static M6World Fresh() => new M6World(DemoRecipes.M6(), DemoRecipes.M6MapBytes());

        private static string Hex(ulong value) => value.ToString("X16");

        private static string[] Golden() => File.ReadAllLines(GoldenPath);

        [Test]
        public void RecordedM6Snapshot_RestoresAndRunsOnThroughTheRecordedHashes()
        {
            string[] lines = Golden();
            Assert.That(lines.Length, Is.EqualTo(3 + ResumeTicks));
            byte[] snapshot = Convert.FromBase64String(lines[1].Substring("snapshot-base64 ".Length));
            Assert.That(AssemblyWorlds.Sha(snapshot), Is.EqualTo(lines[0].Substring("snapshot-sha256 ".Length)));
            M6World world = Fresh();
            world.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(world.Sim.Clock.Tick, Is.EqualTo((long)SnapshotTick));
            Assert.That(Hex(world.Hash), Is.EqualTo(lines[2].Substring("state-hash ".Length)));
            for (int i = 0; i < ResumeTicks; i++)
            {
                world.Step();
                string[] row = lines[3 + i].Split(' ');
                Assert.That(world.Sim.Clock.Tick, Is.EqualTo(long.Parse(row[0])));
                Assert.That(Hex(world.Hash), Is.EqualTo(row[1]), "tick " + row[0]);
            }
        }

        [Test]
        public void RecordedM6Snapshot_IsTheOneTheCurrentBuilderWritesAtTickThreeHundred()
        {
            string[] lines = Golden();
            M6World world = Fresh();
            world.Step(SnapshotTick);
            Assert.That(AssemblyWorlds.Sha(world.Sim.SaveRetainedSnapshot()), Is.EqualTo(lines[0].Substring("snapshot-sha256 ".Length)));
        }

        [Category("Manual"), Test, Explicit("Records the F2a resume lock.")]
        public void RecordResumeLock()
        {
            string path = Environment.GetEnvironmentVariable("WARCORE_F2A_RESUME");
            Assert.That(string.IsNullOrEmpty(path), Is.False);
            Assert.That(File.Exists(path), Is.False);
            M6World source = Fresh();
            source.Step(SnapshotTick);
            byte[] snapshot = source.Sim.SaveRetainedSnapshot();
            M6World world = Fresh();
            world.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(world.Hash, Is.EqualTo(source.Hash));
            var lines = new List<string>
            {
                "snapshot-sha256 " + AssemblyWorlds.Sha(snapshot),
                "snapshot-base64 " + Convert.ToBase64String(snapshot),
                "state-hash " + Hex(world.Hash),
            };
            for (int i = 0; i < ResumeTicks; i++)
            {
                world.Step();
                source.Step();
                Assert.That(world.Hash, Is.EqualTo(source.Hash), "restored and uninterrupted runs agree");
                lines.Add(world.Sim.Clock.Tick + " " + Hex(world.Hash));
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
    }
}
