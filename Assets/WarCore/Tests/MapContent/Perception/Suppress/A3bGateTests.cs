using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 4 and 5: the per-tick gates (line of sight, range, a teammate on the line) and the read ports.
    public sealed class A3bGateTests
    {
        private static readonly SimVec3 Behind = A2.Cell(12, 16), Far = A2.Cell(38, 2), Near = A2.Cell(30, 10);

        // 4(a): A behind the wall (x 15, z 13..17) during [100, 159] has no line to P and submits nothing; back on the
        // point it fires again on 160 and 190. The entry stays throughout.
        [Test]
        public void LineOfSight_BlockedByTheWall()
        {
            var records = new Dictionary<long, FireRecord>();
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 300)), 200, A3b.Script, (x, t) =>
            {
                if (t == 100) { A3b.Place(x, A3b.KeyA, Behind); Assert.That(x.Combat.Visible(A3b.A(x), A3b.P), Is.False); }
                if (t == 160) { A3b.Place(x, A3b.KeyA, A3.AAt); Assert.That(x.Combat.Visible(A3b.A(x), A3b.P), Is.True); }
            }, (x, t) =>
            {
                if (A3b.Fired(x, out FireRecord f)) records[t] = f;
                if (t >= 56) Assert.That(A3b.Count(x), Is.EqualTo(1), "count, tick " + t);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 160, 190)));
            Assert.That(records[160].Outcome, Is.EqualTo(FireOutcome.Open));
            Assert.That(A3b.Same(records[160].Origin, A3.AAt));
        }

        // 4(b): with the 15 m rifle, P at 25.1 m (Cell(38,2)) is out of range, at 13.9 m (Cell(30,10)) in range.
        [Test]
        public void Range_UsesTheWeaponsFalloffEnd()
        {
            var records = new Dictionary<long, FireRecord>();
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 300), A3b.Combat15()), 345, A3b.Script, (x, t) =>
            {
                if (t == 100) A3b.Place(x, A3b.KeyA, Far);
                if (t == 160) A3b.Place(x, A3b.KeyA, Near);
                if (t == 220) A3b.Place(x, A3b.KeyA, A3.AAt);
            }, (x, t) => { if (A3b.Fired(x, out FireRecord f)) records[t] = f; });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 160, 190, 220, 280, 310, 340)));
            foreach (long t in new long[] { 160, 190 })
            {
                Assert.That(A3b.Same(records[t].Origin, Near), "origin, tick " + t);
                Assert.That(A3b.Same(records[t].End, A3b.Raw(1362593, 0, 1437483)), A3b.V(records[t].End));
            }
            foreach (long t in new long[] { 220, 280, 310, 340 })
                Assert.That(A3b.Same(records[t].End, A3b.Raw(1382235, 0, 1666818)), A3b.V(records[t].End));
        }

        // 4(d): F on the midpoint of A -> P during [100, 159]: the intent is declined when consumed (no shot, no ammo, no
        // cooldown change); the entry stays; the shots resume on 160. The contrast with F never moved equals the D = 300
        // vector without F.
        [Test]
        public void AllyOnTheLine_DeclinedWhenConsumed()
        {
            var fired = new HashSet<long>();
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 300), null, false, null, A3b.Friend()), 400, A3b.FScript, (x, t) =>
            {
                if (t == 100)
                {
                    A3b.Place(x, A3b.KeyF, A3b.Mid);
                    Assert.That(x.Combat.AllyBlocks(A3b.A(x), A3b.P, A3b.R30), Is.True);
                    Assert.That(x.Combat.Visible(A3b.A(x), A3b.P), Is.True);
                }
                if (t == 160) { A3b.Place(x, A3b.KeyF, A3b.FHome); Assert.That(x.Combat.AllyBlocks(A3b.A(x), A3b.P, A3b.R30), Is.False); }
            }, (x, t) =>
            {
                if (t >= 56 && t <= 345) Assert.That(A3b.Count(x), Is.EqualTo(1), "count, tick " + t);
                if (t >= 100 && t <= 159)
                {
                    Assert.That(A3b.Same(A3b.At(x, A3b.KeyF), A3b.Mid), "F moved, tick " + t);
                    Assert.That(A3b.Due(x), Is.EqualTo(121000L), "due, tick " + t);
                    Assert.That(A3b.Ammo(x), Is.EqualTo(3), "ammo, tick " + t);
                }
                A3b.AssertNoFriendRecord(x);
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 160, 190, 220, 280, 310, 340)));
            List<long> control = A3b.Run(A3b.World(A3b.Ai(300, 300), null, false, null, A3b.Friend()), 400, A3b.FScript);
            List<long> alone = A3b.Run(A3b.World(A3b.Ai(300, 300)), 400, A3b.Script);
            Assert.That(control, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 151, 181, 241, 271, 301)));
            Assert.That(alone, Is.EqualTo(control));
            A3b.ReverseEqual(A3b.Ai(300, 300), 400, A3b.FScript, (x, t) =>
            {
                if (t == 100) A3b.Place(x, A3b.KeyF, A3b.Mid);
                if (t == 160) A3b.Place(x, A3b.KeyF, A3b.FHome);
            }, null, A3b.Friend());
        }

        // 4(e): F beyond P on the same line (17 m from A, within the 30 m range) also declines the shot: the check runs
        // the whole flight, not only to P.
        [Test]
        public void AllyBeyondThePoint_StillDeclined()
        {
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 300), null, false, null, A3b.Friend()), 400, A3b.FScript, (x, t) =>
            {
                if (t == 100)
                {
                    A3b.Place(x, A3b.KeyF, A3b.Behind);
                    EntityId a = A3b.A(x);
                    Assert.That(x.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.True);
                    Assert.That(x.Combat.AllyBlocks(a, A3b.P, CombatSystem.Horizontal(A3b.P - x.Combat.Point(a)).Length), Is.False);
                }
                if (t == 160) A3b.Place(x, A3b.KeyF, A3b.FHome);
            }, (x, t) => A3b.AssertNoFriendRecord(x));
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 160, 190, 220, 280, 310, 340)));
        }

        // 4(f): F walks west from 108 and is on the line at the end of ticks 151..155. On 151 the rifle is ready and the
        // intent was submitted before the movement, but F stands on the line when it is consumed: no shot until 156.
        [Test]
        public void AllyCrossesTheLine_DeclinedOnTheReadyTick()
        {
            SimVec3 west = A2.Cell(18, 16);
            List<long> fires = A3b.Run(A3b.World(A3b.Ai(300, 300), null, false, null, A3b.Friend()), 300,
                t => t == 108 ? A3.Command(SquadCommandKind.MoveTo, 3, west) : A3b.FScript(t), null, (x, t) =>
                {
                    bool onLine = x.Combat.AllyBlocks(A3b.A(x), A3b.P, A3b.R30);
                    if (t == 150) Assert.That(onLine, Is.False);
                    if (t >= 151 && t <= 155) Assert.That(onLine, Is.True, "on line, tick " + t);
                    if (t == 156) Assert.That(onLine, Is.False);
                    if (t >= 150 && t <= 155) Assert.That(A3b.Due(x), Is.EqualTo(151000L), "due, tick " + t);
                    A3b.AssertNoFriendRecord(x);
                });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 156, 186, 246, 276)));
        }

        // 5(a): the read ports without stepping. B at its start is 0.244 m off the ray A -> P and is hit first (an enemy
        // before the friend gives false); with B moved off the ray F counts on the midpoint and beyond P (but not when the
        // trace stops at P), not at home and not as team 2. A point on the shooter gives no line.
        [Test]
        public void AllyBlocks_Table()
        {
            M6World w = A3b.World(A3b.Ai(300, 300), null, false, null, A3b.Friend());
            EntityId a = A3b.A(w);
            SimFix toP = CombatSystem.Horizontal(A3b.P - w.Combat.Point(a)).Length;
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.False, "F at home");
            A3b.Place(w, A3b.KeyF, A3b.Behind);
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.False, "B first, F beyond P");
            A3b.Place(w, A3b.KeyB, A2.Cell(12, 19));
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.True, "F beyond P");
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, toP), Is.False, "F beyond P, traced to P only");
            A3b.Place(w, A3b.KeyF, A3b.Mid);
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.True, "F on the midpoint");
            A3b.Place(w, A3b.KeyF, A3b.FHome);
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.False, "F home again");
            A3b.Place(w, A3b.KeyF, A3b.Mid); A3b.SetTeam(w, A3b.KeyF, 2);
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.False, "F on the midpoint as team 2");
            Assert.That(w.Combat.AllyBlocks(a, A3.AAt, A3b.R30), Is.False, "no line");
        }

        // 5(a): the point line of sight agrees with the unit line of sight, and the wall blocks the cells behind it.
        [Test]
        public void Visible_Table()
        {
            M6World w = A3b.World(A3b.Ai(300, 300));
            EntityId a = A3b.A(w), b = w.Unit(A3b.KeyB);
            foreach (SimVec3 blocked in new[] { A2.Cell(12, 16), A2.Cell(12, 19), A2.Cell(14, 15) }) Assert.That(w.Combat.Visible(a, blocked), Is.False, A3b.V(blocked));
            foreach (SimVec3 clear in new[] { A2.Cell(21, 21), A2.Cell(17, 15), A3.AAt }) Assert.That(w.Combat.Visible(a, clear), Is.True, A3b.V(clear));
            Assert.That(w.Combat.Visible(a, b), Is.True);
            Assert.That(w.Combat.Visible(a, b), Is.EqualTo(w.Combat.Visible(a, w.Combat.Point(b))));
            A3b.Place(w, A3b.KeyB, A2.Cell(12, 19));
            Assert.That(w.Combat.Visible(a, b), Is.False);
            Assert.That(w.Combat.Visible(a, b), Is.EqualTo(w.Combat.Visible(a, w.Combat.Point(b))));
        }

        // 5(b): a 2 m ridge north of A (z 4..5, slopes 1:1) does not block the wall-only line to Q = Cell(22,1) on this code
        // (the terrain line of sight is another package's); the lines to P and B are clear. Static read ports only.
        [Test]
        public void Terrain_Ridge_WallOnlyLineOfSight()
        {
            Func<int, int, int> ridge = (i, j) => (j == 4 || j == 5) ? 2 * (int)A2.S : (j == 3 || j == 6) ? (int)A2.S : 0;
            M6World w = A3b.World(A3b.Ai(300, 300), null, false, ridge);
            EntityId a = A3b.A(w), b = w.Unit(A3b.KeyB);
            Assert.That(A3b.At(w, A3b.KeyA).Y.Raw, Is.EqualTo(0L));
            Assert.That(w.Combat.Visible(a, A2.Cell(22, 1)), Is.True);
            Assert.That(w.Combat.Visible(a, A3b.P), Is.True);
            Assert.That(w.Combat.Visible(a, b), Is.True);
            Assert.That(w.Combat.Visible(a, b), Is.EqualTo(w.Combat.Visible(a, w.Combat.Point(b))));
            Assert.That(w.Combat.AllyBlocks(a, A3b.P, A3b.R30), Is.False);
        }

        // 5(b): A's side raised 2 m (z <= 12, one 2:1 step at z 12, no ridge): A stands at y = 2 m; the lines to P, to
        // Q2 at the surface height and to B are clear.
        [Test]
        public void Terrain_Plateau_LinesAreClear()
        {
            Func<int, int, int> plateau = (i, j) => j <= 12 ? 2 * (int)A2.S : 0;
            M6World w = A3b.World(A3b.Ai(300, 300), null, false, plateau);
            EntityId a = A3b.A(w), b = w.Unit(A3b.KeyB);
            Assert.That(A3b.Same(A3b.At(w, A3b.KeyA), A3b.Raw(1474560, 131072, 688128)), A3b.V(A3b.At(w, A3b.KeyA)));
            Assert.That(w.Combat.Visible(a, A3b.P), Is.True);
            Assert.That(w.Combat.Visible(a, A3b.Raw(1474560, 131072, 98304)), Is.True);
            Assert.That(w.Combat.Visible(a, b), Is.True);
            Assert.That(w.Combat.Visible(a, b), Is.EqualTo(w.Combat.Visible(a, w.Combat.Point(b))));
        }
    }
}
