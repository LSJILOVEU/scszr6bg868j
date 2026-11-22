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
    // HC-2a: the see-through ring's threshold is the body's standing eye (LegacySeeHeight without a body), and the layer cut's
    // headroom is WallSpan's walk clearance. Grounds as in See1ZoneTests.LowWallsDoNotFlicker: a wall on x 10..30, z 40..41,
    // the unit at (20, 43), radius 5; the samples north of the wall see it from any eye and their lines back cross it.
    public sealed class Hc2aSeeTests
    {
        private const long M = SimFix.Scale;

        // A pack made in code with the given standing eye (height 1.75, muzzle at the eye) and, when crouchEye is given, a
        // crouch level (height 1.0) with that eye. Built directly, not through the pack reader.
        private static BodySizes Sizes(double eye, double? crouchEye = null)
        {
            CombatPack basis = RwrPack.Load("rwr-stance").BuildCombat();
            CombatBody Body(double h, double e) =>
                new CombatBody(SimFix.FromRaw((long)Math.Round(h * M)), SimFix.FromRaw((long)Math.Round(e * M)), SimFix.FromRaw((long)Math.Round(e * M)));
            StanceDef[] stances = crouchEye.HasValue ? new[] { new StanceDef("crouch", Body(1.0, crouchEye.Value), 500, 700, 6) } : null;
            var pack = new CombatPack(basis.Weapons.ToArray(), basis.Resolver, basis.Selector, basis.MaxHealthRaw, body: Body(1.75, eye),
                stances: stances);
            return BodySizes.For(pack);
        }

        private static CutState Wall(double height, BodySizes sizes, int stance = 0)
        {
            ViewMap map = Map((10, 40, 20, 1, height));
            var me = new ViewEntity(1, 0, 20f, 0f, 43f, team: 1, stance: stance);
            return LayerCut.Run(map.Plates, map, me, Forward, 5f, sizes).Wall(0);
        }

        // S2: an eye at 1.2 m fades a wall topping the feet by 1.3 m; an eye at 1.5 m (every shipped body) keeps it.
        [Test]
        public void S2_TheThresholdIsTheBodysEye()
        {
            Assert.That(Wall(1.3, Sizes(1.2)), Is.EqualTo(CutState.Faded), "eye 1.2 under a 1.3 m wall");
            Assert.That(Wall(1.3, Sizes(1.5)), Is.EqualTo(CutState.Shown), "eye 1.5 over a 1.3 m wall");
            Assert.That(Wall(1.6, Sizes(1.5)), Is.EqualTo(CutState.Faded), "eye 1.5 under a 1.6 m wall");
            Assert.That(Wall(1.3, null), Is.EqualTo(CutState.Shown), "no body: 1.5");
            Assert.That(LayerCut.SeeHeight(Sizes(1.2)), Is.EqualTo(Sizes(1.2).StandingEye));
        }

        // S2, the plate branch: in the demo house a platform 1 m up (top 1 m over the feet) stays with an eye at 1.5 m and fades
        // with an eye at 0.9 m; one 2 m up fades with both.
        [Test]
        public void S2_ThePlateThresholdIsTheBodysEye()
        {
            MapPlate Platform(long top) => new MapPlate(10, new MapXZ(3, 6), new MapXZ(3, 4), top, null, M / 2);
            ViewEntity me = At(11.5f, 7.5f);
            var (lowPlates, lowMap) = House(plates: new[] { Platform(M) });
            var (highPlates, highMap) = House(plates: new[] { Platform(2 * M) });
            Assert.That(LayerCut.Run(lowPlates, lowMap, me, Forward, 5, Sizes(1.5)).Plate(2), Is.EqualTo(CutState.Shown), "eye 1.5 over 1 m");
            Assert.That(LayerCut.Run(lowPlates, lowMap, me, Forward, 5, Sizes(0.9)).Plate(2), Is.EqualTo(CutState.Faded), "eye 0.9 under 1 m");
            Assert.That(LayerCut.Run(lowPlates, lowMap, me, Forward, 5, null).Plate(2), Is.EqualTo(CutState.Shown), "no body: 1.5");
            Assert.That(LayerCut.Run(highPlates, highMap, me, Forward, 5, Sizes(1.5)).Plate(2), Is.EqualTo(CutState.Faded), "eye 1.5 under 2 m");
            Assert.That(LayerCut.Run(highPlates, highMap, me, Forward, 5, Sizes(0.9)).Plate(2), Is.EqualTo(CutState.Faded), "eye 0.9 under 2 m");
        }

        // S3: without a body the threshold stays 1.5 m (not the old 1 m eye): 1.4 m stays, 1.6 m fades.
        [Test]
        public void S3_WithoutABodyTheThresholdIsTheLegacyHeight()
        {
            Assert.That(LayerCut.SeeHeight(null), Is.EqualTo(LayerCut.LegacySeeHeight));
            Assert.That(LayerCut.LegacySeeHeight, Is.EqualTo(1.5f));
            Assert.That(Wall(1.4, null), Is.EqualTo(CutState.Shown), "1.4 m");
            Assert.That(Wall(1.6, null), Is.EqualTo(CutState.Faded), "1.6 m");
        }

        // S4: crouched, the threshold is still the standing eye. rwr-stance stands at eye 1.5 and crouches at 0.75: a 1.2 m wall
        // stays for a crouched unit (1.2 < 1.5) though it tops the crouched eye. A body standing at 1.2 crouched at 0.75 fades a
        // 1.3 m wall and keeps a 1.0 m one.
        [Test]
        public void S4_TheStandingEyeWhateverTheStance()
        {
            BodySizes stance = BodySizes.For(RwrPack.Load("rwr-stance").BuildCombat());
            Assert.That(stance.StandingEye, Is.EqualTo(1.5f));
            Assert.That(Wall(1.2, stance, 1), Is.EqualTo(CutState.Shown), "crouched, standing eye 1.5");
            Assert.That(Wall(1.2, stance, 0), Is.EqualTo(CutState.Shown), "standing, eye 1.5");
            BodySizes low = Sizes(1.2, 0.75);
            Assert.That(low.StandingEye, Is.EqualTo(low.Eye(new ViewEntity(1, 0, 0f, 0f, 0f, team: 1))));
            Assert.That(Wall(1.0, low, 1), Is.EqualTo(CutState.Shown), "crouched, standing eye 1.2 over 1.0");
            Assert.That(Wall(1.3, low, 1), Is.EqualTo(CutState.Faded), "crouched, standing eye 1.2 under 1.3");
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
