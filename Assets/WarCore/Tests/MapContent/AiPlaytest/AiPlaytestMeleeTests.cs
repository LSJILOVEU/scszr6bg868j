using NUnit.Framework;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class AiPlaytestMeleeTests
    {
        [Test]
        public void PlaytestRangeMakesAMeleeUnitChase()
        {
            var units = new[] { A1.Unit(1, 1, 10, 10), A1.Unit(2, 2, 20, 10, melee: -1, speed: 1) };
            var recipe = A1.Recipe(null, units, endless: true);
            var off = new M6World(recipe, A1.Map());
            var on = new M6World(recipe.WithTerrainRules(RwrPack.Load("rwr-ai-playtest")), A1.Map());
            long first = (A1.Think - (long)(on.Sim.Entities.StableOf(on.Unit(1)).Value % A1.Think)) % A1.Think;
            if (first == 0) first = A1.Think;
            while (on.Sim.Clock.Tick < first - 1) { off.Step(); on.Step(); }
            SimVec3 enemy = on.Read<Position>(2).Value;
            SimVec3 before = on.Read<Position>(1).Value;
            off.Step(); on.Step();
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(enemy));
            Assert.That(on.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(on.Read<Position>(1).Value, Is.Not.EqualTo(before));
            Assert.That(off.Read<MoveIntent>(1).Target, Is.Not.EqualTo(enemy));
        }
    }
}
