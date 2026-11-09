using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L9 acceptance 2 and 3: the ridge (3 m at x = 12, L9Fixture) hides the two sides from each other and stops shots; the flat
    // twin with the same units is the control, so the ridge results cannot come from "nobody shoots".
    public sealed class L9SightTests
    {
        private const long S = SimFix.Scale;
        private static readonly SimVec3 FarSide = L9.Mm(20500, 0, 4500);

        private static UnitSpawn[] Pair(long bxMm, int bWeapon) => new[] { L9.Unit(1, 1, 3500, 4500, 0), L9.Unit(2, 2, bxMm, 4500, bWeapon) };

        [TestCase(true)]
        [TestCase(false)]
        public void L9Sight_RidgeHidesBothSidesFlatTwinDoesNot(bool ridge)
        {
            var w = L9.World(L9.RidgeMap(ridge), Pair(20500, 0));
            bool ab = w.Combat.Visible(w.Unit(1), w.Unit(2)), ba = w.Combat.Visible(w.Unit(2), w.Unit(1));
            var log = new List<(int, FireRecord)>();
            L9.Run(w, 300, log);
            if (ridge)
            {
                Assert.That(ab, Is.False); Assert.That(ba, Is.False);
                Assert.That(log, Is.Empty, "no shot across the ridge");
            }
            else
            {
                Assert.That(ab, Is.True); Assert.That(ba, Is.True);
                Assert.That(log, Is.Not.Empty);
                Assert.That(log[0].Item2.Outcome, Is.EqualTo(FireOutcome.Hit));
            }
        }

        // One aimed shot on tick 1 from unit 1 towards (20.5, 0, 4.5); unit 2 is unarmed.
        private static FireRecord OneShot(byte[] map, long bxMm, out M6World world)
        {
            world = L9.World(map, Pair(bxMm, -1), 0, new L9.Aim(1, 1, 1, FarSide));
            world.Step();
            Assert.That(world.Combat.Fires.Length, Is.EqualTo(1), "one record on tick 1");
            return world.Combat.Fires[0];
        }

        // 3(a) terrain before the person: the lifted line (0.5 m) touches the ground at x = 7 (0.5 m) and first goes under it at
        // the diagonal x = 7.5 (0.75 m); the record ends there, lowered back to the shot height.
        [Test]
        public void L9Sight_RidgeStopsTheShotAndFlatTwinHits()
        {
            FireRecord f = OneShot(L9.RidgeMap(true), 20500, out _);
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            Assert.That(f.Target, Is.EqualTo(default(StableId)), "no target: unit 2 takes nothing");
            Assert.That(f.End, Is.EqualTo(L9.Raw(491520, 0, 294912)));
            Assert.That(f.Distance.Raw, Is.EqualTo(4 * S));

            FireRecord flat = OneShot(L9.RidgeMap(false), 20500, out M6World fw);
            Assert.That(flat.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(flat.Target, Is.EqualTo(fw.Sim.Entities.StableOf(fw.Unit(2))));
            Assert.That(flat.End, Is.EqualTo(L9.Raw(1327104, 0, 294912)));
        }

        // 3(b) the person before the terrain: unit 2 at x = 5.5 (flat ground) is hit before x = 7.5.
        [Test]
        public void L9Sight_APersonBeforeTheRidgeIsHit()
        {
            FireRecord f = OneShot(L9.RidgeMap(true), 5500, out M6World w);
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(f.Target, Is.EqualTo(w.Sim.Entities.StableOf(w.Unit(2))));
            Assert.That(f.End, Is.EqualTo(L9.Raw(344064, 0, 294912)));
        }

        // 3(c) a wall before the terrain: the box x 5..6 stops the shot on its face x = 5.
        [Test]
        public void L9Sight_AWallBeforeTheRidgeStopsTheShot()
        {
            FireRecord f = OneShot(L9.RidgeMap(true, new[] { (5, 3, 1, 3) }), 20500, out _);
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            Assert.That(f.Target, Is.EqualTo(default(StableId)));
            Assert.That(f.End, Is.EqualTo(L9.Raw(327680, 0, 294912)));
        }

        // 3(d) an oblique shot: shooter (2.5, 1.25), aimed at (5.5, 0, 2.25), direction (3, 0, 1); the first blocked sample of the
        // lifted line, lowered by 0.5 m, is the record end as is (recomputing origin + unit direction * distance would round it
        // to (237566, 0, 106495)).
        [Test]
        public void L9Sight_AnObliqueShotEndsExactlyOnTheBlockedSample()
        {
            var units = new[] { L9.Unit(1, 1, 2500, 1250, 0), L9.Unit(2, 2, 7500, 3500, -1) };
            var w = L9.World(L9.ObliqueMap(), units, 0, new L9.Aim(1, 1, 1, L9.Mm(5500, 0, 2250)));
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            FireRecord f = w.Combat.Fires[0];
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
            Assert.That(f.End, Is.EqualTo(L9.Raw(237568, 0, 106496)));
        }

        // 3(e) the terrain exactly at the hit point: on the bump map the person's capsule is entered at x = 10 and the lifted line
        // first goes under the ground at the grid line x = 10 (0.625 m > 0.5 m; the diagonal x = 9.5 is at 0.3125 m). The ground
        // wins the tie. On the flat twin the same shot is a hit ending at the same point.
        [TestCase(true)]
        [TestCase(false)]
        public void L9Sight_TheGroundWinsATieWithThePerson(bool bump)
        {
            FireRecord f = OneShot(L9.BumpMap(bump), 10250, out M6World w);
            Assert.That(f.End, Is.EqualTo(L9.Raw(655360, 0, 294912)));
            if (bump)
            {
                Assert.That(w.Read<Position>(2).Value.Y.Raw, Is.EqualTo(14336), "unit 2 stands on the slope");
                Assert.That(f.Outcome, Is.EqualTo(FireOutcome.WallBlocked));
                Assert.That(f.Target, Is.EqualTo(default(StableId)));
                Assert.That(f.Distance.Raw, Is.EqualTo(425984));
            }
            else
            {
                Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
                Assert.That(f.Target, Is.EqualTo(w.Sim.Entities.StableOf(w.Unit(2))));
            }
        }
    }
}
