using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // The intermittent test-runner crash in the M0c2S2 category traced to one 214,016 B stackalloc frame in the S2
    // fixture (docs/m0c/block2-independent-review.md, STK-1/STK-2). Two guards keep it from coming back: a source rule
    // that bounds every stackalloc site and keeps them out of loops, and explicit small-thread runs that prove the
    // fixtures fit in a fraction of any runtime's default stack. The explicit runs crash the host when they fail, so
    // they are selected by category (StackBound) rather than run with the suite. Mono raises managed thread stacks
    // below ~140 KB to 143,360 B, so on Mono the 64 KiB runs are regression insurance; the 64 KiB claim rests on the
    // native-thread measurements recorded in docs/m0c/block2-stack-investigation.md. CoreCLR honours the request.
    [TestFixture, Category("StackBudget")]
    public sealed class StackBudgetTests
    {
        private const int BytesPerSite = 16384;
        private const int KiB = 1024;
        private static readonly Regex Site = new Regex(@"\bstackalloc\s+([A-Za-z_][\w\.]*)\s*\[\s*([^\]]+?)\s*\]", RegexOptions.Compiled);
        private static readonly Regex LoopHeader = new Regex(@"\b(for|foreach|while)\s*\(|\bdo\s*$", RegexOptions.Compiled);
        private static readonly Regex MethodHeader = new Regex(
            @"^\s*(\[[^\]]*\]\s*)*((public|private|internal|protected|static|override|virtual|abstract|unsafe|new|async|extern|partial)\s+)+[^(){};]*\(",
            RegexOptions.Compiled);
        private static readonly Regex Literals = new Regex("@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", RegexOptions.Compiled);
        private static readonly Dictionary<string, int> Keywords = new Dictionary<string, int>
        {
            {"byte",1},{"sbyte",1},{"bool",1},{"short",2},{"ushort",2},{"char",2},{"int",4},{"uint",4},{"float",4},
            {"long",8},{"ulong",8},{"double",8},
        };

        [Test]
        public void EveryStackallocSiteStaysWithinBudgetAndOutsideLoops()
        {
            var failures = new List<string>();
            int sites = 0;
            string root = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore");
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                string original = File.ReadAllText(file);
                string code = BlankLiterals(SourceRules.StripComments(original));
                foreach (Match site in Site.Matches(code))
                {
                    sites++;
                    string where = path + ":" + Line(original, site.Index);
                    long count = ResolveCount(site.Groups[2].Value, where, failures);
                    int size = ResolveSize(site.Groups[1].Value, where, failures);
                    if (count >= 0 && size > 0 && count * size > BytesPerSite)
                        failures.Add(where + ": stackalloc " + site.Groups[1].Value + "[" + count + "] is " + count * size + " B; the budget is " + BytesPerSite + " B per site");
                    if (InsideLoop(code, site.Index))
                        failures.Add(where + ": stackalloc inside a loop grows the frame on every iteration; hoist it above the loop");
                }
            }
            Assert.That(sites, Is.GreaterThan(0));
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test, Explicit("A stack overflow kills the runner: select Category=StackBound or run by name."), Category("StackBound")]
        public void S2FixtureTicksFitOnA128KiBThread()
            => RunOnSmallStack(128 * KiB, () => { Simulation sim = M0c2S2Fixture.Create(); for (int tick = 0; tick < 40; tick++) M0c2S2Fixture.Step(sim); });

        [Test, Explicit("A stack overflow kills the runner: select Category=StackBound or run by name."), Category("StackBound")]
        public void S3ScriptsFitOnA64KiBThread()
            => RunOnSmallStack(64 * KiB, () =>
            {
                for (int package = 0; package < 2; package++)
                { Simulation sim = M0c2S3Fixture.Create(package != 0); for (int tick = 0; tick < 358; tick++) M0c2S3Fixture.Step(sim); }
            });

        [Test, Explicit("A stack overflow kills the runner: select Category=StackBound or run by name."), Category("StackBound")]
        public void S4ScriptsFitOnA64KiBThread()
            => RunOnSmallStack(64 * KiB, () =>
            {
                for (int package = 0; package < 2; package++)
                { Simulation sim = M0c2S4Fixture.Create(package != 0); for (int tick = 0; tick < 370; tick++) M0c2S4Fixture.Replay(sim); }
            });

        private static void RunOnSmallStack(int bytes, Action work)
        {
            Exception error = null;
            bool completed = false;
            var thread = new Thread(() => { try { work(); completed = true; } catch (Exception e) { error = e; } }, bytes);
            thread.Start();
            thread.Join();
            if (error != null)
                throw new AssertionException("the workload failed on a thread created with maxStackSize=" + bytes, error);
            Assert.That(completed, Is.True);
            TestContext.WriteLine("completed on a thread created with maxStackSize={0} B", bytes);
        }

        // Same length as the input, so site indexes still map onto the original text.
        internal static string BlankLiterals(string code)
            => Literals.Replace(code, match => match.Value[0] == '\'' ? match.Value : match.Value.Substring(0, 1) + new string(' ', match.Length - 2) + "\"");

        private static int Line(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index; i++)
                if (text[i] == '\n')
                    line++;
            return line;
        }

        private static long ResolveCount(string expression, string where, List<string> failures)
        {
            if (long.TryParse(expression, out long literal))
                return literal;
            string[] parts = expression.Split('.');
            if (parts.Length == 2)
            {
                Type owner = FindType(parts[0]);
                FieldInfo field = owner?.GetField(parts[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null && field.IsLiteral)
                    return Convert.ToInt64(field.GetRawConstantValue());
            }
            failures.Add(where + ": stackalloc length '" + expression + "' must be an integer literal or a const of a WarCore type");
            return -1;
        }

        private static int ResolveSize(string typeName, string where, List<string> failures)
        {
            if (Keywords.TryGetValue(typeName, out int keyword))
                return keyword;
            Type type = FindType(typeName.Substring(typeName.LastIndexOf('.') + 1));
            if (type == null || !type.IsValueType)
            {
                failures.Add(where + ": stackalloc element type '" + typeName + "' is not a known WarCore value type");
                return -1;
            }
            try
            {
                return (int)typeof(StackBudgetTests).GetMethod(nameof(SizeOf), BindingFlags.Static | BindingFlags.NonPublic)
                    .MakeGenericMethod(type).Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                failures.Add(where + ": cannot size '" + typeName + "': " + e.InnerException?.Message);
                return -1;
            }
        }

        private static int SizeOf<T>() where T : struct => MemoryMarshal.AsBytes(new T[1].AsSpan()).Length;

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in new[] { typeof(SimClock).Assembly, typeof(StackBudgetTests).Assembly })
                foreach (Type type in assembly.GetTypes())
                    if (type.Name == name)
                        return type;
            return null;
        }

        // Walk enclosing blocks from the site outward: a loop header before reaching the method's own header means
        // the allocation repeats per iteration. A lambda body ("=> {") is its own frame and ends the walk.
        internal static bool InsideLoop(string code, int index)
        {
            int depth = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                char c = code[i];
                if (c == '}')
                    depth++;
                else if (c == '{')
                {
                    if (depth > 0) { depth--; continue; }
                    string header = HeaderBefore(code, i);
                    if (header.EndsWith("=>", StringComparison.Ordinal))
                        return false;
                    if (LoopHeader.IsMatch(header))
                        return true;
                    if (MethodHeader.IsMatch(header))
                        return false;
                }
            }
            return false;
        }

        private static string HeaderBefore(string code, int brace)
        {
            int parens = 0;
            int start = 0;
            for (int i = brace - 1; i >= 0; i--)
            {
                char c = code[i];
                if (c == ')') parens++;
                else if (c == '(') parens--;
                else if (parens == 0 && (c == ';' || c == '{' || c == '}')) { start = i + 1; break; }
            }
            return code.Substring(start, brace - start).Trim();
        }

        [TestCase("void M(){ for(int i=0;i<2;i++){ Span<byte> b=stackalloc byte[1]; } }", true)]
        [TestCase("void M(){ Span<byte> b=stackalloc byte[1]; for(int i=0;i<2;i++){ b[0]=1; } }", false)]
        [TestCase("void M(){ foreach(var x in y){ if(x){ Span<byte> b=stackalloc byte[1]; } } }", true)]
        [TestCase("void M(){ while(a){ Action f=()=>{ Span<byte> b=stackalloc byte[1]; }; } }", false)]
        [TestCase("public void M(){ if(a){ Span<byte> b=stackalloc byte[1]; } }", false)]
        [TestCase("class C { private void M(){ do { Span<byte> b=stackalloc byte[1]; } while(a); } }", true)]
        public void LoopDetectionFollowsEnclosingBlocks(string code, bool inside)
            => Assert.That(InsideLoop(code, code.IndexOf("stackalloc", StringComparison.Ordinal)), Is.EqualTo(inside));

        [Test]
        public void LiteralBlankingKeepsLengthAndRemovesQuotedSites()
        {
            string code = "var s = \"stackalloc byte[9]\"; var v = @\"x \"\" stackalloc\"; char q = '\"'; Span<byte> b = stackalloc byte[1];";
            string blank = BlankLiterals(code);
            Assert.That(blank.Length, Is.EqualTo(code.Length));
            Assert.That(Site.Matches(blank).Count, Is.EqualTo(1));
            Assert.That(blank.IndexOf("stackalloc byte[1]", StringComparison.Ordinal), Is.EqualTo(code.IndexOf("stackalloc byte[1]", StringComparison.Ordinal)));
        }
    }
}
