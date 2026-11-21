using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3a acceptance 6 and 9: order, seeds, no leftovers, the range clamp, surface order, preconditions and errors.
    public sealed class Ed3ComposeTests
    {
        private const string Line = "[[2, 10], [38, 12]]";

        private static string Mixed(ulong seed) => Gen(40, 40, Join(Hills(6, 5, 5, 35, 35, 3, 9, 500, 4000), Ridge(Line, 3, 2000),
            Patch(20, 20, 6, "mud", 300), River("[[0, 30], [40, 28]]", 2, 1500, "mud"), Road("[[5, 5], [35, 6]]", 2)), seed);

        [Test]
        public void PlanCoversEveryPointAndCellInOrder()
        {
            MapStampPlan plan = Compose(Map(5, 3), Gen(5, 3, Hill(2, 1, 3, 1000)));
            Assert.That(plan.Instance, Is.EqualTo(MapNew.TerrainInstance));
            Assert.That(plan.Heights.Count, Is.EqualTo(6 * 4));
            Assert.That(plan.Surfaces.Count, Is.EqualTo(5 * 3));
            for (int n = 0; n < plan.Heights.Count; n++)
            {
                Assert.That(plan.Heights[n].I, Is.EqualTo(n % 6));
                Assert.That(plan.Heights[n].J, Is.EqualTo(n / 6));
            }
            for (int n = 0; n < plan.Surfaces.Count; n++)
            {
                Assert.That(plan.Surfaces[n].X, Is.EqualTo(n % 5));
                Assert.That(plan.Surfaces[n].Z, Is.EqualTo(n / 5));
                Assert.That(plan.Surfaces[n].Name, Is.EqualTo("grass"));
            }
            Assert.That(plan.Walls.Count + plan.Points.Count + plan.Bases.Count + plan.Units.Count, Is.EqualTo(0));
        }

        [Test]
        public void SameSpecTwiceGivesTheSameBytes()
        {
            MapDesc d = Map(40, 40);
            MapStampPlan a = Compose(d, Mixed(3)), b = Compose(d, Mixed(3));
            for (int n = 0; n < a.Heights.Count; n++) Assert.That(a.Heights[n].Raw, Is.EqualTo(b.Heights[n].Raw));
            for (int n = 0; n < a.Surfaces.Count; n++) Assert.That(a.Surfaces[n].Name, Is.EqualTo(b.Surfaces[n].Name));
            Assert.That(MapDescJson.Write(Apply(d, a)), Is.EqualTo(MapDescJson.Write(Apply(d, b))));
            Assert.That(MapDescJson.Write(Apply(d, a)), Is.Not.EqualTo(MapDescJson.Write(Run(d, Mixed(4)))));
        }

        [Test]
        public void HillAndMesaOrderMatters()
        {
            MapDesc d = Map(40, 40);
            MapDesc hillFirst = Run(d, Gen(40, 40, Join(Hill(20, 20, 40, 10000), Mesa(20, 20, 5, 100))));
            MapDesc mesaFirst = Run(d, Gen(40, 40, Join(Mesa(20, 20, 5, 100), Hill(20, 20, 40, 10000))));
            Assert.That(H(hillFirst, 20, 20), Is.EqualTo(661913));
            Assert.That(H(mesaFirst, 20, 20), Is.EqualTo(661913));
            Assert.That(H(hillFirst, 22, 20), Is.EqualTo(661913));
            Assert.That(H(mesaFirst, 22, 20), Is.EqualTo(658073));
        }

        [Test]
        public void HillsPickTheirPlacesFromTheSeed()
        {
            MapDesc d = Map(40, 40);
            MapDesc r = Run(d, Gen(40, 40, Hills(1, 0, 10, 40, 30, 2, 8, 1000, 5000), seed: 9));
            long x = MapNoise.Pick(9, 0, 0, 0, 0, 40), z = MapNoise.Pick(9, 0, 0, 1, 10, 30);
            int mm = (int)MapNoise.Pick(9, 0, 0, 3, 1000, 5000);
            Assert.That(H(r, (int)x, (int)z), Is.EqualTo(Raw(mm)));
            // Values from the independent script (log): Pick(9, 0, 0, 0..3) = 6, 10, 7, 3962.
            Assert.That(x, Is.EqualTo(6)); Assert.That(z, Is.EqualTo(10)); Assert.That(mm, Is.EqualTo(3962));
        }

        [Test]
        public void SeedARunThenSeedBLeavesNothingOfA()
        {
            MapDesc d = Map(40, 40);
            MapDesc direct = Run(d, Mixed(22));
            MapDesc twice = Run(Run(d, Mixed(21)), Mixed(22));
            Assert.That(MapDescJson.Write(twice), Is.EqualTo(MapDescJson.Write(direct)));
            Assert.That(MapDescJson.Write(Run(d, Mixed(21))), Is.Not.EqualTo(MapDescJson.Write(direct)));
        }

        [Test]
        public void HeightsAreClampedToTheRange()
        {
            // Base below the floor: the flat map sits on the floor.
            MapDesc floor = Run(Map(8, 8, floor: 65536, ceiling: 655360), Gen(8, 8, "", baseMm: "0"));
            Assert.That(H(floor, 3, 3), Is.EqualTo(65536));
            // A ceiling beyond the format's height range: the stack of hills stops at 2^29 - 1.
            var hills = new string[9];
            for (int k = 0; k < hills.Length; k++) hills[k] = Mesa(4, 4, 2, 1000000);
            MapDesc high = Run(Map(8, 8, floor: 0, ceiling: (long)int.MaxValue + 10), Gen(8, 8, Join(hills), baseMm: "1000000"));
            Assert.That(H(high, 4, 4), Is.EqualTo((1 << 29) - 1));
            Assert.That(H(high, 8, 8), Is.EqualTo((1 << 29) - 1 - 6 * L));
            // A floor below the format's range.
            for (int k = 0; k < hills.Length; k++) hills[k] = Mesa(4, 4, 8, -1000000);
            MapDesc low = Run(Map(8, 8, floor: (long)int.MinValue - 10, ceiling: 0), Gen(8, 8, Join(hills), baseMm: "-1000000"));
            Assert.That(H(low, 4, 4), Is.EqualTo(-(1 << 29) + 1));
        }

        [Test]
        public void LaterSurfacesCoverEarlierOnes()
        {
            MapDesc d = Map(20, 20);
            MapDesc patchThenRoad = Run(d, Gen(20, 20, Join(Patch(10, 10, 4, "mud", 0), Road("[[0, 10], [20, 10]]", 1))));
            MapDesc roadThenPatch = Run(d, Gen(20, 20, Join(Road("[[0, 10], [20, 10]]", 1), Patch(10, 10, 4, "mud", 0))));
            Assert.That(Surface(patchThenRoad, 10, 10), Is.EqualTo("road"));
            Assert.That(Surface(roadThenPatch, 10, 10), Is.EqualTo("mud"));
            Assert.That(Surface(patchThenRoad, 10, 12), Is.EqualTo("mud"));
            Assert.That(Surface(patchThenRoad, 1, 1), Is.EqualTo("grass"));
            MapDesc river = Run(d, Gen(20, 20, River("[[10, 0], [10, 20]]", 1, 1000, "mud")));
            Assert.That(Surface(river, 10, 5), Is.EqualTo("mud"));
            Assert.That(Surface(river, 9, 5), Is.EqualTo("mud"));
            Assert.That(Surface(river, 11, 5), Is.EqualTo("grass"));
            Assert.That(H(river, 10, 5), Is.EqualTo(-Raw(1000)));
            Assert.That(H(river, 11, 5), Is.EqualTo(0));
            MapDesc mudGround = Run(d, Gen(20, 20, "", ground: "mud"));
            Assert.That(Surface(mudGround, 3, 3), Is.EqualTo("mud"));
        }

        [Test]
        public void RidgeAndGullyFollowTheBandFalloff()
        {
            MapDesc d = Map(20, 20);
            MapDesc ridge = Run(d, Gen(20, 20, Ridge("[[0, 10], [20, 10]]", 4, 1000)));
            Assert.That(H(ridge, 10, 10), Is.EqualTo(65536));
            Assert.That(H(ridge, 10, 12), Is.EqualTo(65536L * 576 / 1024));
            Assert.That(H(ridge, 10, 14), Is.EqualTo(0));
            MapDesc gully = Run(d, Gen(20, 20, Ridge("[[0, 10], [20, 10]]", 4, -1000)));
            Assert.That(H(gully, 10, 12), Is.EqualTo(-65536L * 576 / 1024));
        }

        private static void Throws<T>(MapDesc d, string gen, string part) where T : Exception
        {
            byte[] before = MapDescJson.Write(d);
            T e = Assert.Throws<T>(() => Compose(d, gen));
            Assert.That(e.Message, Does.Contain(part));
            Assert.That(MapDescJson.Write(d), Is.EqualTo(before));
        }

        [Test]
        public void PreconditionsAndErrors()
        {
            MapDesc noHeights = Map(10, 10, heights: false), noSurfaces = Map(10, 10, surfaces: false), both = Map(10, 10);
            Throws<ArgumentException>(noHeights, Gen(10, 10, Hill(1, 1, 1, 1), baseMm: null), "changes heights");
            Throws<ArgumentException>(noHeights, Gen(10, 10, Road("[[0, 0], [5, 5]]", 1), baseMm: null), "changes heights");
            Throws<ArgumentException>(noHeights, Gen(10, 10, ""), "baseMm");
            Throws<ArgumentException>(noSurfaces, Gen(10, 10, "", ground: "grass"), "ground");
            Throws<ArgumentException>(noSurfaces, Gen(10, 10, Patch(1, 1, 1, "mud", 0), ground: null), "patch");
            Throws<ArgumentException>(both, Gen(10, 10, "", baseMm: null), "baseMm");
            Throws<ArgumentException>(both, Gen(10, 10, "", ground: null), "ground");
            Throws<ArgumentException>(both, Gen(10, 10, Patch(1, 1, 1, "sand", 0)), "sand");
            ArgumentException sand = Assert.Throws<ArgumentException>(() => Compose(both, Gen(10, 10, "", ground: "sand")));
            Assert.That(sand.Message, Does.Contain("sand").And.Contain(Pack));
            Throws<ArgumentException>(both, Gen(10, 10, Road("[[0, 0], [5, 5]]", 1, "sand")), "sand");
            Assert.Throws<ArgumentNullException>(() => MapTerrain.Compose(both, MapGenSpec.Read(Gen(10, 10, "")), null));
            Assert.Throws<ArgumentNullException>(() => MapTerrain.Compose(null, MapGenSpec.Read(Gen(10, 10, "")), Rules()));
            Assert.Throws<ArgumentNullException>(() => MapTerrain.Compose(both, null, Rules()));
            string text = System.Text.Encoding.UTF8.GetString(MapDescJson.Write(Map(10, 10, floor: 0, ceiling: 1)));
            Assert.That(text, Does.Contain("\"floorRaw\": 0,"));
            MapDesc empty = MapDescJson.Read(System.Text.Encoding.UTF8.GetBytes(text.Replace("\"floorRaw\": 0,", "\"floorRaw\": 2,")));
            Throws<ArgumentException>(empty, Gen(10, 10, ""), "range");
            // Accepted mixes: heights only with a river (its surface is not written), surfaces only with a patch.
            MapStampPlan heightsOnly = Compose(noSurfaces, Gen(10, 10, River("[[0, 5], [10, 5]]", 1, 500, "mud"), ground: null));
            Assert.That(heightsOnly.Surfaces.Count, Is.EqualTo(0));
            Assert.That(heightsOnly.Heights.Count, Is.EqualTo(121));
            MapStampPlan surfacesOnly = Compose(noHeights, Gen(10, 10, Patch(5, 5, 2, "mud", 0), baseMm: null));
            Assert.That(surfacesOnly.Heights.Count, Is.EqualTo(0));
            Assert.That(surfacesOnly.Surfaces.Count, Is.EqualTo(100));
            MapStampPlan neither = Compose(Map(10, 10, false, false), Gen(10, 10, "", baseMm: null, ground: null));
            Assert.That(neither.Heights.Count + neither.Surfaces.Count, Is.EqualTo(0));
        }
    }
}
