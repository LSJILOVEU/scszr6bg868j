using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // HC-5: the world limit, the collider and terrain height limits, one second in thousandths of a tick, one metre in raw
    // units and the map side each have one definition; the files that used to copy the number now name it. Comments and
    // string literals are blanked first (SizeLiteralScanner.Blank), so only code counts.
    public sealed class Hc5CopiesTests
    {
        private const string World = @"\b16384L?\b", ColliderHeight = @"\b32768L?\b", TerrainHeight = @"\b1L?\s*<<\s*29\b";
        private const string Second = @"\b30_?000[mMlL]?\b", Metre = @"\b65_?536[fFlL]?\b", MapSide = @"\b512(_?000)?\b";

        private static int Count(string relative, string pattern)
        {
            string path = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore", relative);
            Assert.That(File.Exists(path), Is.True, relative);
            return Regex.Matches(SizeLiteralScanner.Blank(File.ReadAllText(path)), pattern).Count;
        }

        [TestCase("Content/WcMapValidation.cs", World, 0)]
        [TestCase("Content/MapTools/MapDescCross.cs", World, 0)]
        [TestCase("Combat/CombatSystem.Sight.cs", World, 0)]
        [TestCase("Sim/SpatialQuery.cs", ColliderHeight, 1)]
        [TestCase("Sim/Posture.cs", ColliderHeight, 0)]
        [TestCase("Content/MapTools/MapDescReader.cs", TerrainHeight, 0)]
        [TestCase("Combat/WeaponDef.cs", Second, 0)]
        [TestCase("Combat/ProjectileSpec.cs", Second, 0)]
        [TestCase("Combat/ProjectileSystem.cs", Second, 0)]
        [TestCase("Content/Data/ContentWeaponEdit.cs", Second, 0)]
        [TestCase("Presentation/Bridge/Features/ViewGround.cs", Metre, 0)]
        [TestCase("Presentation/Bridge/TerrainMesh.cs", Metre, 0)]
        [TestCase("Presentation/Bridge/ViewMap.cs", Metre, 0)]
        [TestCase("Content/Data/Sections/AiSection.cs", MapSide, 0)]
        public void CopiesNameTheirDefinition(string relative, string pattern, int expected)
        {
            Assert.That(Count(relative, pattern), Is.EqualTo(expected), relative + " " + pattern);
        }

        [Test]
        public void DerivedNamesFollowTheirDefinitions()
        {
            Assert.That(SimClock.MilliTicksPerSecond, Is.EqualTo(1000L * SimClock.TickRate));
            Assert.That(SimClock.MilliTicksPerSecond, Is.EqualTo(30000L));
            Assert.That(WcMap.RawPerMetre, Is.EqualTo(SimFix.Scale));
            Assert.That(WcMapValidation.MaxSide, Is.EqualTo(HeightField.MaxSide));
            Assert.That(WcMapValidation.MaxSideMm, Is.EqualTo(HeightField.MaxSide * 1000));
            Assert.That(AiSection.MaxMeleeChaseRange, Is.EqualTo(HeightField.MaxSide));
            Assert.That(AiSection.MaxSquadRadiusMm, Is.EqualTo(WcMapValidation.MaxSideMm));
            Assert.That(MapDescReader.HeightLimit, Is.EqualTo(HeightField.HeightLimitRaw - 1));
            Assert.That(Collider.HeightLimit, Is.EqualTo(32768L * SimFix.Scale));
        }
    }
}
