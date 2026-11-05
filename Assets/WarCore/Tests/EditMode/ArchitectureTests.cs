using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class ArchitectureTests
    {
        internal static string AssetRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string assets = Path.Combine(directory.FullName, "Assets");
                if (Directory.Exists(Path.Combine(assets, "WarCore")))
                    return assets;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Cannot locate WarCore project assets.");
        }

        [Test]
        public void SimSourceContainsNoEngineClockOrGlobalRandomTokens()
        {
            string root = Path.Combine(AssetRoot(), "WarCore/Sim");
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThan(0));
            foreach (string file in files)
                Assert.That(SourceRules.ForbiddenTokens(File.ReadAllText(file)), Is.Empty, file);
            string asmdef = File.ReadAllText(Path.Combine(root, "WarCore.Sim.asmdef"));
            Assert.That(Regex.IsMatch(asmdef, "\"noEngineReferences\"\\s*:\\s*true"), Is.True);
        }

        [Test]
        public void SimAssemblyHasNoMutableStaticFieldsOrUpperLayerDependencies()
        {
            var assembly = typeof(SimClock).Assembly;
            foreach (FieldInfo field in FindDisallowedStaticFields(assembly.GetTypes()))
                Assert.Fail(field.DeclaringType.FullName + "." + field.Name);
            foreach (Type type in assembly.GetTypes())
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (SourceRules.IsImmutableTable(field))
                        Assert.That(SourceRules.TableChecksumMatches(field), Is.True, field.Name);
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal), Is.False);
                Assert.That(reference.Name.StartsWith("WarCore.", StringComparison.Ordinal), Is.False);
            }
        }

        internal static IEnumerable<FieldInfo> FindDisallowedStaticFields(IEnumerable<Type> types)
        {
            foreach (Type type in types)
            {
                if (type.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    continue;
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (!field.IsDefined(typeof(CompilerGeneratedAttribute), false) && !field.IsLiteral && !SourceRules.IsImmutableTable(field))
                        yield return field;
            }
        }

        [Test]
        public void StaticFieldCheckIgnoresCompilerGeneratedMembersButDetectsHandwrittenState()
        {
            var fields = FindDisallowedStaticFields(new[]{typeof(HandwrittenStaticFieldFixture), typeof(GeneratedStaticFieldFixture)}).ToArray();
            Assert.That(fields, Is.EquivalentTo(new[]{typeof(HandwrittenStaticFieldFixture).GetField("MutableValue", BindingFlags.Static | BindingFlags.NonPublic), typeof(HandwrittenStaticFieldFixture).GetField("ReadonlyArray", BindingFlags.Static | BindingFlags.NonPublic)}));
        }

        internal sealed class HandwrittenStaticFieldFixture
        {
            internal const int Constant = 1;
            internal static int MutableValue = 1;
            internal static readonly int[] ReadonlyArray = {1};
            [CompilerGenerated]
            internal static int GeneratedValue = 1;
        }

        [CompilerGenerated]
        internal sealed class GeneratedStaticFieldFixture
        {
            internal static int MutableValue = 1;
        }

        [Test]
        public void ProjectCodeHasNoChineseLiteralsAndFilesStayBelowReviewLimit()
        {
            foreach (string file in Directory.GetFiles(Path.Combine(AssetRoot(), "WarCore"), "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                Assert.That(Regex.IsMatch(source, "[\\u3400-\\u9fff]"), Is.False, file);
                Assert.That(File.ReadAllLines(file).Length, Is.LessThanOrEqualTo(600), file);
            }
        }

        [Test]
        public void LayerAssembliesDeclareOnlyDownwardDependencies()
        {
            string root = Path.Combine(AssetRoot(), "WarCore");
            foreach (string layer in new[]{"Sim", "Content", "Skin", "Presentation"})
                Assert.That(File.Exists(Path.Combine(root, layer, "WarCore." + layer + ".asmdef")), Is.True);
            string sim = File.ReadAllText(Path.Combine(root, "Sim/WarCore.Sim.asmdef"));
            Assert.That(sim, Does.Not.Contain("WarCore.Content"));
            Assert.That(sim, Does.Not.Contain("WarCore.Presentation"));
            string content = File.ReadAllText(Path.Combine(root, "Content/WarCore.Content.asmdef"));
            Assert.That(content, Does.Not.Contain("WarCore.Skin"));
            Assert.That(content, Does.Not.Contain("WarCore.Presentation"));
            string skin = File.ReadAllText(Path.Combine(root, "Skin/WarCore.Skin.asmdef"));
            Assert.That(skin, Does.Not.Contain("WarCore.Presentation"));
        }
    }
}
