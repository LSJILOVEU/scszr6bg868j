using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Ml5Plates;

namespace WarCore.Tests
{
    // ML-5 acceptance 2m, 3, 5 and 6: large coordinates, the domain and the constructor, the bridge from a .wcmap, and no
    // allocation per query.
    public sealed class Ml5FieldTests
    {
        private const long Far = 1L << 32;

        // 2m: a 2/1 plate at the world's edge crossed by segments from +-2^32: C * dx alone is about 2^81.6, beyond a long.
        [Test]
        public void LargeCoordinates()
        {
            long ox = (1L << 30) - 8 * S, oz = (1L << 30) - 4 * S;
            var plates = new[] { AlongX(1, 2, 0, 4, 4, new[] { 0, 2 * S, 4 * S, 6 * S, 8 * S }) };
            PlateField f = Field(8, 4, ox, oz, plates);
            PlateHit h = Hit(f, V(-Far, -Far, -4294530429), V(Far, 2577855986, Far));
            Assert.That(h.PlateId, Is.EqualTo(1UL));
            T(h, 3220832256, 5153522951);
            AgreesWithTheOracle(f, ox, oz, plates, V(-Far, -Far, -4294530429), V(Far, 2577855986, Far));
            AgreesWithTheOracle(f, ox, oz, plates, V(Far, 2577855986, Far), V(-Far, -Far, -4294530429));
            Clear(f, V(-Far, 9 * S, oz + 2 * S), V(Far, 9 * S, oz + 2 * S), "level over the highest top");
            Assert.That(f.Blocks(V(Far, 2577855986, Far), V(-Far, -Far, -4294530429)), Is.True);
        }

        private static void AgreesWithTheOracle(PlateField f, long ox, long oz, PlateSlab[] plates, SimVec3 a, SimVec3 b)
        {
            Ml5Oracle.Answer want = Ml5Oracle.Solve(ox, oz, plates, a.X.Raw, a.Y.Raw, a.Z.Raw, b.X.Raw, b.Y.Raw, b.Z.Raw);
            Assert.That(f.TryFirstThrough(a, b, out PlateHit hit), Is.EqualTo(want.Found));
            Assert.That(want.Found, Is.True);
            Assert.That((hit.PlateId, hit.At), Is.EqualTo((want.Id, V(want.X, want.Y, want.Z))));
            Assert.That(new System.Numerics.BigInteger(hit.TNum) * want.Den, Is.EqualTo(want.Num * hit.TDen));
        }

        // 3: each coordinate at +-2^32 is taken, one raw beyond is refused before any arithmetic.
        [Test]
        public void QueryDomain([Range(0, 5)] int axis, [Values(-1, 1)] int sign)
        {
            PlateField f = M();
            long[] ok = { 0, 0, 0, 0, 0, 0 }, bad = { 0, 0, 0, 0, 0, 0 };
            ok[axis] = sign * Far; bad[axis] = sign * (Far + 1);
            Assert.DoesNotThrow(() => f.Blocks(V(ok[0], ok[1], ok[2]), V(ok[3], ok[4], ok[5])));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.Blocks(V(bad[0], bad[1], bad[2]), V(bad[3], bad[4], bad[5])));
        }

        private static PlateSlab Points(ulong id, int[] points, long thick = Thick) => new PlateSlab(id, 0, 0, 1, 1, points, thick, new[] { true });

        // 3: the constructor's rules, each case breaking one.
        [Test]
        public void ConstructorRules()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(0, 4, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(HeightField.MaxSide + 1, 4, 0, 0));
            Assert.DoesNotThrow(() => Field(HeightField.MaxSide, 4, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(8, 4, S / 2, 0), "origin off the metre");
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(8, 4, 0, -S / 2), "origin off the metre");
            long edge = SimVec3.WorldLimitMetres * S;
            Assert.DoesNotThrow(() => Field(8, 4, edge - 8 * S, -edge));
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(8, 4, edge - 7 * S, 0), "past the world's edge");
            Assert.Throws<ArgumentOutOfRangeException>(() => Field(8, 4, 0, -edge - S), "past the world's edge");
            Assert.Throws<ArgumentException>(() => Field(8, 4, 0, 0, Flat(1, 5, 0, 4, 4)), "leaves the map");
            Assert.Throws<ArgumentException>(() => Field(8, 4, 0, 0, Flat(0, 2, 0, 1, 1)), "id 0");
            Assert.Throws<ArgumentException>(() => Field(8, 4, 0, 0, Flat(2, 2, 0, 1, 1), Flat(2, 4, 0, 1, 1)), "ids must rise");
            Assert.Throws<ArgumentException>(() => Field(8, 4, 0, 0, Flat(1, 2, 0, 1, 1, thick: Thick - 1)), "thinner than 0.5 m");
            Assert.DoesNotThrow(() => Field(8, 4, 0, 0, Flat(1, 2, 0, 1, 1, thick: HeightField.HeightLimitRaw - 1)));
            Assert.Throws<ArgumentException>(() => Field(8, 4, 0, 0, Flat(1, 2, 0, 1, 1, thick: HeightField.HeightLimitRaw)));
            Assert.Throws<ArgumentException>(() => Field(8, 4, 0, 0, Flat(1, 2, 0, 1, 1, top: HeightField.HeightLimitRaw)), "height");
            Assert.Throws<ArgumentException>(() => Field(8, 4, 0, 0, Flat(1, 2, 0, 1, 1, top: -HeightField.HeightLimitRaw)), "height");
            Assert.Throws<ArgumentException>(() => Field(2, 2, 0, 0, Points(1, new[] { 0, 0, 0, 1 })), "four corners not coplanar");
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlateSlab(1, 0, 0, 0, 1, new int[2], Thick, new bool[0]));
            Assert.Throws<ArgumentException>(() => new PlateSlab(1, 0, 0, 1, 1, new int[3], Thick, new[] { true }));
            Assert.Throws<ArgumentException>(() => new PlateSlab(1, 0, 0, 1, 1, new int[4], Thick, new bool[2]));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SlopeUpToTwoToOne(bool alongX)
        {
            PlateSlab Steep(long step) => alongX ? AlongX(1, 0, 0, 2, 1, new[] { 0, step, 2 * step }) : AlongZ(1, 0, 0, 1, 2, new[] { 0, step, 2 * step });
            Assert.DoesNotThrow(() => Field(4, 4, 0, 0, Steep(2 * S)));
            Assert.Throws<ArgumentException>(() => Field(4, 4, 0, 0, Steep(2 * S + 1)));
            Assert.DoesNotThrow(() => Field(4, 4, 0, 0, Steep(-2 * S)));
            Assert.Throws<ArgumentException>(() => Field(4, 4, 0, 0, Steep(-2 * S - 1)));
        }

        [Test]
        public void PlateCounts()
        {
            PlateSlab[] Spread(int n) => Enumerable.Range(0, n).Select(k => Flat((ulong)k + 1, k % 64, k / 64 % 64, 1, 1)).ToArray();
            Assert.That(Field(64, 64, 0, 0, Spread(PlateField.MaxPlates)).Count, Is.EqualTo(4096));
            Assert.Throws<ArgumentException>(() => Field(64, 65, 0, 0, Spread(PlateField.MaxPlates + 1).Select((p, k) => k < 4096 ? p : Flat(4097, 0, 64, 1, 1)).ToArray()));
            PlateSlab[] Stack(int n) => Enumerable.Range(0, n).Select(k => Flat((ulong)k + 1, 0, 0, 1, 1, top: k * S)).ToArray();
            Assert.That(Field(1, 1, 0, 0, Stack(PlateField.MaxPerCell)).Count, Is.EqualTo(64));
            Assert.Throws<ArgumentException>(() => Field(1, 1, 0, 0, Stack(PlateField.MaxPerCell + 1)));
            PlateSlab[] dug = Stack(PlateField.MaxPerCell + 1);
            dug[64] = Flat(65, 0, 0, 1, 1, top: 64 * S, holes: (0, 0));
            Assert.That(Field(1, 1, 0, 0, dug).Count, Is.EqualTo(65), "a dug cell does not count");
            PlateField none = Field(8, 4, 0, 0);
            Assert.That(none.Count, Is.EqualTo(0));
            Clear(none, V(0, 0, 0), V(524288, 262144, 262144));
        }

        // 5a: the ML-4 demo house through the bridge.
        [Test]
        public void DemoHouseThroughTheBridge()
        {
            PlateField f = WcMapPlateField.Build(Ml4Maps.Bake(Ml4Maps.Demo()));
            Assert.That(f.Count, Is.EqualTo(3));
            PlateHit level = Hit(f, V(327680, 180224, 524288), V(786432, 180224, 524288));
            Assert.That((level.PlateId, level.At.X.Raw), Is.EqualTo((2UL, 589824L)), "outside at 5 m into the house at 12 m, 2.75 m up");
            PlateHit down = Hit(f, V(753664, 458752, 622592), V(753664, 0, 622592));
            Assert.That((down.PlateId, down.At.Y.Raw), Is.EqualTo((3UL, 393216L)), "over the stair hole from above: the roof");
            PlateHit up = Hit(f, V(753664, 6554, 622592), V(753664, 458752, 622592));
            Assert.That((up.PlateId, up.At.Y.Raw), Is.EqualTo((1UL, 90112L)), "from the ground: the stair, the first floor is dug here");
            PlateHit hole = Hit(f, V(753664, 327680, 622592), V(753664, 0, 622592));
            Assert.That((hole.PlateId, hole.At.Y.Raw), Is.EqualTo((1UL, 122880L)), "between floor and roof, down through the hole: the stair");
            PlateHit beside = Hit(f, V(884736, 6554, 622592), V(884736, 458752, 622592));
            Assert.That((beside.PlateId, beside.At.Y.Raw), Is.EqualTo((2UL, 163840L)), "beside the hole: the first floor");
        }

        // 5b: a map whose origin is below zero: the bridge places the plates at Min.X / Min.Z.
        [Test]
        public void BridgeUsesTheMapOrigin()
        {
            var desc = new MapDesc("ml5", "none", "rwr-m6", new MapXZ(8, 4), new MapXZ(-4, -2), 0, 10 * S,
                new[] { new MapLayer("walls", 1) }, null, null, new[] { Ml4Maps.Flat(1, 2, 0, 4, 4, Top, Thick) }, new MapCutout[0], new MapWall[0],
                new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(41, "none"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(1, 1, 1, 1, 2, 1));
            WcMap map = Ml4Maps.Bake(desc);
            Assert.That((map.Min.X.Raw, map.Min.Z.Raw), Is.EqualTo((-262144L, -131072L)));
            PlateHit h = Hit(WcMapPlateField.Build(map), V(-229376, 180224, -32768), V(229383, 180227, -32773));
            T(h, 98304, 458759);
            Assert.That((h.At, h.PlateId), Is.EqualTo((V(-131072, 180224, -32770), 1UL)));
        }

        // 5c: no plates, no field; the limits are ML-4's.
        [Test]
        public void BridgeWithoutPlatesAndTheLimits()
        {
            Assert.That(WcMapPlateField.Build(Ml4Maps.Bake(Ml4Maps.Make(8, 4))), Is.Null);
            Assert.That((PlateField.MaxPlates, PlateField.MaxPerCell), Is.EqualTo((PlateSpan.MaxPlates, PlateSpan.MaxPerCell)));
        }
    }
}
