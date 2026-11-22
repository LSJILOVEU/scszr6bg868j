using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests.Scale
{
    // HC-6: the giant and tiny probe. Each probe measures one thing that should follow the body (docs/world-scale.md, section
    // "Body probe") for the reference body, a 3 m giant and a 0.5 m tiny unit; what does not follow is a known gap listed in
    // tools/size-guard/body-gaps.tsv. A change that makes a probe follow must delete its row; a change that breaks one fails.
    public sealed class BodyProbeTests
    {
        // Every gap of the giant and the tiny unit now, in probe order.
        private static List<BodyGapLedger.Found> FoundNow()
        {
            var found = new List<BodyGapLedger.Found>();
            foreach (ProbeBody body in ProbeBodies.Scaled)
                foreach (Probe probe in BodyProbes.All)
                {
                    ProbeResult r = probe.Run(body);
                    if (r.IsGap) found.Add(new BodyGapLedger.Found(probe.Id, body.Name, r.Note));
                }
            return found;
        }

        // S1: both bodies are the shipped rwr-stance body scaled, rounded down.
        [Test]
        public void ScaledBodies_AreTheShippedBodyScaledDown()
        {
            ProbeBody g = ProbeBodies.Giant, t = ProbeBodies.Tiny;
            Assert.That(g.Body.Height.Raw, Is.EqualTo(196608), "giant stand");
            Assert.That(g.Stances[0].Body.Height.Raw, Is.EqualTo(112347), "giant crouch");
            Assert.That(g.Stances[1].Body.Height.Raw, Is.EqualTo(56173), "giant prone");
            Assert.That(g.Body.Eye.Raw, Is.EqualTo(168521), "giant stand eye");
            Assert.That(g.Body.Muzzle.Raw, Is.EqualTo(168521), "giant stand muzzle");
            Assert.That(t.Body.Height.Raw, Is.EqualTo(32768), "tiny stand");
            Assert.That(t.Stances[0].Body.Height.Raw, Is.EqualTo(18724), "tiny crouch");
            Assert.That(t.Stances[1].Body.Height.Raw, Is.EqualTo(9362), "tiny prone");
            Assert.That(t.Body.Eye.Raw, Is.EqualTo(28086), "tiny stand eye");
            Assert.That(t.Stances[1].Body.Eye.Raw, Is.EqualTo(4681), "tiny prone eye");
            Assert.That(g.RadiusWanted, Is.EqualTo(28086), "giant radius wanted (0.25 m x 12/7)");
            Assert.That(g.Body.Radius.Raw, Is.EqualTo(BodyRadius.MaxRaw), "giant radius as carried: the largest a body may declare");
            Assert.That(t.RadiusWanted, Is.EqualTo(4681), "tiny radius wanted");
            Assert.That(t.Body.Radius.Raw, Is.EqualTo(4681), "tiny radius as carried");
            Assert.That(t.Stances[1].Body.Radius.Raw, Is.EqualTo(4681), "tiny prone radius");
            ProbeBody r = ProbeBodies.Reference;
            Assert.That(r.Body.Height.Raw, Is.EqualTo(ProbeBodies.Shipped.Body.Value.Height.Raw), "reference stand");
            Assert.That(r.Stances[1].Body.Eye.Raw, Is.EqualTo(ProbeBodies.Shipped.Stances[1].Body.Eye.Raw), "reference prone eye");
            // The tiny prone height is exactly twice the tiny radius (the reference radius 0.25 m scaled): the rounding edge a
            // radius that follows the body meets.
            Assert.That(t.Stances[1].Body.Height.Raw, Is.EqualTo(2 * t.Body.Radius.Raw), "tiny prone = 2 x tiny radius");
        }

        // The reader path builds the same body as the direct path.
        [Test]
        public void ReaderLayer_GivesTheScaledBodyBack()
        {
            CombatPack pack = ProbeBodies.LoadLayer(ProbeBodies.Reference);
            Assert.That(pack.Body.Value.Height.Raw, Is.EqualTo(ProbeBodies.Reference.Body.Height.Raw));
            Assert.That(pack.Stances[0].Body.Eye.Raw, Is.EqualTo(ProbeBodies.Reference.Stances[0].Body.Eye.Raw));
            Assert.That(pack.Stances[1].Deny, Is.EqualTo(ProbeBodies.Reference.Stances[1].Deny));
            Assert.That(ProbeBodies.LoadLayer(ProbeBodies.Tiny).Body.Value.Radius.Raw, Is.EqualTo(4681), "tiny radius read back");
            Assert.That(ProbeBodies.Metres(112347), Is.EqualTo("1.7142791749"));
        }

        // S4: the reference body passes every probe and none throws.
        [Test]
        public void ReferenceBodyPassesEverything()
        {
            var failed = new List<string>();
            foreach (Probe probe in BodyProbes.All)
            {
                ProbeResult r = probe.Run(ProbeBodies.Reference);
                if (r.Verdict != Verdict.Pass) failed.Add(probe.Id + " " + r.Verdict + " " + r.Note + " (want " + r.Expected + ", got " + r.Actual + ")");
            }
            Assert.That(failed, Is.Empty, string.Join("\n", failed));
        }

        // S5: the drawn sizes follow the body, so the probe passes for both scaled bodies (it can see a body that follows).
        [Test]
        public void DrawnSizes_FollowBothBodies()
        {
            Probe probe = BodyProbes.Find("B09");
            foreach (ProbeBody body in ProbeBodies.Scaled)
            {
                ProbeResult r = probe.Run(body);
                Assert.That(r.Verdict, Is.EqualTo(Verdict.Pass), body.Name + ": " + r.Note + " (want " + r.Expected + ", got " + r.Actual + ")");
            }
        }

        // A probe that crashes is a Threw result with the exception's type, not an escaped exception.
        [Test]
        public void CrashedProbe_IsRecordedAsThrew()
        {
            var probe = new Probe("X1", "-", "crash", "test", "-", new[] { "" }, BodyProbes.RawTolerance,
                b => { throw new InvalidOperationException("boom"); });
            ProbeResult r = null;
            Exception escaped = null;
            try { r = probe.Run(ProbeBodies.Giant); }
            catch (Exception e) { escaped = e; }
            Assert.That(escaped, Is.Null, "the exception escaped the probe");
            Assert.That(r.Verdict, Is.EqualTo(Verdict.Threw));
            Assert.That(r.Note, Is.EqualTo("threw InvalidOperationException"));
            Assert.That(r.IsGap, Is.True);
        }

        // A ratio probe that measures the same raw length for every body follows only for the reference.
        [Test]
        public void FixedLength_IsAGapForScaledBodies()
        {
            var probe = new Probe("X2", "-", "fixed", "test", "-", new[] { "" }, BodyProbes.RawTolerance, b => new[] { 16384L });
            Assert.That(probe.Run(ProbeBodies.Reference).Verdict, Is.EqualTo(Verdict.Pass));
            Assert.That(probe.Run(ProbeBodies.Giant).Verdict, Is.EqualTo(Verdict.Gap));
            Assert.That(probe.Run(ProbeBodies.Giant).Expected, Is.EqualTo(BodyProbes.Metres(16384L * 12 / 7)));
            Assert.That(probe.Run(ProbeBodies.Tiny).Verdict, Is.EqualTo(Verdict.Gap));
        }

        // S3: the gaps found now are exactly the ledger's rows.
        [Test]
        public void GapsMatchTheLedger()
        {
            BodyGapLedger.Ledger ledger = BodyGapLedger.Parse(File.ReadAllLines(BodyGapLedger.Path()));
            List<string> problems = BodyGapLedger.Compare(FoundNow(), ledger);
            Assert.That(problems, Is.Empty, "\n" + string.Join("\n", problems));
        }

        // Maintenance aid, never run by CI: prints the full probe table (markdown) and the ledger rows for the tree as it is now.
        // With WARCORE_PROBE_REPORT set, also writes the table to that file.
        [Test, Explicit, Category("Manual")]
        public void PrintReport()
        {
            var md = new StringBuilder();
            md.AppendLine("| id | family | what | method | reference | giant want | giant got | giant | tiny want | tiny got | tiny | where |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            var rows = new StringBuilder();
            int count = 0;
            foreach (Probe p in BodyProbes.All)
            {
                ProbeResult r = p.Run(ProbeBodies.Reference), g = p.Run(ProbeBodies.Giant), t = p.Run(ProbeBodies.Tiny);
                md.AppendLine("| " + string.Join(" | ", p.Id, p.Family, p.What, p.Method, r.Verdict == Verdict.Pass ? (r.Actual) : r.Verdict + " " + r.Note,
                    g.Expected, g.Actual, Label(g), t.Expected, t.Actual, Label(t), p.Where) + " |");
                foreach (var pair in new[] { (ProbeBodies.GiantName, g), (ProbeBodies.TinyName, t) })
                    if (pair.Item2.IsGap) { rows.AppendLine(p.Id + "\t" + pair.Item1 + "\t" + p.Family + "\t" + pair.Item2.Note); count++; }
            }
            string text = md + "\nledger rows (" + count + "):\ngaps-max\t" + count + "\n" + rows;
            TestContext.Out.WriteLine(text);
            string path = Environment.GetEnvironmentVariable("WARCORE_PROBE_REPORT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, text);
        }

        private static string Label(ProbeResult r) =>
            r.Verdict == Verdict.Pass ? "pass" : r.Verdict == Verdict.NotApplicable ? "n/a (" + r.Note + ")" : r.Verdict == Verdict.Threw ? "**crash** " + r.Note : "**gap** " + r.Note;
    }
}
