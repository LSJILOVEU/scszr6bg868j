using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L9 acceptance 1: hand-computed vectors for HeightField.TryFirstBelow. S = 65536 raw per metre; fields are built from
    // grid-point heights h(i, j); the ground inside a cell is the triangle formula of HeightField.cs:66-68 (triangle A for
    // u >= v, B otherwise), floored. A sample is blocked when its exact point is in the closed field and the ground there is
    // strictly above its Y. at is the earliest blocked sample.
    public sealed class L9GroundLineTests
    {
        private const long S = SimFix.Scale;

        private static HeightField Field(int w, int d, int ox, int oz, Func<int, int, long> h)
        {
            var p = new int[(w + 1) * (d + 1)];
            for (int j = 0; j <= d; j++)
                for (int i = 0; i <= w; i++) p[j * (w + 1) + i] = (int)h(i, j);
            return new HeightField(w, d, SimFix.FromInt(ox), SimFix.FromInt(oz), p);
        }

        private static SimVec3 V(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        // Calls inside DoesNotThrow, so a call that throws fails on an assertion rather than with an error.
        private static void Below(HeightField f, SimVec3 from, SimVec3 to, long x, long y, long z)
        {
            bool found = false; SimVec3 at = default;
            Assert.DoesNotThrow(() => { found = f.TryFirstBelow(from, to, out SimVec3 a); at = a; });
            Assert.That(found, Is.True, "blocked");
            Assert.That((at.X.Raw, at.Y.Raw, at.Z.Raw), Is.EqualTo((x, y, z)), "first blocked sample");
        }

        private static void Clear(HeightField f, SimVec3 from, SimVec3 to)
        {
            bool found = true; SimVec3 at = V(1, 1, 1);
            Assert.DoesNotThrow(() => { found = f.TryFirstBelow(from, to, out SimVec3 a); at = a; });
            Assert.That(found, Is.False, "not blocked");
            Assert.That(at, Is.EqualTo(default(SimVec3)));
        }

        // 4 x 1 field, h = 2 m at i = 2. The line y = 1 m at z = 0.5 from x = 0.5 to 3.5.
        private static HeightField Ridge(int ox = 0, int oz = 0) => Field(4, 1, ox, oz, (i, j) => i == 2 ? 2 * S : 0);
        private static HeightField Flat(int w, int d, int ox = 0) => Field(w, d, ox, 0, (i, j) => 0);

        // V1: x = 1 ground 0; diagonal x - z = 1 at x = 1.5, ground (cell 1, u = v = 0.5) = 1 m: touching, not blocked;
        // x = 2 ground 2 m > 1 m: the first blocked sample.
        [Test] public void L9Line_V1RidgeBlocksAtTheGridLine() => Below(Ridge(), V(S / 2, S, S / 2), V(7 * S / 2, S, S / 2), 2 * S, S, S / 2);
        // V1r: reversed, the first blocked sample from the other side is the same grid line x = 2.
        [Test] public void L9Line_V1rReversed() => Below(Ridge(), V(7 * S / 2, S, S / 2), V(S / 2, S, S / 2), 2 * S, S, S / 2);
        // V1n: the same field at origin (-2, -1); the ridge is at x = 0.
        [Test] public void L9Line_V1nNegativeOrigin() => Below(Ridge(-2, -1), V(-3 * S / 2, S, -S / 2), V(3 * S / 2, S, -S / 2), 0, S, -S / 2);

        // V2: 2 x 2 field, h = 2 m where i == j. The line y = 1.5 m from (0.75, 0.25) to (0.25, 0.75) crosses no grid line;
        // it crosses the diagonal x - z = 0 at (0.5, 0.5), where (cell 0, triangle A, h00 = h11 = 2 m, h10 = 0) the ground is
        // 2 m > 1.5 m. The ends lie over ground 1 m.
        [Test] public void L9Line_V2OnlyTheDiagonalBlocks() =>
            Below(Field(2, 2, 0, 0, (i, j) => i == j ? 2 * S : 0), V(3 * S / 4, 3 * S / 2, S / 4), V(S / 4, 3 * S / 2, 3 * S / 4), S / 2, 3 * S / 2, S / 2);

        // V3: 2 x 1, h = -1 m at i = 2. From (0.5 m, 1 raw) to (1.5 m, -2 raw): x = 1 at t = 1/2, Y = 1 + floor(-3/2) = -1;
        // ground at x = 1 is 0 > -1: blocked. (Rounding toward zero would give Y = 0, touching, not blocked.)
        [Test] public void L9Line_V3FloorsTowardNegativeInfinity() =>
            Below(Field(2, 1, 0, 0, (i, j) => i == 2 ? -S : 0), V(S / 2, 1, S / 2), V(3 * S / 2, -2, S / 2), S, -1, S / 2);

        // V6: as V1 but ending at x = 2.5: the end (ground 1 m) and the diagonal x = 1.5 / 2.5 only touch; x = 2 blocks.
        [Test] public void L9Line_V6TheLastGridLineCounts() => Below(Ridge(), V(S / 2, S, S / 2), V(5 * S / 2, S, S / 2), 2 * S, S, S / 2);

        // V7 / V7r: h = 2 m at i = 1 and 3; the first blocked grid line in the direction of travel.
        private static HeightField Twin() => Field(4, 1, 0, 0, (i, j) => i == 1 || i == 3 ? 2 * S : 0);
        [Test] public void L9Line_V7TheNearestRidgeWins() => Below(Twin(), V(S / 2, S, S / 2), V(7 * S / 2, S, S / 2), S, S, S / 2);
        [Test] public void L9Line_V7rTheNearestRidgeWinsReversed() => Below(Twin(), V(7 * S / 2, S, S / 2), V(S / 2, S, S / 2), 3 * S, S, S / 2);

        // V8: the whole segment is off the field (x < 0): never blocked.
        [Test] public void L9Line_V8OffTheField() => Clear(Ridge(), V(-5 * S, S, S / 2), V(-S, 0, S / 2));
        // V9: from off the field into it; x = 2 blocks as in V1.
        [Test] public void L9Line_V9EnteringTheField() => Below(Ridge(), V(-S, S, S / 2), V(5 * S / 2, S, S / 2), 2 * S, S, S / 2);

        // V10: a line lying on a zero field touches everywhere and is never blocked.
        [Test] public void L9Line_V10TouchingIsNotBlocked() => Clear(Flat(4, 1), V(S / 2, 0, S / 2), V(7 * S / 2, 0, S / 2));
        // V10u: down to -1 raw: x = 1 at t = 1/6, Y = floor(-1/6) = -1 < 0: blocked there first.
        [Test] public void L9Line_V10uOneRawBelow() => Below(Flat(4, 1), V(S / 2, 0, S / 2), V(7 * S / 2, -1, S / 2), S, -1, S / 2);

        // E1: 1 x 1 zero field; the line leaves through the far edge x = 1 at t = 1/2 with Y = -0.25 m. The closed field counts
        // that edge: blocked at (1, -0.25, 0.25). (A half-open sample would miss it.)
        [Test] public void L9Line_E1TheFarEdgeIsOnTheField() =>
            Below(Flat(1, 1), V(3 * S / 4, S / 4, S / 4), V(5 * S / 4, -3 * S / 4, S / 4), S, -S / 4, S / 4);

        // E2x: 1 x 1 zero field, raw (65535, 131072, 0) -> (65537, -65536, 87381). The part on the field ends at x = 1 m
        // (t = 1/2, Y = 32768 > 0). The z = 1 m crossing (t ~ 0.75) has exact x ~ 65536.5, off the field; flooring first would
        // pull it onto the edge x = 65536 with Y -16385 and block. Decided on the exact point: not blocked.
        [Test] public void L9Line_E2xAnOffFieldCrossingIsNotPulledBack() => Clear(Flat(1, 1), V(65535, 131072, 0), V(65537, -65536, 87381));
        // E2z: the same with x and z swapped.
        [Test] public void L9Line_E2zAnOffFieldCrossingIsNotPulledBack() => Clear(Flat(1, 1), V(0, 131072, 65535), V(87381, -65536, 65537));
        // E2xr: E2x reversed.
        [Test] public void L9Line_E2xrReversed() => Clear(Flat(1, 1), V(65537, -65536, 87381), V(65535, 131072, 0));

        // V11: 4 x 4 zero field, (0.5 m, 0, 0.75 m) -> (1.5 m, -1 raw, 3.75 m). Blocked samples: z = 1 at t = 1/12 (first),
        // diagonal at t = 3/8, x = 1 at t = 1/2, end at t = 1. at = (32768 + floor(65536/12), -1, 65536) = (38229, -1, 65536).
        [Test] public void L9Line_V11TheEarliestFamilyWins() =>
            Below(Flat(4, 4), V(S / 2, 0, 3 * S / 4), V(3 * S / 2, -1, 15 * S / 4), 38229, -1, S);

        // W1 / W2: a 4 x 1 zero field at x = 16380 m; the segment runs past the world limit (16384 m) to x = 16409.5 m.
        // No world check: W1 stays above the ground (not blocked), W2 goes under it; the diagonal at x = 16380.5 (t = 1/30,
        // Y = 0.5 - 1 = -0.5 m) is the first blocked sample.
        private static HeightField Far() => Flat(4, 1, 16380);
        [Test] public void L9Line_W1PastTheWorldEdgeIsAccepted() => Clear(Far(), V(16379 * S + S / 2, S / 2, S / 2), V(16409 * S + S / 2, S / 2, S / 2));
        [Test] public void L9Line_W2PastTheWorldEdgeBlocks() =>
            Below(Far(), V(16379 * S + S / 2, S / 2, S / 2), V(16409 * S + S / 2, -29 * S - S / 2, S / 2), 1073512448, -S / 2, S / 2);

        // Vv: a vertical segment through a zero field: only the end (Y = -1 m) is blocked.
        [Test] public void L9Line_VvVertical() => Below(Flat(4, 1), V(3 * S / 2, S, S / 2), V(3 * S / 2, -S, S / 2), 3 * S / 2, -S, S / 2);
        // Vz / Vzu: zero length, above and one raw below the ground.
        [Test] public void L9Line_VzZeroLengthAbove() => Clear(Flat(4, 1), V(3 * S / 2, S / 2, S / 2), V(3 * S / 2, S / 2, S / 2));
        [Test] public void L9Line_VzuZeroLengthBelow() => Below(Flat(4, 1), V(3 * S / 2, -1, S / 2), V(3 * S / 2, -1, S / 2), 3 * S / 2, -1, S / 2);

        // Vbig: 512 x 512, h = 2 m at i = 256; the line y = 1 m along x = z is parallel to the diagonals (d = 0), and the x and z
        // families meet at the grid point (256, 256) where the ground is 2 m.
        [Test] public void L9Line_VbigLargestField() =>
            Below(Field(512, 512, 0, 0, (i, j) => i == 256 ? 2 * S : 0), V(S / 2, S, S / 2), V(511 * S + S / 2, S, 511 * S + S / 2), 256 * S, S, 256 * S);

        // Vmin: 1 x 1, h = 2 min(i, j) m. Raw (0, 0, 1) -> (1, 0, 0): the exact midpoint ground is 1 raw > 0, so the exact answer is
        // "blocked"; the diagonal sample floors to (0, 0) where the ground is 0, and the ends are on the ground. Known and
        // deterministic: within a few raw of the ground the answer can differ from the exact one in either direction.
        [Test] public void L9Line_VminRoundingCanMissByARaw() =>
            Clear(Field(1, 1, 0, 0, (i, j) => 2 * (i < j ? i : j) * S), V(0, 0, 1), V(1, 0, 0));

        // Inputs beyond 2^32 raw in any coordinate are rejected; 2^32 itself is accepted.
        [Test]
        public void L9Line_CoordinatesBeyondTwoToThe32AreRejected()
        {
            var f = Flat(4, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => f.TryFirstBelow(V((1L << 32) + 1, 0, S / 2), V(S / 2, 0, S / 2), out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.TryFirstBelow(V(S / 2, 0, S / 2), V(S / 2, -(1L << 32) - 1, S / 2), out _));
            Clear(f, V(1L << 32, 0, S / 2), V(S / 2, 0, S / 2));
        }
    }
}
