using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 9(b)(c): the suppression with the AI fire state (P1 reaction, P2 sensing interval). An intent replaces
    // the automatic decision of its tick; a tick without one falls back to the automatic fire.
    public sealed class A3bComboTests
    {
        private const int R = 15;

        // 9(b): R = 15, K = 1, D = 100: the reaction delays the first shots to 16 and 46, the suppression fires 76 and 136.
        [Test]
        public void WithReaction_Vector()
        {
            M6World w = A3b.World(A3b.Ai(300, 100, reaction: R));
            Assert.That(L4.Systems(w), Does.Contain("battle.ai-contacts|0|13;battle.ai-fire|0|14;battle.ai-suppress|0|18;"));
            Assert.That(A3b.Run(w, 400, A3b.Script), Is.EqualTo(A3b.Ticks(16, 46, 76, 136)));
            A3b.ReverseEqual(A3b.Ai(300, 100, reaction: R), 400, A3b.Script);
        }

        // 9(b): B back on 150: the reaction entry returns on 181 (seen by the fire state), the suppression entry goes on
        // the thinking tick 186, and the reaction clock runs again from its first sighting (196 = 181 + R).
        [Test]
        public void WithReaction_SeenAgain()
        {
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 100, reaction: R)), 400, t => A3b.Back(t), null, (x, t) =>
            {
                if (t == 180) Assert.That(A3b.FireCount(x), Is.EqualTo(0));
                if (t >= 181 && t <= 200) Assert.That(A3b.FireCount(x), Is.EqualTo(1), "reaction count, tick " + t);
                if (t == 185) Assert.That(A3b.Count(x), Is.EqualTo(1));
                if (t >= 186) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(16, 46, 76, 136, 196, 226, 286, 316, 346)));
        }

        // 9(c): K = 3 (A samples on ticks = 0 mod 3), R = 15. Without the suppression: {18, 48}; with D = 100 the
        // suppression adds 78 and 138 and the entry lives from 56 to 345.
        [Test]
        public void WithSensing_Plain()
        {
            M6World off = A3b.World(A3b.Ai(300, null, reaction: R, sense: 3));
            Assert.That(off.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            Assert.That(A3b.Run(off, 400, A3b.Script), Is.EqualTo(A3b.Ticks(18, 48)));
            M6World on = A3b.World(A3b.Ai(300, 100, reaction: R, sense: 3));
            List<long> fires = A3b.Run(on, 400, A3b.Script, null, (x, t) =>
            {
                if (t == 55) Assert.That(A3b.Count(x), Is.EqualTo(0));
                if (t >= 56 && t <= 345) Assert.That(A3b.Count(x), Is.EqualTo(1), "count, tick " + t);
                if (t >= 346) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(18, 48, 78, 138)));
            A3b.ReverseEqual(A3b.Ai(300, null, reaction: R, sense: 3), 400, A3b.Script);
            A3b.ReverseEqual(A3b.Ai(300, 100, reaction: R, sense: 3), 400, A3b.Script);
        }

        private static void Block(M6World w, long t)
        {
            if (t == 56) A3b.Place(w, A3b.KeyF, A3b.Mid);
            if (t == 100) A3b.Place(w, A3b.KeyF, A3b.FHome);
        }

        // 9(c): F on the line from the first suppression tick until 100: the declined intents keep the cooldown at 78000
        // and the first suppressive shot is 100.
        [Test]
        public void WithSensing_BlockedFromTheFirstTick()
        {
            string ai = A3b.Ai(300, 100, reaction: R, sense: 3);
            List<long> fires = A3b.Run(A3b.World(ai, null, false, null, A3b.Friend()), 400, A3b.FScript, Block, (x, t) =>
            {
                if (t >= 56 && t <= 99) Assert.That(A3b.Due(x), Is.EqualTo(78000L), "due, tick " + t);
                if (t == 100) Assert.That(A3b.Due(x), Is.EqualTo(160000L));
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(18, 48, 100)));
            A3b.ReverseEqual(ai, 400, A3b.FScript, Block, null, A3b.Friend());
        }

        // 9(c): B back on 150: the fire state sees B on its sample 183 (the entry goes on 186), and the automatic shot
        // comes at 198 = 183 + R; the selector finds B from 185.
        [Test]
        public void WithSensing_SeenAgain()
        {
            string ai = A3b.Ai(300, 100, reaction: R, sense: 3);
            List<long> fires = A3b.Run(A3b.World(ai), 400, t => A3b.Back(t), null, (x, t) =>
            {
                if (t == 182) Assert.That(A3b.FireCount(x), Is.EqualTo(0));
                if (t >= 183 && t <= 200) Assert.That(A3b.FireCount(x), Is.EqualTo(1), "reaction count, tick " + t);
                if (t == 185) { Assert.That(A3b.Count(x), Is.EqualTo(1)); Assert.That(A3b.Selects(x), Is.True); }
                if (t >= 186) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(18, 48, 78, 138, 198, 228, 288, 318, 348)));
            A3b.ReverseEqual(ai, 400, t => A3b.Back(t));
        }

        // 9(c): blocked [56, 100) and B back on 150 with D = 200: 100 is the first suppressive shot, 160 a suppressive
        // shot that hits B walking back, 201 on the automatic fire.
        [Test]
        public void WithSensing_BlockedAndSeenAgain()
        {
            string ai = A3b.Ai(300, 200, reaction: R, sense: 3);
            M6World w = A3b.World(ai, null, false, null, A3b.Friend());
            FireRecord at160 = default;
            List<long> fires = A3b.Run(w, 400, t => t == 150 ? A3b.Back(t) : A3b.FScript(t), Block, (x, t) =>
            {
                if (t == 160) Assert.That(A3b.Fired(x, out at160), Is.True);
                if (t >= 186) Assert.That(A3b.Count(x), Is.EqualTo(0), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(18, 48, 100, 160, 201, 231, 291, 321, 351)));
            Assert.That(at160.Target, Is.EqualTo(A3.Id(w, A3b.KeyB)));
            Assert.That(at160.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(A3b.Same(at160.End, A3b.Raw(1394080, 0, 1541264)), A3b.V(at160.End));
            A3b.ReverseEqual(ai, 400, t => t == 150 ? A3b.Back(t) : A3b.FScript(t), Block, null, A3b.Friend());
        }

        // 9(c) fifth: K = 4 (A samples on ticks = 2 mod 4), B back on 150 and teleported deep into the grass before 227.
        // 227 is no sample tick and the AI has not thought yet (236): with no intent the automatic fire keeps its target
        // and shoots at B where it stands now; the suppression entry comes on 236 and fires 287 and 317 towards P.
        // The contrast without the teleport shoots B at its start on 227 (77 and 137 are the suppressive shots of both runs).
        [Test]
        public void WithSensing_NoIntentFallsBackToTheKeptTarget()
        {
            string ai = A3b.Ai(300, 100, reaction: R, sense: 4);
            M6World w = A3b.World(ai);
            var records = new Dictionary<long, FireRecord>();
            System.Action<M6World, long> teleport = (x, t) => { if (t == 227) A3b.Place(x, A3b.KeyB, A2.Cell(21, 26)); };
            List<long> fires = A3b.Run(w, 330, t => A3b.Back(t), teleport, (x, t) =>
            {
                if (A3b.Fired(x, out FireRecord f)) records[t] = f;
                if (t == 227) { Assert.That(A3b.Selects(x), Is.False); Assert.That(A3b.FireCount(x), Is.EqualTo(1)); }
                if (t == 230) Assert.That(A3b.FireCount(x), Is.EqualTo(0));
                if (t == 235) Assert.That(A3b.Count(x), Is.EqualTo(0));
                if (t == 236) Assert.That(A3b.Count(x), Is.EqualTo(1));
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(17, 47, 77, 137, 197, 227, 287, 317)));
            Assert.That(records[227].Target, Is.EqualTo(A3.Id(w, A3b.KeyB)));
            Assert.That(records[227].Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(A3b.Same(records[227].End, A3b.Raw(1410047, 0, 1720346)), A3b.V(records[227].End));
            foreach (long t in new long[] { 287, 317 })
            {
                Assert.That(records[t].Outcome, Is.EqualTo(FireOutcome.Open), "tick " + t);
                Assert.That(A3b.Same(records[t].End, A3b.Raw(1230720, 0, 2638998)), A3b.V(records[t].End));
            }
            A3b.ReverseEqual(ai, 330, t => A3b.Back(t), teleport);
            M6World control = A3b.World(ai);
            var controlRecords = new Dictionary<long, FireRecord>();
            List<long> kept = A3b.Run(control, 330, t => A3b.Back(t), null, (x, t) => { if (A3b.Fired(x, out FireRecord f)) controlRecords[t] = f; });
            Assert.That(kept, Is.EqualTo(A3b.Ticks(17, 47, 77, 137, 197, 227, 287, 317)));
            foreach (long t in new long[] { 17, 47, 197, 227, 287, 317 }) Assert.That(controlRecords[t].Target, Is.EqualTo(A3.Id(control, A3b.KeyB)), "target, tick " + t);
            foreach (long t in new long[] { 77, 137 }) Assert.That(controlRecords[t].Outcome, Is.EqualTo(FireOutcome.Open), "outcome, tick " + t);
            Assert.That(A3b.Same(controlRecords[227].End, A3b.Raw(1411062, 0, 1196151)), A3b.V(controlRecords[227].End));
        }
    }
}
