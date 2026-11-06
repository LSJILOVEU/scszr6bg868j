using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace WarCore.Tests
{
    // Records AssemblyGolden.Lines from the builder as it is. Output path comes from WARCORE_F2A_OUT; nothing is overwritten.
    // The lock was recorded once on the unmodified builder, before any production change of the assembly framework.
    public sealed class AssemblyRecorder
    {
        [Category("Manual"), Test, Explicit("Records the F2a assembly lock.")]
        public void RecordAssemblyLock()
        {
            string path = Environment.GetEnvironmentVariable("WARCORE_F2A_OUT");
            Assert.That(string.IsNullOrEmpty(path), Is.False);
            Assert.That(File.Exists(path), Is.False);
            var lines = new List<string>();
            foreach (string world in AssemblyWorlds.Names)
                foreach (KeyValuePair<string, string> pair in AssemblyWorlds.Describe(world))
                    lines.Add("            \"" + world + "|" + pair.Key + "|" + pair.Value + "\",");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            string guards = Environment.GetEnvironmentVariable("WARCORE_F2A_GUARDS");
            Assert.That(string.IsNullOrEmpty(guards), Is.False);
            Assert.That(File.Exists(guards), Is.False);
            var rows = new List<string>();
            foreach (var guard in AssemblyGuardCases.Cases)
                rows.Add("            \"" + guard.Name + "|" + AssemblyGuardCases.Outcome(guard.Run).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",");
            File.WriteAllLines(guards, rows, new UTF8Encoding(false));
        }
    }
}
