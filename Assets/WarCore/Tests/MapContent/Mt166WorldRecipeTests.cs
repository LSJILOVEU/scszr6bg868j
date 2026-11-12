using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt166WorldRecipeTests
    {
        [Test]
        public void ZeroSpeedIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UnitSpawn(1, 0, 1, 0, 0, 0));
        }

        [Test]
        public void UnitsAreOrderedByAscendingKey()
        {
            var recipe = new WorldRecipe(41, MoverKind.Interim, new[]
            {
                new UnitSpawn(3, 0, 1, 0, 0, 3000),
                new UnitSpawn(1, 0, 1, 0, 0, 3000),
                new UnitSpawn(2, 0, 1, 0, 0, 3000)
            }, Array.Empty<BridgeCommand>());

            Assert.That(recipe.Units[0].Key, Is.EqualTo(1UL));
            Assert.That(recipe.Units[1].Key, Is.EqualTo(2UL));
            Assert.That(recipe.Units[2].Key, Is.EqualTo(3UL));
        }
    }
}
