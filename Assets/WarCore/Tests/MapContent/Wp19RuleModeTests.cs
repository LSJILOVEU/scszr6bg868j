using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-19 A0: the match mode is the single owner of the result, the end tick commits in full, and Sim refuses to advance after it.
    public sealed class Wp19RuleModeTests
    {
        // Two armed units far apart on a point nobody holds: the match ends by time on tick 6.
        private static WorldRecipe TimeEnd() => M6World.RecipeOf(new[] { M6World.U(1, 1, 10), M6World.U(2, 2, 70) },
            M6World.Pack(new[] { new ControlPointDef(1, M6World.P(40), SimFix.FromInt(1)) }, max: 6));
        private const long End = 6;
        private static InputFrame Busy() => new InputFrame(new SimVec3(SimFix.FromInt(1), default, default), default, primaryAttack: true);

        [Test]
        public void EndTickCommitsInFullAndTheNextStepThrows_ForEveryKindOfInput()
        {
            var a = new M6World(TimeEnd()); var twin = new M6World(M6World.Endless(TimeEnd()));
            for (int tick = 1; tick <= End; tick++) { a.Step(); twin.Step(); }
            Assert.That(a.Match.Winner, Is.Not.Zero); Assert.That(a.Match.EndTick, Is.EqualTo(End)); Assert.That(a.Sim.IsFinished, Is.True);
            Assert.That(twin.Sim.IsFinished, Is.False);
            // The end tick is complete: everything except the result itself equals a world whose match never ends.
            Assert.That(a.Combat.Fires.ToArray(), Is.EqualTo(twin.Combat.Fires.ToArray()));
            foreach (ulong key in new ulong[] { 1, 2 })
                Assert.That(a.Read<WarCore.Combat.Combatant>(key), Is.EqualTo(twin.Read<WarCore.Combat.Combatant>(key)));
            Assert.That(a.Sim.Events.NextSequence, Is.EqualTo(twin.Sim.Events.NextSequence));
            // Sim stores the offered input before it refuses the step (StableSimState.cs:75), so a refusal is not fully atomic:
            // the same input as the last accepted one changes nothing, a different one is remembered. A0 does not change Sim.
            ulong hash = a.Hash;
            Assert.Throws<InvalidOperationException>(() => a.Sim.Step(default));
            Assert.That(a.Sim.Clock.Tick, Is.EqualTo(End)); Assert.That(a.Hash, Is.EqualTo(hash), "the same input as before changes nothing");
            Assert.Throws<InvalidOperationException>(() => a.Sim.Step(Busy()));
            Assert.That(a.Sim.Clock.Tick, Is.EqualTo(End)); Assert.That(a.Match.Winner, Is.Not.Zero); Assert.That(a.Match.EndTick, Is.EqualTo(End));
            foreach (ulong key in new ulong[] { 1, 2 })
                Assert.That(a.Read<WarCore.Combat.Combatant>(key), Is.EqualTo(twin.Read<WarCore.Combat.Combatant>(key)));
            ulong remembered = a.Hash;
            Assert.That(remembered, Is.Not.EqualTo(hash), "a different input is remembered by the refusing Sim, and the hash shows it (non-atomic, documented)");
            Assert.Throws<InvalidOperationException>(() => a.Sim.Step(Busy()));
            Assert.That(a.Hash, Is.EqualTo(remembered), "refusing the same input again is stable");
        }

        [Test]
        public void SnapshotsBeforeAtAndAfterTheEnd_RestoreAndContinueIdentically()
        {
            var a = new M6World(TimeEnd());
            a.Step((int)End - 1); byte[] before = a.Sim.SaveRetainedSnapshot(); ulong hashBefore = a.Hash;
            a.Step(); byte[] atEnd = a.Sim.SaveRetainedSnapshot(); ulong hashEnd = a.Hash;

            var b = new M6World(TimeEnd(), reverse: true); b.Sim.RestoreRetainedSnapshot(before, reverseAllocation: true);
            Assert.That(b.Hash, Is.EqualTo(hashBefore)); Assert.That(b.Sim.IsFinished, Is.False, "LoadState does not evaluate");
            Assert.That(b.Match.Winner, Is.Zero);
            b.Step();
            Assert.That(b.Hash, Is.EqualTo(hashEnd)); Assert.That(b.Match.EndTick, Is.EqualTo(End)); Assert.That(b.Sim.IsFinished, Is.True);

            var c = new M6World(TimeEnd()); c.Sim.RestoreRetainedSnapshot(atEnd);
            Assert.That(c.Hash, Is.EqualTo(hashEnd)); Assert.That(c.Sim.IsFinished, Is.True);
            Assert.That(c.Match.Winner, Is.EqualTo(a.Match.Winner)); Assert.That(c.Match.EndTick, Is.EqualTo(End));
            Assert.That(c.Match.EndReason, Is.EqualTo(a.Match.EndReason));
            Assert.Throws<InvalidOperationException>(() => c.Step());
            // The bridge shows the restored frame at once: no stale view, no interpolation, no further advance.
            c.Session.RefreshView();
            Assert.That(c.Session.Tick, Is.EqualTo(End)); Assert.That(c.Session.IsFinished, Is.True);
            Assert.That(c.Session.Current.Tick, Is.EqualTo(End)); Assert.That(c.Session.Current.Match.IsFinished, Is.True);
            Assert.That(c.Session.Current.Match.Winner, Is.EqualTo(a.Match.Winner)); Assert.That(c.Session.Current.Match.EndTick, Is.EqualTo((int)End));
            Assert.That(c.Session.Previous.Tick, Is.EqualTo(End)); Assert.That(c.Session.Alpha, Is.Zero);
            Assert.That(c.Session.Advance(5_000_000), Is.Zero); Assert.That(c.Session.Tick, Is.EqualTo(End));
            c.Session.StepOnce(); Assert.That(c.Session.Tick, Is.EqualTo(End));
        }

        [TestCase("other.rules")]
        [TestCase(MatchMode.TestNeverEndsId)]
        public void SnapshotFromAnotherRuleSetIsRejected(string otherId)
        {
            var a = new M6World(TimeEnd()); a.Step(3); byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            var other = new M6World(TimeEnd().WithRuleSet(otherId));
            Assert.Catch(() => other.Sim.RestoreRetainedSnapshot(snapshot));
        }

        [Test]
        public void RuleSetIdentityIsAFixedOpaqueIdThatTheRecipeCarriesEverywhere()
        {
            Assert.That(MatchMode.RwrRuleSetId, Is.EqualTo("rwr.m6-demo"));
            Assert.That(DemoRecipes.M6().RuleSetId, Is.EqualTo("rwr.m6-demo"));
            Assert.That(TimeEnd().RuleSetId, Is.EqualTo("rwr.m6-demo"), "the id is not derived from the rules inside the pack");
            var w = new M6World(TimeEnd()); byte[] snapshot = w.Sim.SaveRetainedSnapshot();
            byte[] id = Encoding.UTF8.GetBytes("rwr.m6-demo");
            Assert.That(Contains(snapshot, id), Is.True, "the snapshot carries the id bytes");
            // Every With* keeps the rule configuration, in either order.
            var tagged = TimeEnd().WithRuleSet("x.y");
            Assert.That(tagged.WithCombat(tagged.Combat).RuleSetId, Is.EqualTo("x.y"));
            Assert.That(tagged.WithMatch(tagged.Match).RuleSetId, Is.EqualTo("x.y"));
            Assert.That(tagged.WithPlayer(1).RuleSetId, Is.EqualTo("x.y"));
            Assert.That(tagged.WithPlayer(1).WithMatch(tagged.Match).WithCombat(tagged.Combat).Match, Is.SameAs(tagged.Match));
            Assert.Throws<ArgumentException>(() => TimeEnd().WithRuleSet(""));
        }

        private static bool Contains(byte[] data, byte[] part)
        {
            for (int i = 0; i + part.Length <= data.Length; i++)
            {
                int j = 0; while (j < part.Length && data[i + j] == part[j]) j++;
                if (j == part.Length) return true;
            }
            return false;
        }

        // Judging order, pinned by the cases where two rules are true at once.
        [Test]
        public void AllPointsBeatsExhaustionWhenBothHoldOnTheSameTick()
        {
            var points = new[] { new ControlPointDef(1, M6World.P(30), SimFix.FromInt(1), 1), new ControlPointDef(2, M6World.P(50), SimFix.FromInt(1), 1) };
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1) }, M6World.Pack(points, max: 9)));
            w.Step();
            Assert.That(w.Match.Winner, Is.EqualTo(1)); Assert.That(w.Match.EndReason, Is.EqualTo(MatchEndReason.AllPoints));
        }

        [Test]
        public void TimeJudgesPointsBeforeHeadCount()
        {
            // Team 1 has more units, team 2 holds the only point: points decide.
            var points = new[] { new ControlPointDef(1, M6World.P(30), SimFix.FromInt(1), 2), new ControlPointDef(2, M6World.P(50), SimFix.FromInt(1)) };
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 1, 12), M6World.U(3, 2, 70) }, M6World.Pack(points, max: 2)));
            w.Step(2);
            Assert.That(w.Match.Winner, Is.EqualTo(2)); Assert.That(w.Match.EndReason, Is.EqualTo(MatchEndReason.Time));
        }

        [Test]
        public void TwoWorldsFromOneRecipeNeverShareAResult()
        {
            var recipe = TimeEnd(); var a = new M6World(recipe); var b = new M6World(recipe);
            a.Step((int)End);
            Assert.That(a.Match.Winner, Is.Not.Zero); Assert.That(b.Match.Winner, Is.Zero); Assert.That(b.Sim.IsFinished, Is.False);
            b.Step(); Assert.That(b.Match.EndTick, Is.Zero);
        }

        [Test]
        public void UnboundModeCannotAdvanceAndBindsOnlyOnce()
        {
            var mode = new MatchMode(M6World.Pack());
            Assert.Throws<InvalidOperationException>(() => mode.Start(null));
            Assert.Throws<InvalidOperationException>(() => mode.Evaluate(1));
            var w = new M6World(TimeEnd());
            mode.Bind(w.Match); Assert.Throws<InvalidOperationException>(() => mode.Bind(w.Match));
            Assert.Throws<ArgumentException>(() => new MatchMode(M6World.Pack(), ""));
        }

        [Test]
        public void TheModeIsTheOnlyOwner_MatchSystemKeepsNoResultOfItsOwn()
        {
            var w = new M6World(TimeEnd()); w.Step((int)End);
            var fields = typeof(MatchSystem).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.That(fields.Any(f => f.FieldType == typeof(int) || f.FieldType == typeof(long) || f.FieldType == typeof(MatchEndReason)), Is.False);
            Assert.That(w.Session.Winner, Is.EqualTo(w.Match.Winner)); Assert.That(w.Session.EndTick, Is.EqualTo(w.Match.EndTick));
            Assert.That(w.Session.IsFinished, Is.EqualTo(w.Sim.IsFinished));
        }

        // Replaces the 174 M7-real golden rows after the end: the baseline end tick is 747 and Sim refuses tick 748.
        [Test]
        public void RealMatchEndsOnTheBaselineTick_AndTheNextStepIsRefused()
        {
            var w = M7BehaviorLockTests.Real(41); w.Step((int)Wp13GoldenData.RealEndTick);
            Assert.That(w.Match.EndTick, Is.EqualTo(Wp13GoldenData.RealEndTick)); Assert.That(w.Match.Winner, Is.EqualTo(2));
            Assert.That(w.Sim.IsFinished, Is.True);
            Assert.Throws<InvalidOperationException>(() => w.Step());
        }

        [Test]
        public void NeverEndsRuleSetNeverJudges()
        {
            var w = new M6World(M6World.Endless(TimeEnd())); w.Step(30);
            Assert.That(w.Sim.IsFinished, Is.False); Assert.That(w.Match.Winner, Is.Zero);
        }
    }
}
