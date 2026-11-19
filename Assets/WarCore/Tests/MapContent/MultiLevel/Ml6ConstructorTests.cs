using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-6 acceptance 3 (constructor rules, one broken rule per case) and 7 (Digest).
    public sealed class Ml6ConstructorTests
    {
        private static PlateSlab One(ulong id, int x, int z, long top = 0) => Ml5Plates.Flat(id, x, z, 1, 1, top, 32768);

        private static LayeredNav Build(TerrainTables ground, long flat, PlateSlab[] plates, byte[][] costs = null, ushort[][] speeds = null)
        {
            costs = costs ?? plates.Select(p => Costs(p)).ToArray();
            speeds = speeds ?? costs.Select(Speeds).ToArray();
            return new LayeredNav(ground, flat, plates, costs, speeds);
        }

        private static string Refused<T>(TerrainTables ground, long flat, PlateSlab[] plates, byte[][] costs = null, ushort[][] speeds = null)
            where T : Exception => Assert.Throws<T>(() => Build(ground, flat, plates, costs, speeds)).Message;

        [Test]
        public void PlateCounts()
        {
            var many = Enumerable.Range(0, 4097).Select(k => One((ulong)k + 1, k % 64, k / 64 % 64)).ToArray();
            StringAssert.StartsWith("More than 4096 plates.", Refused<ArgumentException>(Open(64, 64), 0, many));
            Assert.DoesNotThrow(() => Build(Open(64, 64), 0, many.Take(4096).ToArray()));

            var stack = Enumerable.Range(0, 65).Select(k => One((ulong)k + 1, 0, 0, k * 200000)).ToArray();
            StringAssert.StartsWith("More than 64 plates in one cell.", Refused<ArgumentException>(Open(9, 9), 0, stack));
            Assert.DoesNotThrow(() => Build(Open(9, 9), 0, stack.Take(64).ToArray()));

            var three = new[] { One(1, 0, 0), One(2, 1, 0), One(3, 0, 0, 200000) };
            StringAssert.StartsWith("More plate cells than ground cells.", Refused<ArgumentException>(Open(2, 1), 0, three));
            Assert.That(Build(Open(2, 1), 0, three.Take(2).ToArray()).Count, Is.EqualTo(4));
        }

        [Test]
        public void IdsAndPlace()
        {
            StringAssert.StartsWith("Plate ids must rise from 1.", Refused<ArgumentException>(Open(2, 1), 0, new[] { One(2, 0, 0), One(1, 1, 0) }));
            StringAssert.StartsWith("Plate ids must rise from 1.", Refused<ArgumentException>(Open(2, 1), 0, new[] { One(0, 0, 0) }));
            StringAssert.StartsWith("Plate ids must rise from 1.", Refused<ArgumentException>(Open(2, 1), 0, new[] { One(1, 0, 0), One(1, 1, 0) }));
            Assert.DoesNotThrow(() => Build(Open(2, 1), 0, new[] { One(1, 0, 0), One(5, 1, 0) }));
            StringAssert.StartsWith("Plate 1 leaves the map.", Refused<ArgumentException>(Open(2, 1), 0, new[] { Ml5Plates.Flat(1, 1, 0, 2, 1, 0, 32768) }));
            StringAssert.StartsWith("Plate 1 leaves the map.", Refused<ArgumentException>(Open(2, 1), 0, new[] { One(1, 0, 1) }));
            StringAssert.StartsWith("Plate 1 leaves the map.", Refused<ArgumentException>(Open(2, 1), 0, new[] { One(1, -1, 0) }));
            Assert.DoesNotThrow(() => Build(Open(2, 1), 0, new[] { Ml5Plates.Flat(1, 0, 0, 2, 1, 0, 32768) }));
        }

        [Test]
        public void Tables()
        {
            PlateSlab p = Ml5Plates.Flat(1, 0, 0, 2, 1, 0, 32768, (1, 0));
            var plates = new[] { p };
            StringAssert.StartsWith("One cost and one speed table per plate.",
                Refused<ArgumentException>(Open(2, 1), 0, plates, new byte[0][], new ushort[0][]));
            StringAssert.StartsWith("Plate 1 table size.", Refused<ArgumentException>(Open(2, 1), 0, plates, new[] { new byte[] { 1 } }, new[] { new ushort[] { 1000, 0 } }));
            StringAssert.StartsWith("Plate 1 table size.", Refused<ArgumentException>(Open(2, 1), 0, plates, new[] { new byte[] { 1, 0 } }, new[] { new ushort[] { 1000, 0, 0 } }));
            StringAssert.StartsWith("Plate 1: a cell that is not solid is walkable.",
                Refused<ArgumentException>(Open(2, 1), 0, plates, new[] { new byte[] { 1, 1 } }, new[] { new ushort[] { 1000, 1000 } }));
            StringAssert.StartsWith("Plate 1: cost and speed must be zero together.",
                Refused<ArgumentException>(Open(2, 1), 0, plates, new[] { new byte[] { 0, 0 } }, new[] { new ushort[] { 1000, 0 } }));
            StringAssert.StartsWith("Plate 1: cost and speed must be zero together.",
                Refused<ArgumentException>(Open(2, 1), 0, plates, new[] { new byte[] { 1, 0 } }, new[] { new ushort[] { 0, 0 } }));
            StringAssert.StartsWith("Plate 1: speed above 2000.",
                Refused<ArgumentException>(Open(2, 1), 0, plates, new[] { new byte[] { 1, 0 } }, new[] { new ushort[] { 2001, 0 } }));
            Assert.DoesNotThrow(() => Build(Open(2, 1), 0, plates, new[] { new byte[] { 255, 0 } }, new[] { new ushort[] { 2000, 0 } }));
        }

        [Test]
        public void Heights()
        {
            const int limit = 1 << 29;
            foreach (int bad in new[] { limit, -limit })
                StringAssert.StartsWith("Plate 1 height.", Refused<ArgumentException>(Open(2, 1), 0, new[] { Ml5Plates.AlongX(1, 0, 0, 1, 1, new long[] { 0, bad }) }));
            foreach (int good in new[] { limit - 1, -limit + 1 })
                Assert.DoesNotThrow(() => Build(Open(2, 1), 0, new[] { Ml5Plates.AlongX(1, 0, 0, 1, 1, new long[] { 0, good }) }));
            foreach (long bad in new[] { (long)limit, -(long)limit })
                Assert.Throws<ArgumentOutOfRangeException>(() => Build(Open(2, 1), bad, new PlateSlab[0]));
            foreach (long good in new[] { limit - 1L, 1L - limit })
                Assert.DoesNotThrow(() => Build(Open(2, 1), good, new PlateSlab[0]));
            TerrainTables field = Ground(2, 1, new byte[] { 1, 1 }, new int[6]);
            Assert.DoesNotThrow(() => Build(field, limit, new PlateSlab[0]), "ignored with a height field");
        }

        [Test]
        public void ZeroPlatesAndNulls()
        {
            LayeredNav nav = Build(Open(3, 2), 0, new PlateSlab[0]);
            Assert.That((nav.Count, nav.GroundCount, nav.LayerCount), Is.EqualTo((6, 6, 1)));
            for (int node = 0; node < 6; node++) Assert.That(nav.SeamCount(node), Is.Zero);
            Assert.Throws<ArgumentNullException>(() => new LayeredNav(null, 0, new PlateSlab[0], new byte[0][], new ushort[0][]));
            Assert.Throws<ArgumentNullException>(() => new LayeredNav(Open(2, 1), 0, null, new byte[0][], new ushort[0][]));
            Assert.Throws<ArgumentNullException>(() => new LayeredNav(Open(2, 1), 0, new PlateSlab[0], null, new ushort[0][]));
            Assert.Throws<ArgumentNullException>(() => new LayeredNav(Open(2, 1), 0, new PlateSlab[0], new byte[0][], null));
            Assert.Throws<ArgumentNullException>(() => new LayeredNav(Open(2, 1), 0, new PlateSlab[] { null }, new byte[1][], new ushort[1][]));
            Assert.That((LayeredNav.MaxPlates, LayeredNav.MaxPerCell), Is.EqualTo((PlateField.MaxPlates, PlateField.MaxPerCell)));
        }

        // 7: the same input gives the same Digest; each single change below gives another.
        [Test]
        public void DigestFollowsEveryInput()
        {
            PlateSlab a = One(1, 0, 0, 19660), b = One(2, 2, 1, 5000);
            ulong Make(TerrainTables g, long flat, PlateSlab[] ps, byte[][] cs = null, ushort[][] ss = null) => Build(g, flat, ps, cs, ss).Digest;
            ulong baseline = Make(Open(3, 2), 7, new[] { a, b });
            Assert.That(Make(Open(3, 2), 7, new[] { a, b }), Is.EqualTo(baseline), "same input");
            var changed = new[]
            {
                ("ground tables", Make(Open(3, 2, (2, 0)), 7, new[] { a, b })),
                ("flat ground height", Make(Open(3, 2), 8, new[] { a, b })),
                ("a top point", Make(Open(3, 2), 7, new[] { One(1, 0, 0, 19661), b })),
                ("a solid bit", Make(Open(3, 2), 7, new[] { a, Ml5Plates.Flat(2, 2, 1, 1, 1, 5000, 32768, (0, 0)) })),
                ("a cost", Make(Open(3, 2), 7, new[] { a, b }, new[] { new byte[] { 1 }, new byte[] { 2 } })),
                ("a speed", Make(Open(3, 2), 7, new[] { a, b }, new[] { new byte[] { 1 }, new byte[] { 1 } }, new[] { new ushort[] { 1000 }, new ushort[] { 999 } })),
                ("plate order", Make(Open(3, 2), 7, new[] { One(1, 2, 1, 5000), One(2, 0, 0, 19660) })),
            };
            foreach ((string what, ulong digest) in changed) Assert.That(digest, Is.Not.EqualTo(baseline), what);
            Assert.That(changed.Select(c => c.Item2).Distinct().Count(), Is.EqualTo(changed.Length));

            var heights = new int[12];
            TerrainTables field = Ground(3, 2, Costs(3, 2, 1), heights);
            Assert.That(Make(field, 7, new[] { a, b }), Is.EqualTo(Make(field, 8, new[] { a, b })), "the flat height is ignored with a height field");
            Assert.That(Make(field, 7, new[] { a, b }), Is.Not.EqualTo(baseline), "a height field is another ground");
        }
    }
}
