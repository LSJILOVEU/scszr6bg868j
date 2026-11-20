using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // NT-1 A6: with more than two teams the leader in a circle has strictly the most units, and its advantage is judged against
    // the next largest team, never the smallest. One point (id 7) at (20, 20), radius 6; ratio 2 : 1, not strict, 3 ticks; hits
    // deal no damage, so the counts hold.
    public sealed class Nt1CaptureTests
    {
        private const int Ticks = 3;

        // counts[t - 1] units of team t in the circle; team 9 units (outside every team) are added on their own row.
        private static M6World World(int[] counts, int teamNine = 0, bool thirdBase = true, int num = 2, bool second = false)
        {
            // second: the units stand in a second point (id 8) at (50, 20) instead, the first one stays empty.
            var units = new List<UnitSpawn>();
            ulong key = 1; int dx = second ? 30 : 0;
            for (int t = 1; t <= counts.Length; t++)
                for (int j = 0; j < counts[t - 1]; j++) units.Add(M6World.U(key++, t, 15 + j + dx, 16 + 2 * t));
            for (int j = 0; j < teamNine; j++) units.Add(M6World.U(key++, 9, 15 + j + dx, 24));
            var bases = new List<SpawnAnchor> { new SpawnAnchor(1, M6World.P(6)), new SpawnAnchor(2, M6World.P(74)) };
            if (thirdBase) bases.Add(new SpawnAnchor(3, M6World.P(40, 2)));
            var points = second
                ? new[] { new ControlPointDef(7, M6World.P(20), SimFix.FromInt(6)), new ControlPointDef(8, M6World.P(50), SimFix.FromInt(6)) }
                : new[] { new ControlPointDef(7, M6World.P(20), SimFix.FromInt(6)) };
            var pack = M6World.Pack(points, num: num, den: 1, strict: false, ticks: Ticks, bases: bases.ToArray());
            return new M6World(M6World.Endless(M6World.RecipeOf(units.ToArray(), pack, M6World.Guns(new ZeroResolver()))));
        }

        private static void NobodyLeads(M6World w)
        {
            for (int tick = 1; tick <= Ticks + 2; tick++)
            {
                w.Step();
                Assert.That((w.Capture.Points[0].Owner, w.Capture.Points[0].Capturing, w.Capture.Points[0].Progress), Is.EqualTo((0, 0, 0)), "tick " + tick);
                Assert.That(w.Capture.Changes.Length, Is.Zero);
            }
        }

        private static void Captures(M6World w, int team)
        {
            for (int tick = 1; tick < Ticks; tick++)
            {
                w.Step();
                Assert.That((w.Capture.Points[0].Owner, w.Capture.Points[0].Capturing, w.Capture.Points[0].Progress), Is.EqualTo((0, team, tick)), "tick " + tick);
            }
            w.Step();
            Assert.That((w.Capture.Points[0].Owner, w.Capture.Points[0].Capturing, w.Capture.Points[0].Progress), Is.EqualTo((team, 0, 0)));
            Assert.That(w.Capture.Changes.Length, Is.EqualTo(1));
            Assert.That((w.Capture.Changes[0].Tick, w.Capture.Changes[0].PointId, w.Capture.Changes[0].OldOwner, w.Capture.Changes[0].NewOwner),
                Is.EqualTo(((long)Ticks, 7, 0, team)));
        }

        // 5 : 1 against the smallest team would hold; 5 : 3 against the second does not.
        [Test]
        public void Nt1Capture_FiveThreeOne_NobodyLeads()
        {
            var w = World(new[] { 5, 3, 1 });
            Assert.That(w.Recipe.Match.TeamCount, Is.EqualTo(3));
            NobodyLeads(w);
        }

        [Test]
        public void Nt1Capture_SixThreeOne_FirstTeamCaptures() => Captures(World(new[] { 6, 3, 1 }), 1);

        [Test]
        public void Nt1Capture_OneTwoFive_ThirdTeamCaptures() => Captures(World(new[] { 1, 2, 5 }), 3);

        // 5 : 1 against the smallest would hold; the second largest (3) comes before the leader in team order.
        [Test]
        public void Nt1Capture_ThreeFiveOne_NobodyLeads() => NobodyLeads(World(new[] { 3, 5, 1 }));

        // A tie for the most leads nobody; only a 1 : 1 ratio could tell, since a tied count never beats a larger ratio.
        [Test]
        public void Nt1Capture_FourFourOne_TieForTheMostAtOneToOne_NobodyLeads() => NobodyLeads(World(new[] { 4, 4, 1 }, num: 1));

        [Test]
        public void Nt1Capture_OneFourFour_TieForTheMostAfterALowerTeam_NobodyLeads() => NobodyLeads(World(new[] { 1, 4, 4 }, num: 1));

        [Test]
        public void Nt1Capture_FourOneTwoAtOneToOne_FirstTeamCaptures() => Captures(World(new[] { 4, 1, 2 }, num: 1), 1);

        // The second point's counts sit at their own offset: 1, 2, 5 there gives it to team 3, the first point stays neutral.
        [Test]
        public void Nt1Capture_SecondPoint_ThirdTeamCapturesIt()
        {
            var w = World(new[] { 1, 2, 5 }, second: true);
            w.Step(Ticks);
            Assert.That((w.Capture.Points[0].Owner, w.Capture.Points[0].Capturing), Is.EqualTo((0, 0)));
            Assert.That(w.Capture.Points[1].Owner, Is.EqualTo(3));
            Assert.That((w.Capture.Changes.Length, w.Capture.Changes[0].PointId, w.Capture.Changes[0].NewOwner), Is.EqualTo((1, 8, 3)));
        }

        [Test]
        public void Nt1Capture_TwoTeamsFourTwo_LeadsAsBefore()
        {
            var w = World(new[] { 4, 2 }, thirdBase: false);
            Assert.That(w.Recipe.Match.TeamCount, Is.EqualTo(2));
            Captures(w, 1);
        }

        // Team 9 is outside 1..TeamCount: its units never count, so 2 : 1 still holds for team 1.
        [Test]
        public void Nt1Capture_TeamNineUnitsDoNotCount()
        {
            var w = World(new[] { 2, 1, 0 }, teamNine: 5);
            Captures(w, 1);
        }
    }
}
