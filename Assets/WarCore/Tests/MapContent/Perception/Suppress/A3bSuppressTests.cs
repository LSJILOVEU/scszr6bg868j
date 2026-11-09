using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 2 and 3: the suppression vector on the main fixture, and when it stops.
    public sealed class A3bSuppressTests
    {
        private const string Systems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "battle.ai-contacts|0|13;battle.ai-suppress|0|18;interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        private static readonly SimVec3 Origin = A3b.Raw(1474560, 0, 688128), OpenEnd = A3b.Raw(1289910, 0, 2645508), FirstEnd = A3b.Raw(1401240, 0, 1465358);

        // 2: M = 300, D = 100. The entry (46, 56) appears at the end of tick 56; A fires on 1 and 31 at B, on 61 the first
        // suppressive shot (B is still walking along the line and is hit), on 121 and 151 into the open at 30 m towards P;
        // the selector finds nobody on the suppression ticks; A never moves; [156, 345] has no shot although the rifle is
        // ready; the entry stays until the memory expires at the end of 346.
        [Test]
        public void Vector_FiveShotsThenSilence()
        {
            M6World w = A3b.World(A3b.Ai(300, 100));
            Assert.That(L4.Systems(w), Is.EqualTo(Systems));
            Assert.That(A3.FirstThink(w, A3b.KeyA), Is.EqualTo(6));
            var records = new Dictionary<long, FireRecord>();
            var selects = new Dictionary<long, bool>();
            List<long> fires = A3b.Run(w, 400, A3b.Script, null, (x, t) =>
            {
                Assert.That(A3b.Same(A3b.At(x, A3b.KeyA), Origin), "A moved, tick " + t);
                if (A3b.Fired(x, out FireRecord f)) { records[t] = f; selects[t] = A3b.Selects(x); }
                if (t == 55) Assert.That(A3b.Count(x), Is.EqualTo(0));
                if (t == 56)
                {
                    Assert.That(A3b.Count(x), Is.EqualTo(1));
                    Assert.That(A3b.Entry(x, A3b.KeyA, out long seen, out long since), Is.True);
                    Assert.That(seen, Is.EqualTo(A3b.Seen)); Assert.That(since, Is.EqualTo(A3b.Since));
                    A3.Recall(x, A3b.KeyA, t, out _, out SimVec3 p, out _);
                    Assert.That(A3b.Same(p, A3b.P), A3b.V(p));
                }
                if (t >= 56 && t <= 345) Assert.That(A3b.Count(x), Is.EqualTo(1), "count, tick " + t);
                if (t >= 346) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 151)));
            StableId b = A3.Id(w, A3b.KeyB);
            foreach (long t in new long[] { 1, 31 })
            { Assert.That(records[t].Target, Is.EqualTo(b)); Assert.That(records[t].Outcome, Is.EqualTo(FireOutcome.Hit)); }
            Assert.That(records[61].Target, Is.EqualTo(b)); Assert.That(records[61].Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(A3b.Same(records[61].End, FirstEnd), A3b.V(records[61].End));
            foreach (long t in new long[] { 121, 151 })
            {
                Assert.That(records[t].Target, Is.EqualTo(default(StableId)));
                Assert.That(records[t].Outcome, Is.EqualTo(FireOutcome.Open));
                Assert.That(A3b.Same(records[t].End, OpenEnd), A3b.V(records[t].End));
            }
            foreach (long t in new long[] { 61, 121, 151 })
            {
                Assert.That(A3b.Same(records[t].Origin, Origin));
                Assert.That(selects[t], Is.False, "selector, tick " + t);
                A3b.AssertTowards(records[t], A3b.P);
            }
        }

        // 3(a): the window is Since .. Since + D - 1: D = 125 ends before 181, D = 126 fires on 181 and not on 241.
        [TestCase(125, false)]
        [TestCase(126, true)]
        public void Duration_LastSubmissionIsSincePlusDMinusOne(int d, bool fires181)
        {
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, d)), 400, A3b.Script);
            Assert.That(fires.Contains(181), Is.EqualTo(fires181));
            Assert.That(fires.Contains(241), Is.False);
            Assert.That(fires.GetRange(0, 5), Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 151)));
            Assert.That(fires.Count, Is.EqualTo(fires181 ? 6 : 5));
        }

        // 3(b): the memory (M = 60) goes before the suppression (D = 300): the entry goes with it at the end of 106.
        [Test]
        public void MemoryExpiresFirst_EntryGoesWithIt()
        {
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(60, 300)), 400, A3b.Script, null, (x, t) =>
            {
                if (t == 105) Assert.That(A3b.Count(x), Is.EqualTo(1));
                if (t >= 106) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61)));
        }

        // 3(c): B walks back on 150 and is seen on the thinking tick 186: the entry goes that tick, the automatic fire
        // takes over (241 on: the selector finds B). 181 was still a suppressive shot towards P that hit B on its way back:
        // B was concealed when the tick began (the selector found nobody) and stepped out of the grass during the tick.
        [Test]
        public void SeenAgain_EntryGoesOnTheThinkingTick()
        {
            M6World w = A3b.World(A3b.Ai(300, 200));
            var records = new Dictionary<long, FireRecord>(); var selects = new Dictionary<long, bool>();
            bool selectsBefore = true;
            List<long> fires = A3b.Run(w, 400, t => A3b.Back(t), (x, t) => { if (t == 181) selectsBefore = A3b.Selects(x); }, (x, t) =>
            {
                if (A3b.Fired(x, out FireRecord f)) { records[t] = f; selects[t] = A3b.Selects(x); }
                if (t == 185) Assert.That(A3b.Count(x), Is.EqualTo(1));
                if (t == 186) { Assert.That(A3b.Count(x), Is.EqualTo(0)); Assert.That(A3.Count(x), Is.EqualTo(1)); }
                if (t > 186) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 151, 181, 241, 271, 301, 361, 391)));
            StableId b = A3.Id(w, A3b.KeyB);
            Assert.That(selectsBefore, Is.False);
            Assert.That(selects[181], Is.True);
            Assert.That(records[181].Target, Is.EqualTo(b)); Assert.That(records[181].Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(A3b.Same(records[181].End, A3b.Raw(1405338, 0, 1421921)), A3b.V(records[181].End));
            A3b.AssertTowards(records[181], A3b.P);
            Assert.That(selects[241], Is.True);
            Assert.That(records[241].Target, Is.EqualTo(b));
            Assert.That(A3b.Same(records[241].End, A3b.Raw(1411062, 0, 1196151)), A3b.V(records[241].End));
        }

        // 3(d)(e): the target or the observer dies on 100: the memory forgets, the entry goes on 101, nothing fires after.
        [TestCase(A3b.KeyB)]
        [TestCase(A3b.KeyA)]
        public void Death_EntryGoesTheNextTick(ulong victim)
        {
            M6World w = A3b.World(A3b.Ai(300, 200));
            Wp16TestWorld.KillAt(w, 100, victim);
            List<long> fires = null;
            Assert.DoesNotThrow(() => fires = A3b.Run(w, 200, A3b.Script, null, (x, t) =>
            {
                if (t == 100) Assert.That(A3b.Count(x), Is.EqualTo(1));
                if (t >= 101) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            }));
            Assert.That(w.Dead(victim), Is.True);
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61)));
        }

        // 3(f): A leaves team 1 for team 3 between 100 and 101: the AI forgets the contact and the entry goes the same tick.
        [Test]
        public void TeamChange_EntryGoesTheSameTick()
        {
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 300)), 200, A3b.Script,
                (x, t) => { if (t == 101) A3b.SetTeam(x, A3b.KeyA, 3); },
                (x, t) =>
                {
                    if (t == 100) { Assert.That(A3b.Count(x), Is.EqualTo(1)); Assert.That(A3.Count(x), Is.EqualTo(1)); }
                    if (t >= 101) { Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t); Assert.That(A3.Count(x), Is.EqualTo(0)); }
                });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61)));
        }

        // 3(g): a squad hold on 100 keeps A under command for good: no submission while commanded, the entry stays.
        [Test]
        public void Commanded_Hold_NoSubmissionEntryStays()
        {
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 200)), 300,
                t => t == 100 ? A3.Command(SquadCommandKind.Hold, 1) : A3b.Script(t), null, (x, t) =>
                {
                    if (t >= 100) { Assert.That(A3.Commanded(x, A3b.KeyA), Is.True, "tick " + t); Assert.That(A3b.Count(x), Is.EqualTo(1), "tick " + t); }
                });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61)));
        }

        // 3(g) contrast: a move order to Cell(22,12) ends on arrival; the suppression resumes from there (121 on).
        [Test]
        public void Commanded_MoveEnds_SuppressionResumesFromThere()
        {
            SimVec3 there = A2.Cell(22, 12);
            var records = new Dictionary<long, FireRecord>();
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 200)), 300,
                t => t == 100 ? A3.Command(SquadCommandKind.MoveTo, 1, there) : A3b.Script(t), null, (x, t) =>
                {
                    if (A3b.Fired(x, out FireRecord f)) records[t] = f;
                    if (t == 121) Assert.That(A3.Commanded(x, A3b.KeyA), Is.False);
                });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 151, 181, 241)));
            foreach (long t in new long[] { 121, 151, 181, 241 }) Assert.That(A3b.Same(records[t].Origin, there), "origin, tick " + t);
            Assert.That(A3b.Same(records[121].End, A3b.Raw(1247490, 0, 2772110)), A3b.V(records[121].End));
        }
    }
}
