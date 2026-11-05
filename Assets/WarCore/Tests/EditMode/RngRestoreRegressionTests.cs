using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("M33")]
    public sealed class RngRestoreRegressionTests
    {
        [Test]
        public void LateStreamRetainedAcrossRestoreMatchesEveryOnlineTickAndHash()
        {
            var online = new Simulation(91);
            var replay = new Simulation(91);
            byte[] snapshot = replay.SaveSnapshot();
            var held = replay.Rng.Stream("late");
            for (int i = 0; i < 7; i++)
                replay.Rng.NextUInt64(held);
            replay.RestoreSnapshot(snapshot);
            Assert.That(replay.StateHash(), Is.EqualTo(online.StateHash()));
            for (int tick = 0; tick < 40; tick++)
            {
                Assert.That(replay.Rng.NextUInt64(held), Is.EqualTo(online.Rng.NextUInt64(online.Rng.Stream("late"))), "tick=" + tick);
                replay.Step(default);
                online.Step(default);
                Assert.That(replay.StateHash(), Is.EqualTo(online.StateHash()), "tick=" + tick);
            }
        }

        [Test]
        public void StreamHandleContainsOnlyAnIdAndCannotExposeMutableState()
        {
            Type handle = typeof(SimRandom).GetMethod("Stream").ReturnType;
            Assert.That(handle.IsValueType, Is.True);
            FieldInfo[] fields = handle.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Length, Is.EqualTo(1));
            Assert.That(fields[0].FieldType, Is.EqualTo(typeof(string)));
            Assert.That(fields[0].IsInitOnly, Is.True);
        }

        internal static bool HoldsStreamObject(string source)
        {
            string code = SourceRules.StripComments(source);
            code = Regex.Replace(code, "@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            return Regex.IsMatch(code, @"\b(RandomStream|RandomStreamState)\b");
        }

        [Test]
        public void SourceGuardCoversProductFixturesAndExamples()
        {
            var failures = new System.Collections.Generic.List<string>();
            foreach (string root in new[] { "Assets/WarCore", "Tools" })
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    // Windows enumerates with backslashes; the rule paths below are written with '/'.
                    string path = file.Replace('\\', '/');
                    if (path.EndsWith("/Sim/SimRandom.cs", StringComparison.Ordinal) || path.Contains("/out/"))
                        continue;
                    if (HoldsStreamObject(File.ReadAllText(file)))
                        failures.Add(path);
                }
            Assert.That(failures, Is.Empty);
        }

        [TestCase("private RandomStream stream;")]
        [TestCase("using R = WarCore.Sim.RandomStream; private R stream;")]
        [TestCase("private System.Collections.Generic.List<RandomStreamState> streams;")]
        public void SourceGuardRejectsInjectedObjectHoldingSystems(string field)
        {
            string source = "class InjectedSystem : ISimSystem { " + field + " public void Step(SimContext c) {} }";
            Assert.That(HoldsStreamObject(source), Is.True);
            Assert.That(HoldsStreamObject("// " + field + "\nprivate RandomStreamId id;"), Is.False);
        }
        private sealed class LateSystem : ISimSystem, ISimStateSerializer
        {
            private readonly RandomStreamId stream = new RandomStreamId("late-system");
            private ulong result;
            public int Order => 1;
            public string StateTypeId => "test.late-rng";
            public int StateVersion => 1;
            public void Step(SimContext context)
            {
                if (context.Clock.Tick >= 2)
                    result = context.Rng.NextUInt64(stream);
            }
            public void WriteState(StateHash hash) => hash.Add(result);
            public void SaveState(SimStateWriter writer) => writer.UInt64(result);
            public void LoadState(SimStateReader reader) => result = reader.UInt64();
        }

        [Test]
        public void SystemKeepsOnlyIdWhileLateStreamIsRemovedAndRecreatedByRestore()
        {
            var online = new Simulation(17);
            var replay = new Simulation(17);
            online.Register("late", new LateSystem());
            replay.Register("late", new LateSystem());
            online.Step(default);
            replay.Step(default);
            byte[] snapshot = replay.SaveSnapshot();
            for (int tick = 2; tick <= 9; tick++)
                replay.Step(default);
            replay.RestoreSnapshot(snapshot);
            Assert.That(replay.StateHash(), Is.EqualTo(online.StateHash()));
            for (int tick = 2; tick <= 50; tick++)
            {
                online.Step(default);
                replay.Step(default);
                Assert.That(replay.StateHash(), Is.EqualTo(online.StateHash()), "tick=" + tick);
            }
        }
    }
}
