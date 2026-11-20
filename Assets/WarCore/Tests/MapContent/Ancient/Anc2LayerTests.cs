using System.IO;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Anc2LayerTests
    {
        [Test]
        public void TheFieldLayerAddsASpearAndASpearman()
        {
            ContentPack pack = Anc2.Load();
            Assert.That((pack.WeaponCount, pack.MeleeCount, pack.UnitTypeCount), Is.EqualTo((0, 2, 3)));
            Assert.That(File.ReadAllText(Anc2.FieldDirectory + "/ancient-field.json"), Does.Not.Contain("rwr."));
            Assert.That(File.ReadAllText(Anc2.ShippedDirectory + "/ancient.json"), Does.Not.Contain("ancient.spear"));
            MeleeSpec spear = pack.BuildCombat().Melee[1];
            Assert.That((spear.IsThrust, spear.LineHalfWidth, spear.Damage), Is.EqualTo((true, SimFix.Ratio(3, 10), SimFix.FromInt(40))));
            Assert.That(pack.BuildCombat().Melee[0].IsThrust, Is.False);
        }
    }
}
