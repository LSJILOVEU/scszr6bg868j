using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace WarCore.Tests
{
    // Headless only: Roslyn is not available to the Unity test assembly. BoxContractTests.ShapeSensitiveReadsAreLockedPerFile
    // finds shape reads by receiver name, so a read through a renamed variable would slip past it. This binds the same
    // sources the headless project compiles and resolves every use of the locked Collider members by symbol, then requires
    // the per-file symbol count to equal the receiver-name count in every locked root. Together: symbol = name = locked.
    [Category("M0c3a")]
    public sealed class ShapeReadSymbolTests
    {
        private static readonly string[] Sources = { "Assets/WarCore/Sim", "Assets/WarCore/Content", "Assets/WarCore/Combat", "Assets/WarCore/Battle", "Assets/WarCore/Interim", "Assets/WarCore/Control", "Assets/WarCore/Presentation", "Assets/WarCore/Tests/EditMode" };
        private static readonly HashSet<string> Properties = new HashSet<string>(StringComparer.Ordinal) { "Radius", "Height", "ShapeKind" };
        private static readonly HashSet<string> Rebuilds = new HashSet<string>(StringComparer.Ordinal) { "WithLayer", "WithStatic", "WithHeight" };

        [Test]
        public void ReceiverNameLockSeesEveryColliderShapeReadBySymbol()
        {
            var files = Sources.SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => Path.GetFileName(f) != "AllocationMeterTests.cs").Select(f => f.Replace('\\', '/')).ToList();
            CSharpCompilation compilation = Compile(files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), Options, f)));
            Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()), Is.Empty,
                "Every read must bind before it can be counted.");
            INamedTypeSymbol collider = compilation.GetTypeByMetadataName("WarCore.Sim.Collider");
            Assert.That(collider, Is.Not.Null);

            var mismatches = new List<string>();
            int fileCount = 0, sites = 0;
            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                string directory = Path.GetDirectoryName(tree.FilePath).Replace('\\', '/');
                if (!BoxContractTests.Roots.Contains(directory) || BoxContractTests.Excluded.Contains(Path.GetFileName(tree.FilePath)))
                    continue;
                List<string> reads = Reads(compilation, tree, collider);
                int named = BoxContractTests.Count(tree.ToString());
                if (reads.Count != named)
                    mismatches.Add(tree.FilePath + ": symbol " + reads.Count + ", by name " + named + " [" + string.Join("; ", reads) + "]");
                if (reads.Count != 0) { fileCount++; sites += reads.Count; }
            }
            Assert.That(mismatches, Is.Empty, "A shape read the receiver-name lock cannot see; name the receiver collider/value/baseline or review the regex.");

            // Positive control: a read through an unlisted name binds to the symbol but is invisible to the name lock.
            const string renamed = "namespace WarCore.Sim { static class Probe { static long R(in Collider q) => q.Radius.Raw + (long)q.ShapeKind; } }";
            SyntaxTree probe = CSharpSyntaxTree.ParseText(renamed, Options, "Probe.cs");
            CSharpCompilation withProbe = compilation.AddSyntaxTrees(probe);
            Assert.That((Reads(withProbe, probe, withProbe.GetTypeByMetadataName("WarCore.Sim.Collider")).Count, BoxContractTests.Count(renamed)), Is.EqualTo((2, 0)));
            TestContext.WriteLine("Shape reads by symbol: {0} files, {1} sites, all visible to the name lock.", fileCount, sites);
        }

        private static readonly CSharpParseOptions Options = new CSharpParseOptions(LanguageVersion.CSharp9);

        private static CSharpCompilation Compile(IEnumerable<SyntaxTree> trees)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
            references.Add(MetadataReference.CreateFromFile(typeof(Assert).Assembly.Location));
            return CSharpCompilation.Create("WarCore.Sim.Tests", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        }

        // Reads inside Collider itself are its own encoding (hash and extents), not a consumer assuming a shape.
        private static List<string> Reads(CSharpCompilation compilation, SyntaxTree tree, INamedTypeSymbol collider)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            var reads = new List<string>();
            foreach (SimpleNameSyntax name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                ISymbol symbol = model.GetSymbolInfo(name).Symbol;
                if (symbol == null || !SymbolEqualityComparer.Default.Equals(symbol.ContainingType?.OriginalDefinition, collider))
                    continue;
                if (!(symbol is IPropertySymbol && Properties.Contains(symbol.Name)) && !(symbol is IMethodSymbol && Rebuilds.Contains(symbol.Name)))
                    continue;
                if (SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(name.SpanStart)?.ContainingType, collider))
                    continue;
                reads.Add(name.Parent + " @" + (tree.GetLineSpan(name.Span).StartLinePosition.Line + 1));
            }
            return reads;
        }
    }
}
