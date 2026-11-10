using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class AiPlaytestContentTests
    {
        private static object Field(object instance, string name)
        {
            Assert.That(instance, Is.Not.Null, name);
            return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        [Test]
        public void PlaytestLayer_ReachesEveryAiSystem()
        {
            WorldRecipe recipe = ContentLayers.Terrain("rwr-ai-playtest");
            AiRules ai = recipe.TerrainPack.Section<AiRules>();
            // PLACEHOLDER: all nine tuning values require PF1 calibration against recordings.
            var expected = new[] { 90, 300, 90, 6, 3, 3, 15, 60, 12 };
            Assert.That(new[] { ai.FailedSlotTicks, ai.TargetMemoryTicks, ai.SearchTicks, ai.ReactionTicks, ai.SenseTicks,
                ai.BurstShots, ai.BurstPauseTicks, ai.SuppressTicks, ai.MeleeChaseRange }, Is.EqualTo(expected));
            var w = new M6World(recipe, TerrainDemo.MapBytes());
            FailedSlotMemory failed = w.System<FailedSlotMemory>();
            TargetMemory contacts = w.System<TargetMemory>();
            TargetSearch search = w.System<TargetSearch>();
            TargetSuppress suppress = w.System<TargetSuppress>();
            ReactionFire fire = w.System<ReactionFire>();
            SimpleAiSystem brain = w.System<SimpleAiSystem>();
            Assert.That(new[] { (int)Field(failed, "ticks"), (int)Field(contacts, "ticks"), (int)Field(search, "ticks"),
                (int)Field(fire, "ticks"), (int)Field(fire, "senseTicks"), (int)Field(fire, "burstShots"),
                (int)Field(fire, "burstPauseTicks"), (int)Field(suppress, "ticks"),
                (int)(((SimFix)Field(brain, "meleeChaseRange")).Raw / SimFix.Scale) }, Is.EqualTo(expected));
            Assert.That(Field(w.Combat, "autoFire"), Is.SameAs(fire));
            Assert.That(Field(brain, "memory"), Is.SameAs(failed));
            Assert.That(Field(brain, "contacts"), Is.SameAs(contacts));
            Assert.That(Field(brain, "search"), Is.SameAs(search));
            Assert.That(Field(brain, "suppress"), Is.SameAs(suppress));
        }

        [Test]
        public void PlaytestLayer_MeleeUnitChasesVisibleEnemy()
        {
            var units = new[] { A1.Unit(1, 1, 10, 10), A1.Unit(2, 2, 20, 10, melee: -1, speed: 1) };
            WorldRecipe recipe = A1.Recipe(null, units).WithTerrainRules(RwrPack.Load("rwr-ai-playtest"));
            var w = new M6World(recipe, A1.Map());
            long first = L4.FirstThink(w, A1.Think);
            w.Step((int)first - 1);
            SimVec3 enemy = w.Read<Position>(2).Value;
            w.Step();
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(enemy));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
        }
    }
}
