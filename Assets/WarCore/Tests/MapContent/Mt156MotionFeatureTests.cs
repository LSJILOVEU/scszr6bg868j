using System;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt156MotionFeatureTests
    {
        [Test]
        public void EachUnitStartsWithItsOwnSpeedLimit()
        {
            var recipe = new WorldRecipe(41, MoverKind.Interim,
                new[]
                {
                    new UnitSpawn(1, 0, 1, 3500, 3500, 3750),
                    new UnitSpawn(2, 0, 2, 6500, 3500, 7500),
                }, Array.Empty<BridgeCommand>()).WithMotion();
            SimSession session = SimSession.Create(recipe);

            Assert.That(M3TestWorld.Component<MotionLimits>(session, 1).MaxSpeed.Raw, Is.EqualTo(8192L));
            Assert.That(M3TestWorld.Component<MotionLimits>(session, 2).MaxSpeed.Raw, Is.EqualTo(16384L));
        }
    }
}
