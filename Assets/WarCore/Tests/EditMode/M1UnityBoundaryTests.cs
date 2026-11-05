using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class M1UnityBoundaryTests
    {
        private static string UnityRoot => Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Unity");

        [Test]
        public void M1_UnityAssembly_ReferencesOnlyPresentation()
        {
            string source = File.ReadAllText(Path.Combine(UnityRoot, "WarCore.Unity.asmdef"));
            Assert.That(StringArray(source, "references"), Is.EqualTo(new[] { "WarCore.Presentation" }));
            Assert.That(source, Does.Match("\"noEngineReferences\"\\s*:\\s*false"));
            Assert.That(source, Does.Match("\"rootNamespace\"\\s*:\\s*\"WarCore.Unity\""));
        }

        [Test]
        public void M1_UnitySources_DoNotMentionSimulationNamespace()
        {
            string[] files = Directory.GetFiles(UnityRoot, "*.cs", SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty);
            foreach (string file in files)
                Assert.That(File.ReadAllText(file), Does.Not.Contain("WarCore.Sim"), file);
        }

        [Test]
        public void M1_UnityEditorAssembly_IsEditorOnly()
        {
            string source = File.ReadAllText(Path.Combine(UnityRoot, "Editor/WarCore.Unity.Editor.asmdef"));
            Assert.That(StringArray(source, "includePlatforms"), Is.EqualTo(new[] { "Editor" }));
            Assert.That(StringArray(source, "references"), Is.EqualTo(new[] { "WarCore.Unity" }));
            Assert.That(source, Does.Match("\"rootNamespace\"\\s*:\\s*\"WarCore.Unity.Editor\""));
        }

        private static string[] StringArray(string source, string property)
        {
            MatchCollection matches = Regex.Matches(source, "\"" + property + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            Assert.That(matches.Count, Is.EqualTo(1), property);
            string contents = matches[0].Groups[1].Value;
            Assert.That(contents, Does.Match("^\\s*(?:\"[^\"]*\"\\s*(?:,\\s*\"[^\"]*\"\\s*)*)?$"), property);
            return Regex.Matches(contents, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        }
    }
}
