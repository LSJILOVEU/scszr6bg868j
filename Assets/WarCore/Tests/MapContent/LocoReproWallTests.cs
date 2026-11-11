using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class LocoReproWallTests
    {
        [Category("Manual"), Test, Explicit("Reproduces blocked AI movement at wall contact.")]
        public void WallContact_AllowsTangentialMovement()
        {
            var map = M3TestWorld.Boxes(4, 4, (2, 0, 1, 4));
            var world = new M4TestWorld(map, new[] { new UnitSpawn(1, 0, 1, 1750, 1250, 6000) });
            var target = M3TestWorld.Point(1750, 1750);
            Assert.That(M3TestWorld.WallDistance(world.Point(0), target, map, map.Walls[0]), Is.EqualTo(0.25));
            world.Order(0, 1750, 1750);

            for (int tick = 1; tick <= 3; tick++) world.Sim.Step(default);

            Assert.That(world.Point(0).X.Raw, Is.EqualTo(target.X.Raw));
            Assert.That(world.Point(0).Z.Raw, Is.EqualTo(target.Z.Raw), "Wall contact must allow tangential movement.");
        }
    }
}
