using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt027InterimLocomotionTerrainTests
    {
        [Test]
        public void Constructor_UsesTerrainNavigationWhenGridIsOmitted()
        {
            WcMap map = Wp23Fx.Map((x, z) => false);
            TerrainTables terrain = TerrainDerivation.Derive(map, Wp23Fx.ClosedPack());
            SimSession s = Wp23Fx.World(MoverKind.InterimPath, map, Wp23.Unit(1, 1, 2500, 5500));
            InterimLocomotion locomotion = null;
            Assert.DoesNotThrow(() => locomotion = new InterimLocomotion(M3TestWorld.Sim(s).Entities, 0, null, false, null, null, terrain));
            Assert.That(locomotion.StateVersion, Is.EqualTo(103));
        }

        [Test]
        public void Constructor_RejectsAnotherGridWithTheSameCells()
        {
            WcMap map = Wp23Fx.Map((x, z) => false);
            TerrainTables terrain = TerrainDerivation.Derive(map, Wp23Fx.ClosedPack());
            SimSession s = Wp23Fx.World(MoverKind.InterimPath, map, Wp23.Unit(1, 1, 2500, 5500));
            NavGrid other = Wp23Fx.Nav(map);
            Assert.That(Assert.Throws<ArgumentException>(() =>
                new InterimLocomotion(M3TestWorld.Sim(s).Entities, 0, other, false, null, null, terrain)).ParamName,
                Is.EqualTo("navGrid"));
        }

        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void MaximumBaseStep_MovesExactlyOneQuarterMetre(MoverKind mover)
        {
            SimSession s = Wp23Fx.World(mover, Wp23Fx.Map((x, z) => false), Wp23.Unit(1, 1, 2500, 5500, 7500));
            Wp23Fx.Order(s, 1, 4500, 5500);
            Assert.That(Wp23.Intent(s).SpeedPerTick.Raw, Is.EqualTo(16384));
            Assert.DoesNotThrow(() => s.StepOnce());
            Assert.That(Wp23.At(s), Is.EqualTo(Wp23.V(2750, 0, 5500)));
            Assert.That(Wp23.Intent(s).Active, Is.True);
        }

        [Test]
        public void MinimumSurfaceStep_ArrivesAndStopsInOneTick()
        {
            string slow = TerrainDemoMap.PackJson.Replace("\"speedMilli\": 500", "\"speedMilli\": 1");
            WcMap map = Wp23.Make(6, 3, 0, 6 * Wp23.S, null, 4 * Wp23.S, null, Wp23.Names, (x, z) => 1);
            SimSession s = Wp23.Session(map, MoverKind.InterimPath, new[] { Wp23.Unit(1, 1, 500, 1500, 100) }, pack: Wp23.Pack(slow));
            SimVec3 target = Wp23.At(s) + new SimVec3(SimFix.FromRaw(1), default, default);
            M3TestWorld.Locomotion(s).Order(M3TestWorld.Unit(s), target);
            s.StepOnce();
            Assert.That(Wp23.At(s), Is.EqualTo(target));
            Assert.That(Wp23.Intent(s).Active, Is.False);
            Assert.That(M3TestWorld.Component<MoveWaypoint>(s), Is.EqualTo(default(MoveWaypoint)));
            Assert.That(Wp23Fx.Progress(s, 1), Is.EqualTo(default(MoveProgress)));
        }

        [Test]
        public void ExactStepDistance_ArrivesAndStopsInOneTick()
        {
            SimSession s = Wp23Fx.World(MoverKind.InterimPath, Wp23Fx.Map((x, z) => false), Wp23.Unit(1, 1, 2500, 5500, Wp23Fx.Speed));
            Wp23Fx.Order(s, 1, 2625, 5500);
            s.StepOnce();
            Assert.That(Wp23.At(s), Is.EqualTo(Wp23.V(2625, 0, 5500)));
            Assert.That(Wp23.Intent(s).Active, Is.False);
            Assert.That(M3TestWorld.Component<MoveWaypoint>(s), Is.EqualTo(default(MoveWaypoint)));
            Assert.That(Wp23Fx.Progress(s, 1), Is.EqualTo(default(MoveProgress)));
        }
    }
}
