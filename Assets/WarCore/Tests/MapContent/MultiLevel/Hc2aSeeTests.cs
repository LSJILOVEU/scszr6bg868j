using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // HC-2a: the see-through ring's threshold is the unit's body height at its stance (LegacySeeHeight without a body); whether
    // the unit sees a sample stays judged from its eye. The layer cut's headroom is WallSpan's walk clearance. Grounds as in
    // See1ZoneTests.LowWallsDoNotFlicker: a wall on x 10..30, z 40..41, the unit at (20, 43), radius 5; the samples north of
    // the wall see it from any eye used here and their lines back cross it.
    public sealed class Hc2aSeeTests
    {
        private const long M = SimFix.Scale;

        // A pack made in code: standing height and eye (muzzle at the eye) and, when crouch is given, a crouch level (height,
        // eye). Built directly, not through the pack reader.
        private static BodySizes Sizes(double height, double eye, (double Height, double Eye)? crouch = null)
        {
            CombatPack basis = RwrPack.Load("rwr-stance").BuildCombat();
            CombatBody Body(double h, double e) =>
                new CombatBody(SimFix.FromRaw((long)Math.Round(h * M)), SimFix.FromRaw((long)Math.Round(e * M)), SimFix.FromRaw((long)Math.Round(e * M)));
            StanceDef[] stances = crouch.HasValue ? new[] { new StanceDef("crouch", Body(crouch.Value.Height, crouch.Value.Eye), 500, 700, 6) } : null;
            var pack = new CombatPack(basis.Weapons.ToArray(), basis.Resolver, basis.Selector, basis.MaxHealthRaw, body: Body(height, eye),
                stances: stances);
            return BodySizes.For(pack);
        }

        private static CutState Wall(double height, BodySizes sizes, int stance = 0)
        {
            ViewMap map = Map((10, 40, 20, 1, height));
            var me = new ViewEntity(1, 0, 20f, 0f, 43f, team: 1, stance: stance);
            return LayerCut.Run(map.Plates, map, me, Forward, 5f, sizes).Wall(0);
        }

        // S2: the threshold is the body's height, not its eye. A body 1.75 tall (eye 1.5) keeps a 1.6 m wall, which tops its eye,
        // and fades a 1.8 m one; a body 1.2 tall (eye 1.0) fades a 1.3 m wall that the 1.75 m body keeps.
        [Test]
        public void S2_TheThresholdIsTheBodysHeight()
        {
            BodySizes tall = Sizes(1.75, 1.5), small = Sizes(1.2, 1.0);
            Assert.That(Wall(1.6, tall), Is.EqualTo(CutState.Shown), "1.75 m body, 1.6 m wall");
            Assert.That(Wall(1.8, tall), Is.EqualTo(CutState.Faded), "1.75 m body, 1.8 m wall");
            Assert.That(Wall(1.3, tall), Is.EqualTo(CutState.Shown), "1.75 m body, 1.3 m wall");
            Assert.That(Wall(1.3, small), Is.EqualTo(CutState.Faded), "1.2 m body, 1.3 m wall");
            Assert.That(Wall(1.1, small), Is.EqualTo(CutState.Shown), "1.2 m body, 1.1 m wall");
            var me = new ViewEntity(1, 0, 0f, 0f, 0f, team: 1);
            Assert.That(LayerCut.SeeHeight(small, me), Is.EqualTo(small.Top(me)));
        }

        // S2, the plate branch: in the demo house a platform 1 m up (top 1 m over the feet) stays for a 1.75 m body and fades for a
        // 0.9 m one (eye 0.8); one 2 m up fades for both.
        [Test]
        public void S2_ThePlateThresholdIsTheBodysHeight()
        {
            MapPlate Platform(long top) => new MapPlate(10, new MapXZ(3, 6), new MapXZ(3, 4), top, null, M / 2);
            ViewEntity me = At(11.5f, 7.5f);
            BodySizes tall = Sizes(1.75, 1.5), small = Sizes(0.9, 0.8);
            var (lowPlates, lowMap) = House(plates: new[] { Platform(M) });
            var (highPlates, highMap) = House(plates: new[] { Platform(2 * M) });
            Assert.That(LayerCut.Run(lowPlates, lowMap, me, Forward, 5, tall).Plate(2), Is.EqualTo(CutState.Shown), "1.75 m body, 1 m");
            Assert.That(LayerCut.Run(lowPlates, lowMap, me, Forward, 5, small).Plate(2), Is.EqualTo(CutState.Faded), "0.9 m body, 1 m");
            Assert.That(LayerCut.Run(lowPlates, lowMap, me, Forward, 5, null).Plate(2), Is.EqualTo(CutState.Shown), "no body: 1.5");
            Assert.That(LayerCut.Run(highPlates, highMap, me, Forward, 5, tall).Plate(2), Is.EqualTo(CutState.Faded), "1.75 m body, 2 m");
            Assert.That(LayerCut.Run(highPlates, highMap, me, Forward, 5, small).Plate(2), Is.EqualTo(CutState.Faded), "0.9 m body, 2 m");
        }

        // S3: without a body the threshold stays 1.5 m (not the old 1 m head): 1.4 m stays, 1.6 m fades.
        [Test]
        public void S3_WithoutABodyTheThresholdIsTheLegacyHeight()
        {
            Assert.That(LayerCut.SeeHeight(null, new ViewEntity(1, 0, 0f, 0f, 0f, team: 1)), Is.EqualTo(LayerCut.LegacySeeHeight));
            Assert.That(LayerCut.LegacySeeHeight, Is.EqualTo(1.5f));
            Assert.That(Wall(1.4, null), Is.EqualTo(CutState.Shown), "1.4 m");
            Assert.That(Wall(1.6, null), Is.EqualTo(CutState.Faded), "1.6 m");
        }

        // S4: the threshold follows the stance, like the drawn body. rwr-stance stands 1.75 tall and crouches 1.0 (eye 0.75): a
        // 1.2 m wall stays for a standing unit and fades for a crouched one; a 0.9 m wall stays for both.
        [Test]
        public void S4_TheHeightFollowsTheStance()
        {
            BodySizes stance = BodySizes.For(RwrPack.Load("rwr-stance").BuildCombat());
            Assert.That(Wall(1.2, stance, 0), Is.EqualTo(CutState.Shown), "standing, 1.75 m");
            Assert.That(Wall(1.2, stance, 1), Is.EqualTo(CutState.Faded), "crouched, 1.0 m");
            Assert.That(Wall(0.9, stance, 1), Is.EqualTo(CutState.Shown), "crouched, 0.9 m wall");
            var crouched = new ViewEntity(1, 0, 0f, 0f, 0f, team: 1, stance: 1);
            Assert.That(LayerCut.SeeHeight(stance, crouched), Is.EqualTo(stance.Top(crouched)));
            Assert.That(stance.Top(crouched), Is.LessThan(stance.StandingHeight));
        }

        // S6: the ring's radius is 8/7 of the standing height: 2 m for every shipped body (all 1.75 m, so the picture keeps its
        // ring), 2 m without a body, more for a bigger body and the same whatever the stance.
        [TestCase("rwr-body")]
        [TestCase("rwr-field")]
        [TestCase("rwr-showcase")]
        [TestCase("rwr-showcase-hamlet")]
        [TestCase("rwr-stance")]
        public void S6_ShippedBodiesKeepTheTwoMetreRing(string pack)
        {
            BodySizes sizes = BodySizes.For(RwrPack.Load(pack).BuildCombat());
            Assert.That(sizes, Is.Not.Null);
            Assert.That(LayerCut.SeeRadiusFor(sizes), Is.EqualTo(LayerCut.SeeRadius));
            Assert.That(LayerCut.SeeRadius, Is.EqualTo(2f));
        }

        [Test]
        public void S6_TheRingFollowsTheStandingHeight()
        {
            Assert.That(LayerCut.SeeRadiusFor(null), Is.EqualTo(LayerCut.SeeRadius));
            Assert.That(LayerCut.SeeRadiusFor(Sizes(3.5, 3.0)), Is.EqualTo(4f).Within(1e-5f), "a 3.5 m body: 4 m");
            Assert.That(LayerCut.SeeRadiusFor(Sizes(0.875, 0.75)), Is.EqualTo(1f).Within(1e-5f), "a 0.875 m body: 1 m");
            Assert.That(LayerCut.SeeRadiusFor(Sizes(1.75, 1.5, (1.0, 0.75))), Is.EqualTo(2f), "crouch levels do not change it");
        }

        // S5: the layer cut's headroom is WallSpan's walk clearance, 2 m.
        [Test]
        public void S5_TheHeadroomIsTheWalkClearance()
        {
            Assert.That(LayerCut.Headroom, Is.EqualTo(WallSpan.ClearanceRaw / 65536f));
            Assert.That(WallSpan.ClearanceMetres, Is.EqualTo(WallSpan.ClearanceRaw / 65536f));
            Assert.That(LayerCut.Headroom, Is.EqualTo(2f));
        }
    }
}
