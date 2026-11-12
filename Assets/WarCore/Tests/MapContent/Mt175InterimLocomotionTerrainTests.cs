using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt175InterimLocomotionTerrainTests
    {
        [Test]
        public void ConstructorRejectsAnotherNavigationGrid()
        {
            WcMap map = Wp23Fx.Map((x, z) => false);
            TerrainTables terrain = TerrainDerivation.Derive(map, Wp23Fx.ClosedPack());
            var session = Wp23Fx.World(MoverKind.InterimPath, map, Wp23.Unit(1, 1, 1500, 1500));
            EntityStore entities = M3TestWorld.Sim(session).Entities;
            NavGrid other = WcMapNavigation.Create(map);

            Assert.That(Assert.Throws<ArgumentException>(() =>
                new InterimLocomotion(entities, 0, other, false, null, null, terrain)).ParamName,
                Is.EqualTo("navGrid"));
        }

        [Test]
        public void TerrainPathRejectsAMoverWithoutACollider()
        {
            WcMap map = Wp23.Make(8, 4, 0, 6 * Wp23.S, new[] { (6, 2, 1, 1) }, 4 * Wp23.S,
                names: Wp23.Names, surface: (x, z) => 0);
            var session = Wp23.Session(map, MoverKind.InterimPath,
                new[] { Wp23.Unit(1, 1, 1500, 1500) }, pack: Wp23.Pack());
            Wp23Fx.Order(session, 1, 3500, 1500);
            M3TestWorld.Sim(session).Entities.Remove<Collider>(M3TestWorld.Unit(session));

            Assert.Throws<InvalidOperationException>(() => session.StepOnce());
        }
    }
}
