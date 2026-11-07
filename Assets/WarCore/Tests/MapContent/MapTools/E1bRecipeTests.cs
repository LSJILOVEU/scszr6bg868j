using System;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using static WarCore.Tests.E1aSamples;
using static WarCore.Tests.E1bSamples;

namespace WarCore.Tests
{
    // E1 acceptance 3 (the M6 description runs tick for tick like the demo recipe), field mapping, and pack swap (acceptance 6).
    public sealed class E1bRecipeTests
    {
        private static void SameEveryTick(SimSession doc, SimSession demo)
        {
            int compared = 0;
            for (int tick = 0; tick <= 600; tick++)
            {
                Assert.That(doc.Tick, Is.EqualTo(tick), "description world tick");
                Assert.That(demo.Tick, Is.EqualTo(tick), "demo world tick");
                Assert.That(doc.StateHash, Is.EqualTo(demo.StateHash), "first different state hash at tick " + tick);
                compared++;
                if (tick < 600) { doc.StepOnce(); demo.StepOnce(); }
            }
            Assert.That(compared, Is.EqualTo(601));
        }

        [Test]
        public void M6DescriptionRunsLikeTheDemoRecipe()
        {
            SimSession doc = null;
            Assert.DoesNotThrow(() => doc = MapDocument.Open(File.ReadAllBytes(M6Path)).Start());
            SameEveryTick(doc, SimSession.Create(DemoRecipes.M6(), DemoRecipes.M6MapBytes()));
        }

        [Test]
        public void M6DescriptionRunsLikeTheDemoRecipeWithReversedAllocation()
        {
            MapBakeResult result = Bake(File.ReadAllBytes(M6Path));
            WorldRecipe recipe = MapRecipe.From(result, RwrPack.Load(result.PackId));
            SameEveryTick(Build(recipe, result.WcMapBytes, true), Build(DemoRecipes.M6(), DemoRecipes.M6MapBytes(), true));
        }

        // Keys, types, squads and positions all differ; teams 1 and 2, reserve true and false, and squad != team on every soldier.
        // Indices from E1bLockTests: soldier-2 (weapon 1, no melee), swordsman (no weapon, melee 0), rifle-sword (weapon 0, melee 0).
        private static readonly string ThreeSoldiers = Unit(3, "rwr.soldier-2", 2, 5, 10500, 4500, false) + ", "
            + Unit(9, "rwr.swordsman", 1, 4, 2500, 6500, true) + ", " + Unit(6, "rwr.rifle-sword", 1, 8, 7500, 1500, false);

        private static WorldRecipe MeleeRecipe(bool withPoint)
        {
            string text = withPoint
                ? Desc("rwr-melee", null, null, OnePoint, TwoBases, ThreeSoldiers, 77, "teamIdle", 2, 3, 10)
                : Desc("rwr-melee", null, null, "", "", ThreeSoldiers, 78, "none", 1, 1, 10);
            MapBakeResult result = Bake(text);
            return MapRecipe.From(result, RwrPack.Load("rwr-melee"));
        }

        private static void AssertUnit(UnitSpawn u, ulong key, int squad, long x, long z, int team, int weapon, int melee, bool reserve)
        {
            string at = "unit " + key;
            Assert.That(u.Key, Is.EqualTo(key), at);
            Assert.That(u.Archetype, Is.EqualTo(0), at);
            Assert.That(u.Squad, Is.EqualTo(squad), at);
            Assert.That(u.XMm, Is.EqualTo(x), at);
            Assert.That(u.ZMm, Is.EqualTo(z), at);
            Assert.That(u.SpeedMmPerSec, Is.EqualTo(6000), at);
            Assert.That(u.Team, Is.EqualTo(team), at);
            Assert.That(u.Weapon, Is.EqualTo(weapon), at);
            Assert.That(u.Melee, Is.EqualTo(melee), at);
            Assert.That(u.Reserve, Is.EqualTo(reserve), at);
            Assert.That(u.Block, Is.EqualTo(-1), at);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SoldiersMapFieldByField(bool withPoint)
        {
            WorldRecipe recipe = MeleeRecipe(withPoint);
            Assert.That(recipe.Units.Length, Is.EqualTo(3));
            AssertUnit(recipe.Units[0], 3, 5, 10500, 4500, 2, 1, -1, false);
            AssertUnit(recipe.Units[1], 6, 8, 7500, 1500, 1, 0, 0, false);
            AssertUnit(recipe.Units[2], 9, 4, 2500, 6500, 1, -1, 0, true);
            Assert.That(recipe.Seed, Is.EqualTo(withPoint ? 77UL : 78UL));
            Assert.That(recipe.Mover, Is.EqualTo(MoverKind.InterimCrowd));
            Assert.That(recipe.CrowdPass, Is.EqualTo(withPoint ? CrowdPassKind.TeamIdle : CrowdPassKind.None));
            Assert.That(recipe.Combat, Is.Not.Null);
            if (withPoint) Assert.That(recipe.Match, Is.Not.Null);
            else Assert.That(recipe.Match, Is.Null);
            Assert.That(recipe.TerrainPack, Is.Null);
            Assert.That(recipe.PlayerKey, Is.Null);
            Assert.That(recipe.Script.Length, Is.EqualTo(0));
            Assert.That(recipe.Facing || recipe.Motion || recipe.Standing || recipe.BlockAi, Is.False);
        }

        private const string M6PackLine = "\"pack\": \"rwr-m6\",";
        private const string MismatchMessage = "Map pack 'rwr-melee' does not match content pack 'rwr-m6'.";

        [Test]
        public void SwappingThePackKeepsTheMapAndChangesTheHash()
        {
            string text = File.ReadAllText(M6Path);
            MapBakeResult m6 = Bake(text);
            MapBakeResult melee = Bake(Edit(text, M6PackLine, "\"pack\": \"rwr-melee\","));
            Assert.That(melee.PackId, Is.EqualTo("rwr-melee"));
            Assert.That(melee.WcMapBytes, Is.EqualTo(m6.WcMapBytes));
            Assert.That(melee.DescHash, Is.Not.EqualTo(m6.DescHash));
            ContentPack m6Pack = RwrPack.Load(RwrPack.M6Layer);
            ArgumentException e = Assert.Throws<ArgumentException>(() => MapRecipe.From(melee, m6Pack));
            Assert.That(ExceptionText.Body(e), Is.EqualTo(MismatchMessage));
            Assert.That(e.ParamName, Is.EqualTo("pack"));
            Assert.DoesNotThrow(() => MapRecipe.From(melee, RwrPack.Load("rwr-melee")));
        }
    }
}
