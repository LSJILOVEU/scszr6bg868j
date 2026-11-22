using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace WarCore.Tests
{
    // HC-0: what SizeLiteralScanner counts as a size literal, and how the ledger is read and compared.
    public sealed class SizeLiteralScannerTests
    {
        private static string[] Tokens(string source) => SizeLiteralScanner.Scan(source).Select(h => h.Token).ToArray();

        [Test]
        public void R1_RealLiteralsAreFound()
        {
            Assert.That(Tokens("public const float BodyHeight = 1.7f;"), Is.EqualTo(new[] { "1.7f" }));
            Assert.That(Tokens("double reach = 0.25;"), Is.EqualTo(new[] { "0.25" }));
        }

        [Test]
        public void R1_IntegerWithARealSuffixIsFound() =>
            Assert.That(Tokens("public const float GrenadeReach = 28f;"), Is.EqualTo(new[] { "28f" }));

        [Test]
        public void R1_ZeroAndOneAreNotSizes() =>
            Assert.That(Tokens("float a = 0f, b = 1.0f, c = 0.0f, d = 1f;"), Is.Empty);

        [Test]
        public void R1_TolerancesAndUnitConversionsAreNotSizes()
        {
            Assert.That(Tokens("const float Tolerance = 1e-3f; double big = 2E6, small = 5e-9;"), Is.Empty);
            Assert.That(Tokens("float m = mm / 1000f; double s = us / 1000000.0; float k = 1000.5f;"), Is.EqualTo(new[] { "1000.5f" }));
        }

        [Test]
        public void R1_HalvingAndDoublingAreNotSizes()
        {
            Assert.That(Tokens("float h = w / 2f, q = w * 0.5f, r = 2f * w, t = 0.5f * w, u = w /2.0f;"), Is.Empty);
            Assert.That(Tokens("public const float SnapRadius = 0.5f; var v = new Vector3(0.5f, h, 2f);"), Is.EqualTo(new[] { "0.5f", "0.5f", "2f" }));
            Assert.That(Tokens("float a = w + 2f, b = w - 0.5f;"), Is.EqualTo(new[] { "2f", "0.5f" }));
        }

        [Test]
        public void R1_ColourLinesAreSkipped()
        {
            Assert.That(Tokens("var c = new Color(0.2f, 0.3f, 0.4f);"), Is.Empty);
            Assert.That(Tokens("var c = new Color32(20, 30, 40, 255); float w = 0.5f;"), Is.Empty);
            Assert.That(Tokens("float w = 0.5f;"), Is.EqualTo(new[] { "0.5f" }));
            Assert.That(Tokens("var c = UnityEngine.Color(0.2f, 0.3f, 0.4f);"), Is.Empty);
            Assert.That(Tokens("DrawLine(a, b, 2.5f); SetColor(c); var t = TeamColor(3.5f);"), Is.EqualTo(new[] { "2.5f", "3.5f" }));
        }

        [Test]
        public void R1_DigitSeparatorsAndLeadingDotsAreFound()
        {
            Assert.That(Tokens("float a = 1_500f; double b = 2_000.25; float c = .25f; float d = 1__5f;"),
                Is.EqualTo(new[] { "1500f", "2000.25", ".25f", "15f" }));
            Assert.That(() => Tokens("float m = mm / 1_000f;"), Throws.Nothing);
            Assert.That(Tokens("float m = mm / 1_000f;"), Is.Empty);
            Assert.That(Tokens("var r = arr[1..5]; int _2f = x;"), Is.Empty);
        }

        [Test]
        public void R1_HexAndIdentifierDigitsAreNotLiterals() =>
            Assert.That(Tokens("private const ulong Salt = 0x6a09e667bb67ae85UL; int v2f = a3d;"), Is.Empty);

        [Test]
        public void R2_LiteralSimFixConstructionIsFound()
        {
            Assert.That(Tokens("private static readonly SimFix Radius = SimFix.Ratio(1, 4);"), Is.EqualTo(new[] { "SimFix.Ratio(1,4)" }));
            Assert.That(Tokens("var h = SimFix.FromInt(3);"), Is.EqualTo(new[] { "SimFix.FromInt(3)" }));
            Assert.That(Tokens("var h = SimFix.FromInt(1);"), Is.EqualTo(new[] { "SimFix.FromInt(1)" }));
            Assert.That(Tokens("var r = SimFix.FromRaw(-16384);"), Is.EqualTo(new[] { "SimFix.FromRaw(-16384)" }));
        }

        [Test]
        public void R2_DigitSeparatorsAreFound() =>
            Assert.That(Tokens("var c = SimFix.FromInt(1_000); var z = SimFix.FromInt(0_0);"), Is.EqualTo(new[] { "SimFix.FromInt(1000)" }));

        [Test]
        public void R2_ZeroOrComputedArgumentsAreNotSizes() =>
            Assert.That(Tokens("var a = SimFix.FromInt(0); var b = SimFix.FromInt(width); var c = SimFix.Ratio(0, 0); var d = SimFix.Ratio(x, 4);"),
                Is.Empty);

        [Test]
        public void R2_ARatioWithOneZeroSideIsFound() =>
            Assert.That(Tokens("var a = SimFix.Ratio(0, 7);"), Is.EqualTo(new[] { "SimFix.Ratio(0,7)" }));

        [Test]
        public void R3_ScaleTimesOrOverALiteralIsFound()
        {
            Assert.That(Tokens("long h = 2 * SimFix.Scale;"), Is.EqualTo(new[] { "2*SimFix.Scale" }));
            Assert.That(Tokens("long r = SimFix.Scale / 4;"), Is.EqualTo(new[] { "SimFix.Scale/4" }));
            Assert.That(Tokens("long r = SimFix.Scale * 3L;"), Is.EqualTo(new[] { "SimFix.Scale*3L" }));
            Assert.That(Tokens("long r = x * SimFix.Scale + SimFix.Scale / w;"), Is.Empty);
        }

        [Test]
        public void R3_DigitSeparatorsAreFound() =>
            Assert.That(Tokens("long h = 2_000 * SimFix.Scale; long r = SimFix.Scale / 1_0;"), Is.EqualTo(new[] { "2000*SimFix.Scale", "SimFix.Scale/10" }));

        [Test]
        public void R4_DigitSeparatorsAreFound()
        {
            Assert.That(Tokens("public const long ReachMm = 28_000;"), Is.EqualTo(new[] { "ReachMm=28000" }));
            Assert.That(Tokens("public const long OneTicks = 0_1;"), Is.Empty);
        }

        [Test]
        public void R4_UnitNamedConstantsAreFound()
        {
            Assert.That(Tokens("private const long SpeedMmPerSec = 6000;"), Is.EqualTo(new[] { "SpeedMmPerSec=6000" }));
            Assert.That(Tokens("public const long FuseTicks = 33, CooldownTicks = 90, ReachMm = 28000;"),
                Is.EqualTo(new[] { "FuseTicks=33", "CooldownTicks=90", "ReachMm=28000" }));
            Assert.That(Tokens("internal static readonly int WallHeight = 3;"), Is.EqualTo(new[] { "WallHeight=3" }));
        }

        [Test]
        public void R4_CapsCountsAndPlainVariablesAreNot()
        {
            Assert.That(Tokens("public const long MaxDistanceMm = 100000, MinStepMm = 10;"), Is.Empty);
            Assert.That(Tokens("public const int MaxTeams = 8; private const int HouseReserves = 12;"), Is.Empty);
            Assert.That(Tokens("int Width = 40; long speedMm = 6000;"), Is.Empty);
            Assert.That(Tokens("public const long OneTicks = 1, NoRange = 0;"), Is.Empty);
        }

        [Test]
        public void R4_ADeclarationAlreadyCaughtByR3IsCountedOnce()
        {
            Assert.That(Tokens("public const long ClearanceRaw = 2 * SimFix.Scale;"), Is.EqualTo(new[] { "2*SimFix.Scale" }));
            Assert.That(Tokens("private static readonly SimFix SnapRadius = SimFix.Ratio(9, 10);"), Is.EqualTo(new[] { "SimFix.Ratio(9,10)" }));
            Assert.That(Tokens("public const double SpanMetres = 3 + 0.25;"), Is.EqualTo(new[] { "0.25" }));
        }

        [Test]
        public void CommentsStringsAndCharsAreBlanked()
        {
            Assert.That(Tokens("int a = b; // 1.5 m above the floor, 2 * SimFix.Scale"), Is.Empty);
            Assert.That(Tokens("int a = /* 2.5f */ b;"), Is.Empty);
            Assert.That(Tokens("string s = \"1.5f\" + @\"0.25\" + $\"{x} 3.5f\"; char c = '1';"), Is.Empty);
            Assert.That(Tokens("string s = \"a \\\" 1.5f\"; float f = 2.5f;"), Is.EqualTo(new[] { "2.5f" }));
            Assert.That(Tokens("string s = @\"a \"\" 1.5f\"; float f = 2.5f;"), Is.EqualTo(new[] { "2.5f" }));
            Assert.That(Tokens("string s = @\"a \"\" \\\"; float f = 2.5f;"), Is.EqualTo(new[] { "2.5f" }));
            Assert.That(Tokens("char q = '\"'; float f = 2.5f; char e = '\\''; float g = 3.5f;"), Is.EqualTo(new[] { "2.5f", "3.5f" }));
        }

        [Test]
        public void LineNumbersSurviveBlockComments()
        {
            List<SizeLiteralScanner.Hit> hits = SizeLiteralScanner.Scan("/* one\ntwo\n*/\nfloat f = 2.5f;\r\nfloat g = 3.5f;");
            Assert.That(hits.Select(h => h.Line).ToArray(), Is.EqualTo(new[] { 4, 5 }));
        }

        [Test]
        public void ScopeLeavesOutTestsAndVerification()
        {
            Assert.That(SizeLiteralScanner.InScope("Unity/ShowcaseFx.cs"), Is.True);
            Assert.That(SizeLiteralScanner.InScope("Unity/TestsHelper.cs"), Is.True);
            Assert.That(SizeLiteralScanner.InScope("Unity/TestsData/Helper.cs"), Is.True);
            Assert.That(SizeLiteralScanner.InScope("Unity/Tests/Show1ViewsTests.cs"), Is.False);
            Assert.That(SizeLiteralScanner.InScope("Tests/EditMode/A.cs"), Is.False);
            Assert.That(SizeLiteralScanner.InScope("Verification/Generated/B.cs"), Is.False);
            Assert.That(SizeLiteralScanner.InScope("Unity/ShowcaseFx.cs.meta"), Is.False);
        }

        private static SortedDictionary<string, List<SizeLiteralScanner.Hit>> Tree(string file, string source) =>
            new SortedDictionary<string, List<SizeLiteralScanner.Hit>> { { file, SizeLiteralScanner.Scan(source) } };

        [Test]
        public void CompareReportsANewLiteral()
        {
            var ledger = SizeLiteralGuardTests.Parse(new[] { "debt-max\t1", "Unity/A.cs\t1.5f\t1\tdebt" });
            List<string> problems = SizeLiteralGuardTests.Compare(Tree("Unity/A.cs", "float a = 1.5f, b = 1.5f;"), ledger);
            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.StartWith("NEW size literal '1.5f' (2 found, 1 listed): Unity/A.cs:1"));
        }

        [Test]
        public void CompareReportsAnUnlistedFile()
        {
            var ledger = SizeLiteralGuardTests.Parse(new[] { "debt-max\t0" });
            List<string> problems = SizeLiteralGuardTests.Compare(Tree("Unity/A.cs", "float a = 0.5f;"), ledger);
            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.StartWith("NEW size literal '0.5f' (1 found, 0 listed)"));
        }

        [Test]
        public void CompareReportsARemovedLiteral()
        {
            var ledger = SizeLiteralGuardTests.Parse(new[] { "debt-max\t3", "Unity/A.cs\t1.5f\t3\tdebt" });
            List<string> problems = SizeLiteralGuardTests.Compare(Tree("Unity/A.cs", "float a = 1.5f;"), ledger);
            Assert.That(problems, Is.EqualTo(new[] { "FEWER Unity/A.cs 1.5f: ledger line 2 says 3, the code has 1  -> set the count to 1 and lower debt-max" }));
            problems = SizeLiteralGuardTests.Compare(Tree("Unity/A.cs", "float a = b;"), ledger);
            Assert.That(problems, Is.EqualTo(new[] { "FEWER Unity/A.cs 1.5f: ledger line 2 says 3, the code has 0  -> delete the row and lower debt-max" }));
        }

        [Test]
        public void CompareAcceptsAnExactLedger()
        {
            var ledger = SizeLiteralGuardTests.Parse(new[] { "debt-max\t1", "Unity/A.cs\t1.5f\t1\tdebt", "Unity/A.cs\t28f\t1\tui\tdrawn flight" });
            Assert.That(SizeLiteralGuardTests.Compare(Tree("Unity/A.cs", "float a = 1.5f, b = 28f;"), ledger), Is.Empty);
        }

        [Test]
        public void ParseRefusesABadLedger()
        {
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t1", "Unity/A.cs\t1.5f\t1\tdebt" }).Problems, Is.Empty);
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "Unity/A.cs\t1.5f\t1\tdebt" }).Problems,
                Is.EqualTo(new[] { "the debt-max line is missing" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t2", "Unity/A.cs\t1.5f\t1\tdebt" }).Problems,
                Is.EqualTo(new[] { "debt rows total 1 but debt-max says 2; set debt-max to 1 (it may only go down)" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t1\tcanon" }).Problems,
                Is.EqualTo(new[] { "line 2: a canon row needs a note saying why the value lives here" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t1\tmath" }).Problems,
                Is.EqualTo(new[] { "line 2: a math row needs a note saying why the value lives here" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t1\t?\tx" }).Problems,
                Is.EqualTo(new[] { "line 2: tag must be canon, ui, math or debt" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t0\tui\tx" }).Problems,
                Is.EqualTo(new[] { "line 2: count must be a whole number of at least 1" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Tests/A.cs\t1.5f\t1\tui\tx" }).Problems,
                Is.EqualTo(new[] { "line 2: Tests/A.cs is outside the scanned code" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t1\tui\tx", "Unity/A.cs\t1.5f\t1\tui\ty" }).Problems,
                Is.EqualTo(new[] { "line 3: Unity/A.cs 1.5f is listed twice" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\tx" }).Problems[0], Is.EqualTo("line 1: debt-max needs one whole number"));
            Assert.That(() => SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t1" }), Throws.Nothing);
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t1" }).Problems,
                Is.EqualTo(new[] { "line 2: needs file, token, count, tag and an optional note" }));
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "debt-max\t0", "Unity/A.cs\t1.5f\t1\tui\tnote\textra" }).Problems,
                Is.EqualTo(new[] { "line 2: needs file, token, count, tag and an optional note" }));
        }

        [Test]
        public void ParseSkipsCommentsAndBlankLines() =>
            Assert.That(SizeLiteralGuardTests.Parse(new[] { "# note", "", "debt-max\t0", "Unity/A.cs\t1.5f\t1\tui\tfeel" }).Rows.Count, Is.EqualTo(1));
    }
}
