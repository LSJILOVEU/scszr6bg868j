using System;
using System.IO;
using NUnit.Framework;

namespace WarCore.Tests
{
    // The builder only sequences the hooks: combat, player control, the battle adapter and every system registration live in
    // the installers under Bridge/Features.
    public sealed class F2bStructureTests
    {
        private const string BuilderPath = "Assets/WarCore/Presentation/Bridge/WorldBuilder.cs";

        [Test]
        public void TheBuilderIsShort() => Assert.That(File.ReadAllLines(BuilderPath).Length, Is.LessThanOrEqualTo(120));

        [TestCase("new CombatSystem(")]
        [TestCase("new PlayerControl(")]
        [TestCase("new BattleAdapter(")]
        [TestCase("RegisterGameplay(")]
        public void TheBuilderNoLongerBuildsSystems(string text) =>
            Assert.That(File.ReadAllText(BuilderPath).IndexOf(text, StringComparison.Ordinal), Is.EqualTo(-1));
    }
}
