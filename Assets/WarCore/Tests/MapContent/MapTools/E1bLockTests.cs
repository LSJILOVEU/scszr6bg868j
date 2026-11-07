using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using static WarCore.Tests.E1aSamples;

namespace WarCore.Tests
{
    // Recorded on the base code before MapRecipe existed: the content and description facts the E1b tests rely on.
    public sealed class E1bLockTests
    {
        // Weapon order of rwr.json (rwr.weapon-1, rwr.weapon-2) and melee order of rwr-melee.json (rwr.sword-1).
        // E1bRecipeTests writes these indices as literals; a swap of weapon and melee changes both indices of at least one type.
        [TestCase("rwr.soldier-2", 1, -1)]
        [TestCase("rwr.swordsman", -1, 0)]
        [TestCase("rwr.rifle-sword", 0, 0)]
        public void MeleePackIndicesOfTheMappedUnitTypes(string type, int weapon, int melee)
        {
            ContentPack pack = RwrPack.Load("rwr-melee");
            Assert.That(pack.WeaponOfUnitType(type), Is.EqualTo(weapon), type + " weapon");
            Assert.That(pack.MeleeOfUnitType(type), Is.EqualTo(melee), type + " melee");
        }

        // The M6 description carries the demo recipe's units: same key, team, squad (= team), position and reserve flag.
        [Test]
        public void M6DescriptionSoldiersMatchTheDemoRecipeUnits()
        {
            MapSetup setup = Bake(File.ReadAllBytes(M6Path)).Setup;
            WorldRecipe demo = DemoRecipes.M6();
            Assert.That(setup.Soldiers.Count, Is.EqualTo(80));
            Assert.That(demo.Units.Length, Is.EqualTo(80));
            int reserves = 0;
            for (int i = 0; i < 80; i++)
            {
                MapSoldier s = setup.Soldiers[i];
                UnitSpawn u = demo.Units[i];
                string at = "unit " + u.Key;
                Assert.That(s.Key, Is.EqualTo(u.Key), at);
                Assert.That(s.Team, Is.EqualTo(u.Team), at);
                Assert.That(s.Squad, Is.EqualTo(u.Squad), at);
                Assert.That(s.XMm, Is.EqualTo(u.XMm), at);
                Assert.That(s.ZMm, Is.EqualTo(u.ZMm), at);
                Assert.That(s.Reserve, Is.EqualTo(u.Reserve), at);
                Assert.That(s.Type, Is.EqualTo(s.Team == 1 ? RwrPack.SoldierTeam1 : RwrPack.SoldierTeam2), at);
                if (s.Reserve) reserves++;
            }
            Assert.That(reserves, Is.EqualTo(60));
            Assert.That(setup.Seed, Is.EqualTo(41UL));
            Assert.That(setup.CrowdPass, Is.EqualTo("teamIdle"));
        }
    }
}
