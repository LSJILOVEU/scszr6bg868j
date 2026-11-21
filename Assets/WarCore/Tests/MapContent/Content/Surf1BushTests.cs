using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // SURF-1: bush cover, end to end. The world is assembled from the real "rwr-surfaces" layer, so the cover factor comes from the
    // data through CombatFeature into ConcealedSight. Sight 20 m (A2 combat pack), asked range 30 m, so L = 20 m; bush
    // hides a target beyond L * 500 / 1000 = 10 m. Only the target's cell counts (ConcealedSight, see A2SelectTests).
    // Distances are along +X from the shooter at x = 5.5 m, z = 10.5 m, in millimetres (multiples of 500 are exact in raw).
    public sealed class Surf1BushTests
    {
        private const long S = SimFix.Scale;
        private static readonly string[] Names = { "bush", "grass" }; // the map file keeps its surface names in ordinal order

        // bushFrom(x): which cells are bush.
        private static M6World World(Func<int, bool> bush, long targetXMm)
        {
            WcMap map = Wp23.Make(A2.Width, A2.Depth, 0, 4 * S, Array.Empty<(int, int, int, int)>(), 2 * S, (x, z) => 0, Names,
                (x, z) => bush(x) ? (byte)0 : (byte)1);
            var units = new[]
            {
                new UnitSpawn(1, 0, 1, 5500, 10500, 1000, 1, -1, false),
                new UnitSpawn(2, 0, 2, targetXMm, 10500, 1000, 2, -1, false),
            };
            var pack = RwrPack.Load(Surf1RulesTests.Layer);
            var w = new M6World(A2.Recipe(units, A2.Combat(0), pack), WcMapWriter.Write(map));
            w.Sim.CompleteSpatialBoundary();
            return w;
        }

        private static bool Noticed(M6World w)
        {
            Assert.That(w.Combat.Pack.Selector, Is.InstanceOf<ConcealedSight>(), "bush cover must wrap the selector");
            bool found = w.Combat.Pack.Selector.TrySelect(w.Combat, w.Unit(1), SimFix.FromInt(30), out EntityId target);
            if (found) Assert.That(target, Is.EqualTo(w.Unit(2)));
            return found;
        }

        // The target stands on bush (cells x >= 12): noticed up to 10 m, not beyond. 10000 mm = 655360 raw = the limit exactly.
        [TestCase(9000L, true)]
        [TestCase(10000L, true)]
        [TestCase(10500L, false)]
        [TestCase(11000L, false)]
        public void TargetInBush_IsNoticedOnlyWithinHalfTheDistance(long distanceMm, bool noticed)
        {
            Assert.That(Noticed(World(x => x >= 12, 5500 + distanceMm)), Is.EqualTo(noticed), "distance " + distanceMm);
        }

        // The shooter stands in bush (cells x < 8) and the target on grass: the shooter's own cell does not matter, L = 20 m.
        [TestCase(19000L, true)]
        [TestCase(20000L, true)]
        [TestCase(20500L, false)]
        [TestCase(21000L, false)]
        public void ShooterInBush_SeesAsFarAsEver(long distanceMm, bool noticed)
        {
            Assert.That(Noticed(World(x => x < 8, 5500 + distanceMm)), Is.EqualTo(noticed), "distance " + distanceMm);
        }

        // Without any bush the same world sees the full 20 m (the wrapper is absent, the plain sight-limited selector decides).
        [TestCase(11000L, true)]
        [TestCase(20000L, true)]
        [TestCase(20500L, false)]
        public void NoBush_NothingIsHidden(long distanceMm, bool noticed)
        {
            M6World w = World(x => false, 5500 + distanceMm);
            Assert.That(w.Combat.Pack.Selector, Is.Not.InstanceOf<ConcealedSight>());
            Assert.That(w.Combat.Pack.Selector.TrySelect(w.Combat, w.Unit(1), SimFix.FromInt(30), out _), Is.EqualTo(noticed), "distance " + distanceMm);
        }
    }
}
