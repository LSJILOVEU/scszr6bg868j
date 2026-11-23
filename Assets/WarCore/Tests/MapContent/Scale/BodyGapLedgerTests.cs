using System.Collections.Generic;
using NUnit.Framework;

namespace WarCore.Tests.Scale
{
    // HC-6 C-c: the ledger comparison and the probe tolerance on made-up rows; no probe runs here.
    public sealed class BodyGapLedgerTests
    {
        private static BodyGapLedger.Found F(string id, string body) => new BodyGapLedger.Found(id, body, "made up");

        private static List<string> Run(string[] lines, params BodyGapLedger.Found[] found) =>
            BodyGapLedger.Compare(found, BodyGapLedger.Parse(lines));

        [Test]
        public void Agreeing_ReportsNothing()
        {
            List<string> problems = Run(new[] { "# c", "gaps-max\t2", "B03\tgiant\tH1\tx", "B03\ttiny\tH1\ty" }, F("B03", "giant"), F("B03", "tiny"));
            Assert.That(problems, Is.Empty);
        }

        // (1) a ledger row the probes no longer find
        [Test]
        public void LedgerHasAnExtraRow_IsFixed()
        {
            List<string> problems = Run(new[] { "gaps-max\t2", "B03\tgiant\tH1\tx", "B04\tgiant\tH1\ty" }, F("B03", "giant"));
            Assert.That(problems, Is.EqualTo(new[] { "FIXED B04 giant: delete the row and lower gaps-max (ledger line 3)" }));
        }

        // (2) a gap the ledger does not list
        [Test]
        public void ProbesFindAnExtraGap_IsNew()
        {
            List<string> problems = Run(new[] { "gaps-max\t1", "B03\tgiant\tH1\tx" }, F("B03", "giant"), F("B07", "giant"));
            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.StartWith("NEW B07 giant: made up"));
        }

        // (3) gaps-max must equal the number of rows
        [Test]
        public void GapsMaxDiffersFromTheRows_IsReported()
        {
            List<string> problems = Run(new[] { "gaps-max\t3", "B03\tgiant\tH1\tx" }, F("B03", "giant"));
            Assert.That(problems, Is.EqualTo(new[] { "gaps-max 3 but 1 rows" }));
        }

        // (4) the key is (id, body): a giant row does not stand for the tiny gap
        [Test]
        public void SameIdOtherBody_IsOneFixedAndOneNew()
        {
            List<string> problems = Run(new[] { "gaps-max\t1", "B03\tgiant\tH1\tx" }, F("B03", "tiny"));
            Assert.That(problems.Count, Is.EqualTo(2));
            Assert.That(problems[0], Does.StartWith("NEW B03 tiny"));
            Assert.That(problems[1], Does.StartWith("FIXED B03 giant"));
        }

        // (5) a ledger saved with CRLF line ends reads the same
        [Test]
        public void CrlfLines_Parse()
        {
            BodyGapLedger.Ledger ledger = BodyGapLedger.Parse(new[] { "# c\r", "gaps-max\t1\r", "B06\ttiny\t-\tthrew ArgumentOutOfRangeException\r" });
            Assert.That(ledger.Problems, Is.Empty);
            Assert.That(ledger.GapsMax, Is.EqualTo(1));
            Assert.That(ledger.Rows[0].Note, Is.EqualTo("threw ArgumentOutOfRangeException"));
            Assert.That(BodyGapLedger.Compare(new[] { F("B06", "tiny") }, ledger), Is.Empty);
        }

        [Test]
        public void MalformedRows_AreReported()
        {
            BodyGapLedger.Ledger ledger = BodyGapLedger.Parse(new[] { "B03\tgiant\tH1", "B03\thuge\tH1\tx", "B04\ttiny\tH1\tx", "B04\ttiny\tH1\ty" });
            Assert.That(ledger.Problems, Is.EqualTo(new[]
            {
                "line 1: needs id, body, family and note", "line 2: body must be giant or tiny", "line 4: B04 tiny is listed twice",
                "the gaps-max line is missing"
            }));
        }

        // (6) the judgement's tolerance: one raw unit off passes and two do not; on the float path two pass and three do not.
        [Test]
        public void Tolerance_OneRawUnitOrTwoOnTheFloatPath()
        {
            Assert.That(BodyProbes.Within(100, 100, BodyProbes.RawTolerance), Is.True);
            Assert.That(BodyProbes.Within(101, 100, BodyProbes.RawTolerance), Is.True);
            Assert.That(BodyProbes.Within(99, 100, BodyProbes.RawTolerance), Is.True);
            Assert.That(BodyProbes.Within(102, 100, BodyProbes.RawTolerance), Is.False);
            Assert.That(BodyProbes.Within(98, 100, BodyProbes.RawTolerance), Is.False);
            Assert.That(BodyProbes.Within(102, 100, BodyProbes.FloatTolerance), Is.True);
            Assert.That(BodyProbes.Within(103, 100, BodyProbes.FloatTolerance), Is.False);
            Assert.That(BodyProbes.Within(97, 100, BodyProbes.FloatTolerance), Is.False);
        }
    }
}
