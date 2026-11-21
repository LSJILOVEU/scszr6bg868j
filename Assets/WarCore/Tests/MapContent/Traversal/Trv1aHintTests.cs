using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using static WarCore.Tests.Trv1aMaps;

namespace WarCore.Tests
{
    // TRV-1a: door hints (T1-14, T1-15).
    public sealed class Trv1aHintTests
    {
        // A bare 3 x 2 floor at x 5..7, z 3..4, top g, on flat ground: its ten edge sides face walkable ground.
        private static MapDesc Bare(long g) => Flat(16, 10, Ml4Maps.Flat(1, 5, 3, 3, 2, g));

        private static string Line(MapHint h) => h.CellX + "," + h.CellZ + "," + h.Side;

        // T1-14: nothing on the ground reaches the floor; the order is z, x, plate, side (PlusX, MinusX, PlusZ, MinusZ).
        [TestCase(Trv1aMaps.Step + 1)]
        [TestCase(58982)]
        [TestCase(98304)]
        public void AFloorTheGroundCannotReachIsNamedSideBySide(long g)
        {
            var hints = Hints(Bare(g));
            Assert.That(hints.Select(Line), Is.EqualTo(new[]
            {
                "5,3,MinusX", "5,3,MinusZ", "6,3,MinusZ", "7,3,PlusX", "7,3,MinusZ",
                "5,4,MinusX", "5,4,PlusZ", "6,4,PlusZ", "7,4,PlusX", "7,4,PlusZ",
            }));
            Assert.That(hints.All(h => h.Code == MapHint.DoorGap && h.Plate == 1 && h.GapRaw == g));
        }

        // 0.3 m is a seam (the floor is reached); above 1.5 m it is a storey, not a doorway.
        [TestCase(Trv1aMaps.Step)]
        [TestCase(98305)]
        public void OutsideTheBandNothingIsNamed(long g)
        {
            Assert.That(Hints(Bare(g)), Is.Empty);
            Assert.That(MapGate.HintMaxGapRaw, Is.EqualTo(98304));
        }

        // A wall on the ground cells west of the floor closes that side: two hints fewer.
        [Test]
        public void OnlyWalkableGroundCounts()
        {
            MapDesc d = Ml4Maps.Make(0, 16, 10, null, new[] { Ml4Maps.Flat(1, 5, 3, 3, 2, 58982) }, walls: new[] { Ml4Maps.Wall(1, 4, 3, 1, 2, 3 * Trv1aMaps.M) });
            Assert.That(Hints(d).Select(Line), Is.EqualTo(new[]
            {
                "5,3,MinusZ", "6,3,MinusZ", "7,3,PlusX", "7,3,MinusZ", "5,4,PlusZ", "6,4,PlusZ", "7,4,PlusX", "7,4,PlusZ",
            }));
        }

        // A sloped plate off the ground, 39321 -> 78642 over x 5..6 (points 39321, 58981, 78642), z = 3: each side names
        // the larger of its two end gaps.
        [Test]
        public void TheLargerEndGapIsNamed()
        {
            MapDesc d = Flat(16, 10, Ml4Maps.Slope(1, 5, 3, 2, 1, "x", 39321, 78642));
            Assert.That(Hints(d).Select(h => Line(h) + "," + h.GapRaw), Is.EqualTo(new[]
            {
                "5,3,MinusX,39321", "5,3,PlusZ,58981", "5,3,MinusZ,58981", "6,3,PlusX,78642", "6,3,PlusZ,78642", "6,3,MinusZ,78642",
            }));
        }

        // A ramp at the east side joins the floor to the ground: no hint is left (the other sides are reached across the floor).
        [Test]
        public void ARampSilencesTheFloor()
        {
            MapDesc d = Stamp(Bare(58982), Ramp(Bare(58982), 8, 3, 2, MapSide.PlusX, 58982));
            Assert.That(Hints(d), Is.Empty);
        }

        // T1-15: the stair sides of the demo houses face walkable ground with drops in the band, but the stairs are reached.
        [Test]
        public void TheDemoHousesHaveNoHints()
        {
            Assert.That(MapGate.Hints(PlateDemoMap.DescBytes(), RwrPack.Load(PlateDemoMap.Desc().Pack)), Is.Empty);
            Assert.That(MapGate.Hints(PlateAssaultMap.DescBytes(), RwrPack.Load(PlateAssaultMap.Desc().Pack)), Is.Empty);
        }

        // Hints stay out of Check: the bare floor's gate result has no hint in it.
        [Test]
        public void CheckDoesNotCarryHints() =>
            Assert.That(Ml4Maps.Gate(Bare(58982)).Any(p => p.Code.StartsWith("HINT", StringComparison.Ordinal)), Is.False);

        [Test]
        public void WhatCheckReportsGivesNoHints()
        {
            Assert.That(Ok(() => MapGate.Hints(Encoding.UTF8.GetBytes("{"), Pack)), Is.Empty, "does not read");
            Assert.That(Ok(() => Hints(Ml4Maps.Buried())), Is.Empty, "does not bake");
            Assert.That(Ml4Maps.Gate(Ml4Maps.Buried()), Is.Not.Empty);
            Assert.That(Ok(() => Hints(Flat(16, 10))), Is.Empty, "no plates");
            Assert.Throws<ArgumentException>(() => MapGate.Hints(Ml4Maps.Bytes(Bare(58982)), RwrPack.Load("rwr")));
            Assert.Throws<ArgumentNullException>(() => MapGate.Hints(null, Pack));
            Assert.Throws<ArgumentNullException>(() => MapGate.Hints(Ml4Maps.Bytes(Bare(58982)), null));
        }

        // Flat ground at -2^29 reads and passes Check, but the walking graph refuses it: no hints instead of a throw.
        [Test]
        public void GroundTheWalkingGraphRefusesGivesNoHints()
        {
            MapDesc d = Ml4Maps.Make(-536870912, 3, 3, null, new[] { Ml4Maps.Flat(1, 1, 1, 1, 1, -536811930, 32768) }, ceilingRaw: -536805376);
            Assert.That(Ml4Maps.Gate(d), Is.Empty);
            Assert.That(Ok(() => Hints(d)), Is.Empty);
        }
    }
}
