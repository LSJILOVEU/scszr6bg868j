using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // AIM-2 S2-S4: the reach widens the pick cylinder by the body (radius x radiusPermille, height of the stance level x
    // heightPermille), the enemy nearest the ray among those hit wins, and an enemy behind the ground is never picked.
    // Bodies from "rwr-stance": radius 0.25, top 1.75 standing, 1.0 crouched. Reach 3000 / 1150: radius 0.75, 2.0125 / 1.15 tall.
    public sealed class Aim2ReachTests
    {
        private static readonly BodySizes Sizes = BodySizes.For(RwrPack.Load("rwr-stance").BuildCombat());
        private static readonly PickReach Reach = new PickReach(3000, 1150);

        private static ViewEntity Enemy(ulong key, float x, float z, float y = 0f, int stance = 0, int layer = 0) =>
            new ViewEntity(key, 0, x, y, z, team: 2, layer: layer, stance: stance);

        // The player (key 1, team 1) far off at (0, 0, -40), then the enemies in key order.
        private static FrameView Shown(params ViewEntity[] enemies)
        {
            var all = new ViewEntity[enemies.Length + 1];
            all[0] = new ViewEntity(1, 0, 0f, 0f, -40f, team: 1);
            enemies.CopyTo(all, 1);
            return new FrameView(1, all);
        }

        private static AimTarget Pick(FrameView shown, float ox, float oy, float oz, float fx, float fy, float fz, ViewGround ground = null,
            ViewPlates plates = null, PickReach reach = null) =>
            AimPick.PickRay(ox, oy, oz, fx, fy, fz, 0f, ground, shown, 1, plates, null, Sizes, reach ?? Reach);

        private static ulong? Snapped(AimTarget a) => a.Kind == AimKind.Unit ? a.UnitKey : (ulong?)null;

        [Test]
        public void TheReachScalesTheBodyInFloat()
        {
            Assert.That(Sizes.Radius, Is.EqualTo(0.25f));
            Assert.That(Reach.Radius(Sizes.Radius), Is.EqualTo(0.75f));
            Assert.That(Reach.Height(1.75f), Is.EqualTo(1.75f * 1150 / 1000f));
            Assert.That(Reach.Height(1.75f), Is.EqualTo(2.0125f).Within(1e-6));
            Assert.That(new PickReach(2000, 1000).Radius(0.25f), Is.EqualTo(0.5f), "2000 permille is the old 0.5 m round a 0.25 m body");
            Assert.That(() => new PickReach(1999, 1000), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PickReach(6001, 1000), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PickReach(3000, 999), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PickReach(3000, 2001), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        // S2 radius: a near level ray along +x at 1 m passes the axis at (9, 0) at a horizontal distance d: 0.749 snaps, 0.751 not.
        [TestCase(0.749f, true)]
        [TestCase(0.751f, false)]
        [TestCase(-0.749f, true)]
        [TestCase(-0.751f, false)]
        public void S2_TheRadiusIsTheBodyTimesThePermille(float d, bool snaps)
        {
            FrameView shown = Shown(Enemy(2, 9f, 0f));
            AimTarget a = Pick(shown, 5f, 1f, d, 1f, -0.001f, 0f);
            Assert.That(Snapped(a), Is.EqualTo(snaps ? 2UL : (ulong?)null));
        }

        // S2 height: as Scale2aGoldenTests.Sweep group 0, from x 7 along +x through the axis at (9, 0): the ray leaves the
        // 0.75 m circle at x 9.75 (t 2.75). Leaving at or under the reach's top it has met the cylinder; over it, not.
        [TestCase(0, 2.012f, true)]
        [TestCase(0, 2.013f, false)]
        [TestCase(1, 1.149f, true)]
        [TestCase(1, 1.151f, false)]
        public void S2_TheHeightIsTheStanceTopTimesThePermille(int stance, float exitY, bool snaps)
        {
            FrameView shown = Shown(Enemy(2, 9f, 0f, stance: stance));
            float oy = 2.5f, fy = (exitY - oy) / 2.75f;
            AimTarget a = Pick(shown, 7f, oy, 0f, 1f, fy, 0f);
            Assert.That(Snapped(a), Is.EqualTo(snaps ? 2UL : (ulong?)null), "stance " + stance + ", leaving at " + exitY);
        }

        // S3(a): both widened cylinders are hit; the one whose axis is nearer the ray wins although the ray meets it later.
        [Test]
        public void S3a_TheEnemyNearestTheRayWinsOverTheFirstHit()
        {
            FrameView shown = Shown(Enemy(2, 9f, 0.6f), Enemy(3, 12f, 0.1f));
            Assert.That(Snapped(Pick(shown, 5f, 1f, 0f, 1f, -0.001f, 0f)), Is.EqualTo(3UL));
            Assert.That(Snapped(AimPick.PickRay(5f, 1f, 0f, 1f, -0.001f, 0f, 0f, null, shown, 1, null, null, Sizes, new PickReach(3000, 1150))),
                Is.EqualTo(3UL));
        }

        // S3(b): the same distance from the ray (0.25 m beside both, exact in binary so both distances are 0.0625 exactly):
        // the one met first wins, though its key is the larger.
        [Test]
        public void S3b_AtTheSameDistanceTheFirstHitWins()
        {
            FrameView shown = Shown(Enemy(2, 12f, 0.25f), Enemy(3, 9f, 0.25f));
            Assert.That(AimPick.AxisDistanceSquared(5, 1, 0, 1, -0.001, 0, 12, 0, 1.75, 0.25),
                Is.EqualTo(AimPick.AxisDistanceSquared(5, 1, 0, 1, -0.001, 0, 9, 0, 1.75, 0.25)));
            Assert.That(Snapped(Pick(shown, 5f, 1f, 0f, 1f, -0.001f, 0f)), Is.EqualTo(3UL));
        }

        // S3(c): the same distance and the same hit (two enemies on one spot): the smaller key wins.
        [Test]
        public void S3c_AtTheSameDistanceAndHitTheSmallerKeyWins()
        {
            FrameView shown = Shown(Enemy(2, 9f, 0.3f), Enemy(3, 9f, 0.3f));
            Assert.That(Snapped(Pick(shown, 5f, 1f, 0f, 1f, -0.001f, 0f)), Is.EqualTo(2UL));
        }

        // S3(d): the axis runs from the feet to the stance top, not the widened top. A ray at 1.1 m passes a crouched enemy
        // (top 1.0, widened 1.15) 0.2 m beside and a standing one 0.21 m beside: 0.1 m over the crouched head counts, so the
        // standing one is nearer (0.0441 against 0.05); against a 1.15 m axis the crouched one would be (0.04).
        [Test]
        public void S3d_TheAxisEndsAtTheStanceTopNotTheWidenedOne()
        {
            FrameView shown = Shown(Enemy(2, 9f, 0.2f, stance: 1), Enemy(3, 12f, 0.21f));
            Assert.That(Snapped(Pick(shown, 5f, 1.1f, 0f, 1f, -0.00001f, 0f)), Is.EqualTo(3UL));
            Assert.That(AimPick.AxisDistanceSquared(5, 1.1, 0, 1, -0.00001, 0, 9, 0, 1.0, 0.2), Is.EqualTo(0.05).Within(1e-4));
            Assert.That(AimPick.AxisDistanceSquared(5, 1.1, 0, 1, -0.00001, 0, 12, 0, 1.75, 0.21), Is.EqualTo(0.0441).Within(1e-4));
        }

        // S3: a ray that starts inside a widened cylinder does not pick that body (only t >= 0 counts).
        [Test]
        public void S3_ARayStartingInsideTheCylinderDoesNotPickIt()
        {
            FrameView shown = Shown(Enemy(2, 9f, 0f));
            AimTarget a = Pick(shown, 9f, 1f, 0.5f, 1f, -0.001f, 0f);
            Assert.That(a.Kind, Is.Not.EqualTo(AimKind.Unit));
        }

        // S3e: a reach without body sizes is a wiring mistake.
        [Test]
        public void S3e_AReachWithoutBodySizesThrows()
        {
            FrameView shown = Shown(Enemy(2, 9f, 0f));
            var e = Assert.Throws<ArgumentException>(() =>
                AimPick.PickRay(5f, 1f, 0f, 1f, -0.001f, 0f, 0f, null, shown, 1, null, null, null, Reach));
            Assert.That(e.Message, Does.Contain("pick needs body sizes"));
            Assert.DoesNotThrow(() => AimPick.PickRay(5f, 1f, 0f, 1f, -0.001f, 0f, 0f, null, shown, 1, null, null, null, null));
        }

        // S4 (the case this card is for): an enemy on the far edge of the demo house's first floor (3 m, cells x 9..14) at
        // x 14.7; a 2-in-1 ray from the west passes 0.6 m beside his head and lands on the ground behind the house. The old
        // pick (0.5 m) misses him and aims at the ground; the reach picks him with his floor's height.
        [Test]
        public void S4_ANearMissOnAnUpperFloorPicksTheEnemyAtHisHeight()
        {
            var house = House();
            ViewGround ground = ViewGround.For(house.Map);
            FrameView shown = Shown(Enemy(2, 14.7f, 8f, y: 3f, layer: 2));
            float ox = 14.7f - 20f, oy = 4.6f + 10f, oz = 8.6f;
            AimTarget old = AimPick.PickRay(ox, oy, oz, 1f, -0.5f, 0f, 0f, ground, shown, 1, house.Plates, null, Sizes);
            Assert.That(old.Kind, Is.Not.EqualTo(AimKind.Unit));
            Assert.That(old.Y, Is.LessThan(0.5f), "the old pick lands on the ground behind the house");
            AimTarget a = Pick(shown, ox, oy, oz, 1f, -0.5f, 0f, ground, house.Plates);
            Assert.That(a.Kind, Is.EqualTo(AimKind.Unit));
            Assert.That(a.UnitKey, Is.EqualTo(2UL));
            Assert.That(a.YMm, Is.EqualTo(3000L));
        }

        // A ridge 6 m high at x 12 (slope 1), 24 x 8 m, as the A4 fixture builds its height fields.
        private static ViewGround HighRidge() => ViewGround.For(ViewMap.Parse(WcMapWriter.Write(Wp23.Make(24, 8, 0, 12 * A4.S,
            Array.Empty<(int, int, int, int)>(), 2 * A4.S, (i, j) => i >= 6 && i <= 18 ? (int)((6 - Math.Abs(i - 12)) * A4.S) : 0, A4.Names,
            (x, z) => 0))));

        // S4 on a height field: an enemy on the 6 m ridge top; a 45 degree ray passes 0.6 m beside his head and lands beyond the
        // ridge. The reach picks him at the height under his feet.
        [Test]
        public void S4_ANearMissOnARidgePicksTheEnemyAtTheGroundUnderHim()
        {
            ViewGround ground = HighRidge();
            FrameView shown = Shown(Enemy(2, 12f, 4f, y: 6f));
            float ox = 12f - 10f, oy = 7.6f + 10f, oz = 4.6f;
            AimTarget old = AimPick.PickRay(ox, oy, oz, 1f, -1f, 0f, 0f, ground, shown, 1, null, null, Sizes);
            Assert.That(old.Kind, Is.Not.EqualTo(AimKind.Unit));
            Assert.That(old.Y, Is.LessThan(0.5f), "the old pick lands beyond the ridge");
            AimTarget a = Pick(shown, ox, oy, oz, 1f, -1f, 0f, ground);
            Assert.That(a.Kind, Is.EqualTo(AimKind.Unit));
            Assert.That(a.YMm, Is.EqualTo(6000L).Within(1L));
        }

        // S4 counter-example: an enemy behind the ridge, the ray hits the near slope (x 10, 4 m) first: no pick, although the
        // ray, carried on through the hill, would run through his widened cylinder.
        [Test]
        public void S4_AnEnemyBehindTheRidgeIsNotPicked()
        {
            ViewGround ground = HighRidge();
            FrameView shown = Shown(Enemy(2, 17f, 4f, y: 1f));
            AimTarget a = Pick(shown, 0f, 8f, 4f, 1f, -0.4f, 0f, ground);
            Assert.That(a.Kind, Is.EqualTo(AimKind.Ground));
            Assert.That(a.X, Is.EqualTo(10f).Within(0.05f));
            Assert.That(a.YMm, Is.EqualTo(4000L).Within(50L));
        }

        // S4: one enemy on the near slope beside the ray (hit before the ground) and one behind the ridge right on it: the
        // hidden one is left out before the nearest is chosen, so the visible one is picked.
        [Test]
        public void S4_AHiddenEnemyDoesNotTakeThePickFromAVisibleOne()
        {
            ViewGround ground = HighRidge();
            FrameView shown = Shown(Enemy(2, 8.5f, 4.5f, y: 2.5f), Enemy(3, 17f, 4f, y: 1f));
            AimTarget a = Pick(shown, 0f, 8f, 4f, 1f, -0.4f, 0f, ground);
            Assert.That(Snapped(a), Is.EqualTo(2UL));
            Assert.That(a.YMm, Is.EqualTo(2500L));
        }
    }
}
