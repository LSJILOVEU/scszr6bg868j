using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (7): the production recipe by structure only; the whole-match Golden runs on the frozen twin (L6 fixture + copier).
    public sealed class L10DemoTests
    {
        private const long Limit = 9000;

        internal sealed class Run
        {
            internal M6World World;
            internal L6Trace Trace;
            internal int Switches, Respawns, OffGround;
        }

        // Steps with the script queued until the match is decided; counts control switches, respawns and off-ground ticks.
        internal static Run ToEnd(WorldRecipe recipe)
        {
            var r = new Run { World = new M6World(recipe, L6.MapBytes()), Trace = new L6Trace() };
            M6World w = r.World;
            Queue(w.Session, L10FlatLockTests.DemoScript);
            ulong? last = w.Session.ControlledKey;
            while (w.Match.Winner == 0 && w.Sim.Clock.Tick < Limit)
            {
                w.Session.StepOnce();
                r.Trace.Observe(w);
                ulong? key = w.Session.ControlledKey;
                if (key != last) { r.Switches++; if (key.HasValue && last.HasValue) r.Respawns++; last = key; }
                if (key.HasValue && !w.Dead(key.Value) && !OnGround(w, At(w, key.Value))) r.OffGround++;
            }
            return r;
        }

        [Test]
        public void L10Demo_ProductionRecipeAndScript_AreTheReviewedValues()
        {
            WorldRecipe recipe = TerrainPlayerDemo.Recipe();
            Assert.That(recipe.Match.RespawnTicks, Is.EqualTo(300));
            Assert.That(recipe.PlayerKey, Is.EqualTo((ulong?)1));
            Assert.That(recipe.TerrainPack, Is.Not.Null);
            var script = TerrainPlayerDemo.Script();
            var expected = L10FlatLockTests.DemoScript;
            Assert.That(script.Length, Is.EqualTo(6));
            for (int i = 0; i < 6; i++)
            {
                PlayerInput a = script[i].Input, e = expected[i].Input;
                Assert.That(script[i].AtTick, Is.EqualTo(expected[i].AtTick), "tick " + i);
                Assert.That((a.MoveXMilli, a.MoveZMilli, a.AimXMm, a.AimZMm), Is.EqualTo((e.MoveXMilli, e.MoveZMilli, e.AimXMm, e.AimZMm)), "move/aim " + i);
                Assert.That((a.Primary, a.Block, a.Throw, a.SkillSlot), Is.EqualTo((e.Primary, e.Block, e.Throw, e.SkillSlot)), "buttons " + i);
            }
            Assert.That(TerrainPlayerDemo.Script(), Is.Not.SameAs(script), "a new array each call");
        }

        [Test]
        public void L10Demo_ProductionMatch_IsDecidedOnTheGround()
        {
            Run r = ToEnd(TerrainPlayerDemo.Recipe());
            Assert.That(r.World.Match.Winner, Is.Not.EqualTo(0));
            Assert.That(r.OffGround, Is.EqualTo(0));
            Assert.That(r.World.Session.PlayerLog.Count, Is.EqualTo(6));
        }

        // New behaviour baseline of this package: the L6 line columns, then switches, respawns, off-ground ticks.
        private const string TwinGolden =
            "41,765,2,1,4BEF913C6CDAE8A0,9C572B43B73B29C0,-,BF6F07BEBF73AAD4,100,332,3,17,12,8,8,10,89,74,25,0,0,0,0";

        [Test]
        public void L10Demo_FrozenTwin_Golden()
        {
            Run r = ToEnd(Twin());
            Assert.That(r.World.Recipe.Match.RespawnTicks, Is.EqualTo(300));
            string line = L6.Line(r.World, r.Trace) + "," + r.Switches + "," + r.Respawns + "," + r.OffGround;
            Assert.That(line, Is.EqualTo(TwinGolden));
        }
    }
}
