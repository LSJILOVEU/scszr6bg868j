using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Interim;

namespace WarCore.Tests
{
    public sealed class InterimDeterminismTests
    {
        [Test]
        public void InterimDeterminism_SourceHasNoForbiddenTokens()
        {
            string root = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Interim");
            Assert.That(Directory.Exists(root), Is.True);
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty);
            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.That(SourceRules.ForbiddenTokens(source), Is.Empty, file);
                Assert.That(ExtraForbiddenTokens(source), Is.Empty, file);
            }
        }

        [Test]
        public void InterimDeterminism_AssemblyHasNoStaticStateAndOnlySimOrBclReferences()
        {
            Assembly assembly = typeof(InterimLocomotion).Assembly;
            Assert.That(ArchitectureTests.FindDisallowedStaticFields(assembly.GetTypes()), Is.Empty);
            AssemblyName[] references = assembly.GetReferencedAssemblies();
            Assert.That(references.Select(r => r.Name), Does.Contain("WarCore.Sim"));
            foreach (AssemblyName reference in references)
                Assert.That(reference.Name == "WarCore.Sim" || IsBcl(reference.Name), Is.True, reference.FullName);
        }

        [Test]
        public void InterimDeterminism_AsmdefDisablesEngineAndReferencesOnlySim()
        {
            string path = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Interim/WarCore.Interim.asmdef");
            Assert.That(File.Exists(path), Is.True);
            string source = File.ReadAllText(path);
            Assert.That(Regex.IsMatch(source, "\"noEngineReferences\"\\s*:\\s*true"), Is.True);
            Match references = Regex.Match(source, "\"references\"\\s*:\\s*\\[([^]]*)\\]");
            Assert.That(references.Success, Is.True);
            Assert.That(Regex.Matches(references.Groups[1].Value, "\"([^\"]+)\"")
                .Cast<Match>().Select(m => m.Groups[1].Value), Is.EqualTo(new[] { "WarCore.Sim" }));
        }

        [TestCase("Dictionary")]
        [TestCase("HashSet")]
        [TestCase("SortedDictionary")]
        [TestCase("SortedSet")]
        [TestCase("ConcurrentDictionary")]
        [TestCase("Hashtable")]
        [TestCase("Random")]
        public void InterimDeterminism_ExtraTokenInjectionIsDetectedWithNegativeControls(string token)
        {
            Assert.That(ExtraForbiddenTokens(token + " value;"), Is.EqualTo(new[] { token }));
            Assert.That(ExtraForbiddenTokens("global::System.Collections.Generic." + token + " value;"), Is.Not.Empty);
            Assert.That(ExtraForbiddenTokens("using Alias = System." + token + ";"), Is.Not.Empty);
            Assert.That(ExtraForbiddenTokens("\"//\"; " + token + " value;"), Is.Not.Empty);
            Assert.That(ExtraForbiddenTokens("\"" + token + "\""), Is.Not.Empty);
            Assert.That(ExtraForbiddenTokens("/* " + token + " */ // " + token), Is.Empty);
            Assert.That(ExtraForbiddenTokens(token + "Suffix _" + token + ";"), Is.Empty);
        }

        [TestCase("UnityEngine")]
        [TestCase("DateTime")]
        [TestCase("Stopwatch")]
        [TestCase("Thread")]
        [TestCase("Task")]
        [TestCase("Guid")]
        [TestCase("Random")]
        [TestCase("float")]
        [TestCase("double")]
        [TestCase("decimal")]
        [TestCase("MathF")]
        [TestCase("Mathf")]
        [TestCase("Vector2")]
        [TestCase("Vector3")]
        [TestCase("Environment .")]
        [TestCase("Time .")]
        [TestCase("Math .")]
        public void InterimDeterminism_SharedTokenInjectionIsDetectedWithNegativeControls(string token)
        {
            string name = token.TrimEnd(' ', '.');
            Assert.That(SourceRules.ForbiddenTokens(token + " value;"), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("global::System." + token + " value;"), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("\"//\"; " + token + " value;"), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("\"" + token + "\""), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("/* " + token + " */ // " + token), Is.Empty);
            Assert.That(SourceRules.ForbiddenTokens(name + "Suffix _" + name + ";"), Is.Empty);
            if (token.EndsWith(".", StringComparison.Ordinal))
                Assert.That(SourceRules.ForbiddenTokens(name + " /* gap */ \n . Member"), Is.Not.Empty);
        }

        private static IEnumerable<string> ExtraForbiddenTokens(string source)
        {
            const string pattern = @"\b(Dictionary|HashSet|SortedDictionary|SortedSet|ConcurrentDictionary|Hashtable|Random)\b";
            return Regex.Matches(SourceRules.StripComments(source), pattern).Cast<Match>().Select(m => m.Value);
        }

        private static bool IsBcl(string name) => name == "mscorlib" || name == "netstandard"
            || name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
    }
}
