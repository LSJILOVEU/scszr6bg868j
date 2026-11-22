using System;
using NUnit.Framework;
using WarCore.Combat;

namespace WarCore.Tests.Aim1
{
    // AIM-1 A2: choosing the band of one step. threshold = floor(aboveMmPerSec * 65536 / 30000) raw per tick; a step of
    // (dx, dz) raw is in a band when dx^2 + dz^2 > threshold^2 strictly; the last such band wins; none gives 1000.
    public sealed class Aim1BandTests
    {
        private static MoveSpreadTable Default() => new MoveSpreadTable(new[] { 500, 4000 }, new[] { 1500, 3000 });

        [Test]
        public void A2_ThresholdsAreFlooredRawPerTick()
        {
            MoveSpreadTable t = Default();
            Assert.That(t.ThresholdRaw(0), Is.EqualTo(1092), "500 * 65536 / 30000 = 1092.26");
            Assert.That(t.ThresholdRaw(1), Is.EqualTo(8738), "4000 * 65536 / 30000 = 8738.13");
            Assert.That(new MoveSpreadTable(new[] { 300000 }, new[] { 1000 }).ThresholdRaw(0), Is.EqualTo(655360));
            Assert.That(new MoveSpreadTable(new[] { 1 }, new[] { 1000 }).ThresholdRaw(0), Is.EqualTo(2), "65536 / 30000 = 2.18");
        }

        [Test]
        public void A2_BandEdgesAreStrict()
        {
            MoveSpreadTable t = Default();
            Assert.That(t.PermilleFor(1092, 0), Is.EqualTo(1000));
            Assert.That(t.PermilleFor(1093, 0), Is.EqualTo(1500));
            Assert.That(t.PermilleFor(8738, 0), Is.EqualTo(1500));
            Assert.That(t.PermilleFor(8739, 0), Is.EqualTo(3000), "the last band the step exceeds, not the first");
            Assert.That(t.PermilleFor(0, 0), Is.EqualTo(1000));
            Assert.That(t.PermilleFor(-1093, 0), Is.EqualTo(1500), "a step towards -x");
        }

        [Test]
        public void A2_BothHorizontalComponentsCount()
        {
            MoveSpreadTable t = Default();
            Assert.That(t.PermilleFor(0, 1092), Is.EqualTo(1000));
            Assert.That(t.PermilleFor(0, 1093), Is.EqualTo(1500), "z only");
            Assert.That(t.PermilleFor(0, -8739), Is.EqualTo(3000), "z only, towards -z");
            // Diagonal: 773^2 + 773^2 = 1195058 > 1092^2 = 1192464, although each component alone is below 1092.
            Assert.That(t.PermilleFor(773, 773), Is.EqualTo(1500));
            // 772^2 * 2 = 1191968 <= 1192464: Euclidean, not Manhattan (772 + 772 = 1544 > 1092).
            Assert.That(t.PermilleFor(772, 772), Is.EqualTo(1000));
        }

        [Test]
        public void A2_ZeroThresholdTakesAnyMovementButNotStanding()
        {
            var t = new MoveSpreadTable(new[] { 0, 4000 }, new[] { 1200, 3000 });
            Assert.That(t.ThresholdRaw(0), Is.Zero);
            Assert.That(t.PermilleFor(0, 0), Is.EqualTo(1000), "standing is not moving");
            Assert.That(t.PermilleFor(1, 0), Is.EqualTo(1200));
            Assert.That(t.PermilleFor(0, -1), Is.EqualTo(1200));
        }

        [Test]
        public void A2_FarStepsTakeTheTopBandWithoutSquaring()
        {
            MoveSpreadTable t = Default();
            const long far = MoveSpreadTable.FarRaw;
            Assert.That(far, Is.EqualTo(1L << 24));
            Assert.That(t.PermilleFor(far + 1, 0), Is.EqualTo(3000));
            Assert.That(t.PermilleFor(-far - 1, 0), Is.EqualTo(3000));
            Assert.That(t.PermilleFor(0, far + 1), Is.EqualTo(3000));
            Assert.That(t.PermilleFor(0, -far - 1), Is.EqualTo(3000));
            Assert.That(t.PermilleFor(long.MaxValue, long.MinValue), Is.EqualTo(3000), "no overflow");
            // z alone far: long.MaxValue squared wraps to 1 and long.MinValue squared to 0, so only the guard gives the top band.
            Assert.That(t.PermilleFor(0, long.MaxValue), Is.EqualTo(3000), "no overflow on z");
            Assert.That(t.PermilleFor(0, long.MinValue), Is.EqualTo(3000), "no overflow on z, towards -z");
            Assert.That(t.PermilleFor(far, far), Is.EqualTo(3000), "2^24 on both axes is squared: 2^49 fits");
            // The top band of a one-band table whose threshold is above 256 m per tick is impossible, so the guard picks it.
            var one = new MoveSpreadTable(new[] { 300000 }, new[] { 2000 });
            Assert.That(one.PermilleFor(far + 1, 0), Is.EqualTo(2000));
            Assert.That(one.PermilleFor(655360, 0), Is.EqualTo(1000));
            Assert.That(one.PermilleFor(655361, 0), Is.EqualTo(2000));
        }

        [Test]
        public void A2_TableRules()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new int[0], new int[0]), "no band");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new int[9] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, new int[9] { 1, 1, 1, 1, 1, 1, 1, 1, 1 }), "9 bands");
            Assert.DoesNotThrow(() => new MoveSpreadTable(new int[8] { 0, 1, 2, 3, 4, 5, 6, 300000 }, new int[8] { 1, 1, 1, 1, 1, 1, 1, 4000 }), "8 bands");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new[] { 500, 500 }, new[] { 1500, 3000 }), "equal speeds");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new[] { 500, 400 }, new[] { 1500, 3000 }), "decreasing speeds");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new[] { -1 }, new[] { 1500 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new[] { 300001 }, new[] { 1500 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new[] { 500 }, new[] { 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveSpreadTable(new[] { 500 }, new[] { 4001 }));
            Assert.Throws<ArgumentException>(() => new MoveSpreadTable(new[] { 500 }, new[] { 1500, 3000 }), "one permille per band");
            Assert.Throws<ArgumentNullException>(() => new MoveSpreadTable(null, new[] { 1500 }));
            Assert.Throws<ArgumentNullException>(() => new MoveSpreadTable(new[] { 500 }, null));
            var t = Default();
            Assert.That((t.Count, t.AboveMmPerSec(0), t.Permille(0), t.AboveMmPerSec(1), t.Permille(1)), Is.EqualTo((2, 500, 1500, 4000, 3000)));
        }

        [Test]
        public void A2_PerWeaponTablesReplaceTheDefaultWhole()
        {
            MoveSpreadTable d = Default(), own = new MoveSpreadTable(new[] { 500, 4000 }, new[] { 2500, 4000 });
            var spread = new MoveSpread(d, new[] { null, null, own });
            Assert.That(spread.WeaponSlots, Is.EqualTo(3));
            Assert.That(spread.For(0), Is.SameAs(d));
            Assert.That(spread.For(2), Is.SameAs(own));
            Assert.That(spread.For(-1), Is.SameAs(d), "no weapon");
            Assert.That(spread.For(3), Is.SameAs(d));
            Assert.That(new MoveSpread(d).WeaponSlots, Is.Zero);
            Assert.That(new MoveSpread(d).For(2), Is.SameAs(d));
            Assert.Throws<ArgumentNullException>(() => new MoveSpread(null));
        }
    }
}
