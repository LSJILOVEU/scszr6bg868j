using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests.Perception
{
    // Acceptance 2-7, 10a, 11 and the overflow bound: ConcealedSight called directly. The shooter (unit 0) stands at
    // x = 1 m, z = 20.5 m on a 40 x 40 grid; cells with x >= 5 are grass (or the factor given), the rest have no cover.
    // Distances are along +X, so the horizontal distance is exactly the X offset in raw units.
    public sealed class A2SelectTests
    {
        private const long S = SimFix.Scale, X0 = S, Z0 = 20 * S + S / 2;

        private static ConcealedSight Covered(ITargetSelector inner, short grass, int edge = 5) =>
            new ConcealedSight(inner, A2.Grid(40, 40), A2.Cover(40, 40, (x, z) => x >= edge ? grass : (short)1000));

        private static (bool, int) At(ITargetSelector selector, long offsetRaw, SimFix range)
        {
            M5TestWorld w = A2.Direct(1);
            A2.Place(w, 0, X0, Z0);
            A2.Place(w, 1, X0 + offsetRaw, Z0);
            return A2.Pick(w, selector, range);
        }

        // 2: sight-limited, Sight 20 m, range 30 m, so L = 20 m.
        [TestCase(500, 655360, true)]
        [TestCase(500, 655361, false)]
        [TestCase(1000, 1310720, true)]
        [TestCase(1000, 1310721, false)]
        [TestCase(333, 436469, true)]
        [TestCase(333, 436470, false)]
        public void Boundary_SightLimited(int k, long offsetRaw, bool chosen)
        {
            Assert.That(At(Covered(A2.Limited(), (short)k), offsetRaw, SimFix.FromInt(30)), Is.EqualTo(chosen ? (true, 1) : (false, -1)));
        }

        // 3: range 8 m under Sight 20 m, grass 500: Lc = 4 m.
        [TestCase(262144, true)]
        [TestCase(262145, false)]
        public void Boundary_RangeBelowSight(long offsetRaw, bool chosen)
        {
            Assert.That(At(Covered(A2.Limited(), 500), offsetRaw, SimFix.FromInt(8)), Is.EqualTo(chosen ? (true, 1) : (false, -1)));
        }

        // 4: nearest-visible without a sight cap: L = range 30 m, grass 500: Lc = 15 m.
        [TestCase(983040, true)]
        [TestCase(983041, false)]
        public void Boundary_NearestVisible(long offsetRaw, bool chosen)
        {
            Assert.That(At(Covered(new NearestVisibleEnemy(), 500), offsetRaw, SimFix.FromInt(30)), Is.EqualTo(chosen ? (true, 1) : (false, -1)));
        }

        // 5: the target's cell counts, not the shooter's. Shooter at x = 1 m, target 15 m east (cell 16), L = 20 m.
        [Test]
        public void TargetCellCounts_NotShooterCell()
        {
            // Grass on x < 10: the shooter stands in it, the target does not: chosen.
            var shooterInGrass = new ConcealedSight(A2.Limited(), A2.Grid(40, 40), A2.Cover(40, 40, (x, z) => x < 10 ? (short)500 : (short)1000));
            M5TestWorld w = A2.Direct(1);
            A2.Place(w, 0, X0, Z0); A2.Place(w, 1, X0 + 15 * S, Z0);
            Assert.That(A2.Pick(w, shooterInGrass, SimFix.FromInt(30)), Is.EqualTo((true, 1)));
            // Grass on x >= 10: the target stands in it (Lc = 10 m), the shooter does not: not chosen.
            var targetInGrass = new ConcealedSight(A2.Limited(), A2.Grid(40, 40), A2.Cover(40, 40, (x, z) => x >= 10 ? (short)500 : (short)1000));
            Assert.That(A2.Pick(w, targetInGrass, SimFix.FromInt(30)), Is.EqualTo((false, -1)));
        }

        // 6: the nearest qualifying target wins. Shooter at x = 20 m; grass is x >= 25; E1 (unit 1) east, E2 (unit 2) west.
        [TestCase(8, 6, 2)]
        [TestCase(8, 12, 1)]
        [TestCase(11, 12, 2)]
        public void NearestQualifyingTarget(int e1Metres, int e2Metres, int chosen)
        {
            var selector = Covered(A2.Limited(), 500, 25);
            M5TestWorld w = A2.Direct(2);
            A2.Place(w, 0, 20 * S, Z0);
            A2.Place(w, 1, (20 + e1Metres) * S, Z0);
            A2.Place(w, 2, (20 - e2Metres) * S, Z0);
            Assert.That(A2.Pick(w, selector, SimFix.FromInt(30)), Is.EqualTo((true, chosen)));
        }

        // 7: a point outside the grid has no cover. The grid is 10 x 40 (x < 10 m), all grass 500; the target 15 m east is
        // outside it and chosen; the same target inside a 40 x 40 all-grass grid is not.
        [Test]
        public void OutsideTheGrid_NoCover()
        {
            M5TestWorld w = A2.Direct(1);
            A2.Place(w, 0, X0, Z0); A2.Place(w, 1, X0 + 15 * S, Z0);
            var small = new ConcealedSight(A2.Limited(), A2.Grid(10, 40), A2.Cover(10, 40, (x, z) => 500));
            var large = new ConcealedSight(A2.Limited(), A2.Grid(40, 40), A2.Cover(40, 40, (x, z) => 500));
            Assert.That(A2.Pick(w, small, SimFix.FromInt(30)), Is.EqualTo((true, 1)));
            Assert.That(A2.Pick(w, large, SimFix.FromInt(30)), Is.EqualTo((false, -1)));
        }

        // The scaled limit never forms L * k: with L.Raw = 1000q + r it is q*k + floor(r*k/1000) <= L.Raw, the exact floor(L * k / 1000).
        [TestCase(long.MaxValue - 1000000, 999)]
        [TestCase(long.MaxValue, 999)]
        [TestCase(long.MaxValue, 1000)]
        [TestCase(1310720, 1000)]
        [TestCase(1310720, 333)]
        [TestCase(999, 1)]
        public void ScaledLimit_IsTheExactFloor(long limitRaw, int k)
        {
            MethodInfo scaled = typeof(ConcealedSight).GetMethod("Scaled", BindingFlags.NonPublic | BindingFlags.Static);
            long value = (long)scaled.Invoke(null, new object[] { limitRaw, k });
            Assert.That(new BigInteger(value), Is.EqualTo(new BigInteger(limitRaw) * k / 1000));
        }

        // 11: only the two nearest-visible forms are wrapped.
        [Test]
        public void OtherInner_IsRejected()
        {
            Assert.Throws<NotSupportedException>(() => new ConcealedSight(new OtherSelector(), A2.Grid(4, 4), A2.Cover(4, 4, (x, z) => 500)));
            Assert.Throws<NotSupportedException>(() => new ConcealedSight(new SightLimited(new OtherSelector(), A2.Sight), A2.Grid(4, 4), A2.Cover(4, 4, (x, z) => 500)));
        }

        // 10a: WithSelector keeps every other argument; its digest is that of the same pack built with the new selector.
        [Test]
        public void WithSelector_CopiesEveryArgument()
        {
            SimFix m(int v) => SimFix.FromInt(v);
            var weapons = new[] { A2.Rifle(32768), new WeaponDef(45000, 10, 60000, m(5), m(40), 65536) };
            var melee = new[] { new MeleeSpec(1, 1, 0, m(2), 500, m(1), m(10), 3, m(1)) };
            var block = new[] { new BlockSpec(500, 600, 0) };
            var resolver = new RwrHitResolver();
            var original = new CombatPack(weapons, resolver, A2.Limited(), 77 * S, true, true, 123, 45000, melee, block);
            var selector = new NearestVisibleEnemy();
            CombatPack copy = original.WithSelector(selector);
            var direct = new CombatPack(weapons, resolver, selector, 77 * S, true, true, 123, 45000, melee, block);
            Assert.That(copy.Selector, Is.SameAs(selector));
            Assert.That(copy.Resolver, Is.SameAs(original.Resolver));
            Assert.That(copy.Weapons.ToArray(), Is.EqualTo(original.Weapons.ToArray()));
            Assert.That(copy.Melee.ToArray(), Is.EqualTo(original.Melee.ToArray()));
            Assert.That(copy.Block.ToArray(), Is.EqualTo(original.Block.ToArray()));
            Assert.That((copy.MaxHealthRaw, copy.FriendlyFire, copy.FriendlyFireLocked, copy.AllyCheckMissRaw, copy.AllyHoldMilli, copy.MaxHitsPerAction),
                Is.EqualTo((77 * S, true, true, 123, 45000L, original.MaxHitsPerAction)));
            Assert.That(copy.PolicyDigest, Is.EqualTo(direct.PolicyDigest));
            Assert.That(copy.PolicyDigest, Is.Not.EqualTo(original.PolicyDigest));
            // A pack without melee or block keeps them absent (the digest branches stay off).
            var plain = new CombatPack(weapons, resolver, A2.Limited(), 77 * S);
            Assert.That(plain.WithSelector(selector).PolicyDigest, Is.EqualTo(new CombatPack(weapons, resolver, selector, 77 * S).PolicyDigest));
        }

        internal sealed class OtherSelector : ITargetSelector
        {
            public ulong PolicyKey => 0x4132544553540001UL;
            public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target) { target = default; return false; }
        }
    }
}
