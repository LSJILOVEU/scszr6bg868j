using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-3: the AI read ports (Sees, WallWithin, Visible to a ground point, AllyBlocks) and the melee thrust and wall line,
    // each in body mode with a geometry where the legacy heights would answer differently.
    public sealed class Ml3SightPortsTests
    {
        private const long M = Ml3World.M;
        private static Ml3World.Unit U(long x, long y, long z, int team, int weapon = -1) => new Ml3World.Unit(x, y, z, team, weapon);
        // A wall 1 m thick across x = 5 m (z -2..2 m), from the ground to top.
        private static Ml3World.Wall[] Across(long top) => new[] { new Ml3World.Wall(9 * M / 2, -2 * M, 11 * M / 2, 2 * M, 0, top) };

        // A 1 m wall between (0, 0) and (10, 0): eye 1.5 m sees over it and no wall stands on the eye line; the legacy
        // 0.5 m eye is hidden and the wall stands on its line (Cover reads both).
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        public void SeesAndWallWithinReadTheEye(bool bodied, bool sees, bool wall)
        {
            var w = new Ml3World(Ml3World.Pack(bodied ? Ml3World.Rwr : (CombatBody?)null), new[] { U(0, 0, 20 * M, 1) }, Across(M));
            SimVec3 from = Ml3World.Raw(0, 0, 0), to = Ml3World.Raw(10 * M, 0, 0);
            Assert.That((w.Combat.Sees(from, to), w.Combat.WallWithin(from, to, SimFix.FromInt(20))), Is.EqualTo((sees, wall)));
        }

        // Visible to a ground point behind the 1 m wall: eye to the point lifted to the eye (1.5 m) passes over it; a line
        // ending on the ground point would cross x = 5 m at 0.75 m, into the wall.
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void VisibleToAGroundPointLiftsThePoint(bool bodied, bool visible)
        {
            var w = new Ml3World(Ml3World.Pack(bodied ? Ml3World.Rwr : (CombatBody?)null), new[] { U(0, 0, 0, 1) }, Across(M));
            Assert.That(w.Combat.Visible(w.U(0), Ml3World.Raw(10 * M, 0, 0)), Is.EqualTo(visible));
        }

        // AllyBlocks toward the ground point (10, 0, 0) runs muzzle (1.5 m) to the point lifted to the eye (1.5 m). A teammate
        // standing with its feet 1.0 m up at x = 8 (body 1.0..2.75 m) is on that line; a line ending on the ground would pass
        // it at 0.3 m. One with its feet 1.3 m up at x = 2 is on it too; a line from the foot would pass it at 0.3 m.
        [TestCase(8 * M, M)]
        [TestCase(2 * M, 13 * M / 10)]
        public void AllyBlocksRunsMuzzleToTheLiftedPoint(long allyX, long allyY)
        {
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr), new[] { U(0, 0, 0, 1, 0), U(allyX, allyY, 0, 1) });
            Assert.That(w.Combat.AllyBlocks(w.U(0), Ml3World.Raw(10 * M, 0, 0), SimFix.FromInt(20)), Is.True);
        }

        // A thrust (line half width 0.2 m) runs at the body centre (0.875 m): an enemy 1 m ahead with its feet 0.5 m up (body
        // 0.5..2.25 m) is struck; a thrust at the foot would reach only 0.2 m up. Far below a thin floor this is what keeps a
        // thrust from reaching the storey underneath.
        [Test]
        public void ThrustRunsAtTheBodyCentre()
        {
            var spec = new MeleeSpec(2, 1, 3, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(1), 4, SimFix.FromInt(2), SimFix.Ratio(1, 5));
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr, melee: new[] { spec }), new[] { U(0, 0, 0, 1), U(M, M / 2, 0, 2) });
            Assert.That(Landed(w), Is.EqualTo(1));
        }

        // The melee wall line runs eye to eye: with eye 1.5 m and muzzle 1.25 m a 1.4 m wall is under the eye line and the
        // swing lands; a line from the muzzle would hit the wall.
        [Test]
        public void MeleeWallLineStartsAtTheEye()
        {
            var spec = new MeleeSpec(3, 1, 3, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(5), 1, SimFix.FromInt(2));
            var wall = new[] { new Ml3World.Wall(M / 2, -2 * M, 3 * M / 2, 2 * M, 0, 14 * M / 10) };
            var w = new Ml3World(Ml3World.Pack(Ml3World.Body(114688, 98304, 81920), new LethalResolver(), new[] { spec }), new[] { U(0, 0, 0, 1), U(2 * M, 0, 0, 2) }, wall);
            Assert.That(Landed(w), Is.EqualTo(1));
        }

        private static int Landed(Ml3World w)
        {
            w.Combat.SetMelee(w.U(0), 0);
            w.Produce = c => { if (c.Clock.Tick == 1) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), w.Id(1))); };
            int landed = 0;
            for (int t = 0; t < 6; t++) { w.Step(); landed += w.Combat.Strikes.ToArray().Count(s => s.Kind == StrikeKind.Landed); }
            return landed;
        }
    }
}
