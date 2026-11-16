using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt153CrowdPassFeatureTests
    {
        [TestCase("CrowdPassWithoutCombat")]
        [TestCase("CrowdPassWithoutCrowd")]
        public void RejectsMissingSubsystem(string name)
        {
            var example = AssemblyGuardCases.Cases.Single(c => c.Name == name);
            var error = Assert.Throws<ArgumentException>(() => example.Run());
            Assert.That(error.ParamName, Is.EqualTo("recipe"));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        public void RecentShotBlocksUntilWindowExpires(ulong engaged)
        {
            var w = new M6World(M6World.Endless(M6World.RecipeOf(new[]
            {
                M6World.U(1, 1, 5), M6World.U(2, 1, 15)
            })));
            var rule = new TeamIdleCrowdPass(w.Sim, w.Combat, w.Recipe.Combat, null);
            w.Step(100);
            Assert.That(rule.MayPass(w.Unit(1), w.Unit(2)), Is.True);

            w.Set(engaged, new Combatant(1, -1, 0, 41000));
            Assert.That(rule.MayPass(w.Unit(1), w.Unit(2)), Is.False, "59 ticks after the shot due time");

            w.Step();
            Assert.That(rule.MayPass(w.Unit(1), w.Unit(2)), Is.True, "60 ticks after the shot due time");
        }
    }
}
