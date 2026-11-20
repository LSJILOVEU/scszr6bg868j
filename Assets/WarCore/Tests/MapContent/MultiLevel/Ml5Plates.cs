using System.Linq;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-5 fixtures: plate fields built straight from numbers (raw units, S = 65536).
    internal static class Ml5Plates
    {
        internal const long S = SimFix.Scale;
        internal const long Top = 196608, Thick = 32768, Bottom = Top - Thick;

        internal static SimVec3 V(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        // A flat plate; holes are local cells (x, z) dug out.
        internal static PlateSlab Flat(ulong id, int x, int z, int w, int d, long top = Top, long thick = Thick, params (int, int)[] holes)
        {
            var points = Enumerable.Repeat((int)top, (w + 1) * (d + 1)).ToArray();
            return new PlateSlab(id, x, z, w, d, points, thick, Solid(w, d, holes));
        }

        // A plate sloped along x: alongX[i] is the top of grid line i (i = 0..w), the same on every z line.
        internal static PlateSlab AlongX(ulong id, int x, int z, int w, int d, long[] alongX, long thick = Thick, params (int, int)[] holes)
        {
            var points = new int[(w + 1) * (d + 1)];
            for (int j = 0; j <= d; j++)
                for (int i = 0; i <= w; i++) points[j * (w + 1) + i] = (int)alongX[i];
            return new PlateSlab(id, x, z, w, d, points, thick, Solid(w, d, holes));
        }

        // A plate sloped along z: alongZ[j] is the top of grid line j.
        internal static PlateSlab AlongZ(ulong id, int x, int z, int w, int d, long[] alongZ, long thick = Thick, params (int, int)[] holes)
        {
            var points = new int[(w + 1) * (d + 1)];
            for (int j = 0; j <= d; j++)
                for (int i = 0; i <= w; i++) points[j * (w + 1) + i] = (int)alongZ[j];
            return new PlateSlab(id, x, z, w, d, points, thick, Solid(w, d, holes));
        }

        internal static bool[] Solid(int w, int d, params (int, int)[] holes)
        {
            var solid = Enumerable.Repeat(true, w * d).ToArray();
            foreach ((int hx, int hz) in holes ?? new (int, int)[0]) solid[hz * w + hx] = false;
            return solid;
        }

        internal static PlateField Field(int w, int d, long originX, long originZ, params PlateSlab[] plates) =>
            new PlateField(w, d, SimFix.FromRaw(originX), SimFix.FromRaw(originZ), plates);

        // Fixture M: map 8 x 4 at the origin, plate 1 flat on cells [2,0] size [4,4], top 3 m, 0.5 m thick.
        internal static PlateField M(params (int, int)[] holes) => Field(8, 4, 0, 0, Flat(1, 2, 0, 4, 4, holes: holes));

        internal static PlateHit Hit(PlateField f, SimVec3 from, SimVec3 to)
        {
            Assert.That(f.TryFirstThrough(from, to, out PlateHit hit), Is.True, "blocked");
            Assert.That(f.Blocks(from, to), Is.True);
            return hit;
        }

        internal static void Clear(PlateField f, SimVec3 from, SimVec3 to, string because = null)
        {
            Assert.That(f.TryFirstThrough(from, to, out PlateHit hit), Is.False, because);
            Assert.That(f.Blocks(from, to), Is.False);
            Assert.That((hit.At, hit.PlateId, hit.TNum, hit.TDen), Is.EqualTo((default(SimVec3), 0UL, 0L, 1L)), "nothing hit");
        }

        // t* equals num / den (cross multiplied; TDen > 0 and 0 <= TNum <= TDen).
        internal static void T(PlateHit hit, long num, long den)
        {
            Assert.That(hit.TDen, Is.GreaterThan(0));
            Assert.That(hit.TNum, Is.InRange(0, hit.TDen));
            Assert.That(new System.Numerics.BigInteger(hit.TNum) * den, Is.EqualTo(new System.Numerics.BigInteger(num) * hit.TDen), "t*");
        }
    }
}
