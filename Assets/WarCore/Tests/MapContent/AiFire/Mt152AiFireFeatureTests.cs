using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt152AiFireFeatureTests
    {
        [TestCase(1, 1, 0)]
        [TestCase(0, 2, 0)]
        [TestCase(0, 1, 1)]
        public void MinimumSetting_IsActive(int reactionTicks, int senseTicks, int burstShots)
        {
            WorldRecipe recipe = P1.Recipe(P1.Cast(), P3.Ai(reactionTicks, senseTicks, burstShots));
            WcMap map = A2.Map(A2.DWalls);
            var assembly = new WorldAssembly(recipe, WcMapWriter.Write(map), false)
            {
                Terrain = TerrainDerivation.Derive(map, recipe.TerrainPack)
            };

            Assert.That(AiFireFeature.Instance.IsActive(assembly), Is.True);
        }
    }
}
