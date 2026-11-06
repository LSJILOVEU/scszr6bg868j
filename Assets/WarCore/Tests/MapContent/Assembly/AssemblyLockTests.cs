using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // F2a: the world assembly is a fixed contract. Each world below is described by values recorded on the builder before it was
    // split into a framework and installers, and must keep describing itself the same way.
    public sealed class AssemblyLockTests
    {
        private static readonly string[] Keys =
            { "components", "systems", "creation", "creationReverse", "snap0", "snap1", "snap100", "snapReverse0" };

        private static IEnumerable<string> Worlds() => AssemblyWorlds.Names;

        [TestCaseSource(nameof(Worlds))]
        public void World_KeepsItsRecordedAssembly(string world)
        {
            Dictionary<string, string> recorded = AssemblyGolden.Of(world);
            Dictionary<string, string> actual = AssemblyWorlds.Describe(world);
            Assert.That(recorded.Keys, Is.EquivalentTo(Keys), world);
            foreach (string key in Keys)
                Assert.That(actual[key], Is.EqualTo(recorded[key]), world + " " + key);
        }

        // The recording is only worth something if the fixtures reach the branch they are named for.
        private static readonly (string World, string Contains)[] Reach =
        {
            ("M2b", "interim.position"), ("M2b", "interim.locomotion|"), ("M3", "interim.move-waypoint"), ("M4", "interim.move-waypoint"),
            ("M5", "combat.system"), ("M6", "battle.match"), ("M6", "battle.capture"), ("M7", "battle.reinforce"),
            ("MeleeDuel", "bridge.dead-cleanup"), ("MpDemo", "control.player-fire"), ("MpDemo", "control.fallback"),
            ("Terrain", "interim.move-progress"), ("FacingMotionStanding", "interim.facing"),
            ("FacingMotionStanding", "interim.motion-limits"), ("FacingMotionStanding", "interim.standing"),
            ("BlockAi", "battle.block-ai"), ("BlockAi", "interim.facing"),
        };

        [Test]
        public void Fixtures_ReachTheBranchesTheyAreNamedFor()
        {
            foreach (var (world, token) in Reach)
            {
                Dictionary<string, string> recorded = AssemblyGolden.Of(world);
                Assert.That(recorded["components"] + ";" + recorded["systems"], Does.Contain(token), world + " " + token);
            }
        }

        [Test]
        public void Fixtures_AreAliveAtTickOneHundred()
        {
            foreach (string world in AssemblyWorlds.Names)
            {
                Dictionary<string, string> recorded = AssemblyGolden.Of(world);
                Assert.That(recorded["snap100"], Is.Not.EqualTo(recorded["snap0"]), world);
            }
        }

        [Test]
        public void ChainedSwitches_SurviveCopy_InEitherOrder()
        {
            WorldRecipe[] chains =
            {
                Base().WithFacing().WithMotion().WithStandingOrders().WithBlockAi().WithRuleSet("x"),
                Base().WithBlockAi().WithStandingOrders().WithMotion().WithFacing().WithRuleSet("x"),
            };
            foreach (WorldRecipe chained in chains)
            {
                WorldRecipe copy = chained.WithPlayer(1).WithCrowdPass(CrowdPassKind.TeamIdle);
                Assert.That(copy.Facing && copy.Motion && copy.Standing && copy.BlockAi, Is.True);
                Assert.That(copy.RuleSetId, Is.EqualTo("x"));
                Assert.That(copy.PlayerKey, Is.EqualTo(1UL));
                Assert.That(copy.CrowdPass, Is.EqualTo(CrowdPassKind.TeamIdle));
            }
            WorldRecipe plain = Base();
            Assert.That(plain.Facing || plain.Motion || plain.Standing || plain.BlockAi, Is.False);
            Assert.That(plain.WithFacing().Facing, Is.True);
            Assert.That(plain.Facing, Is.False, "switches return copies");
            Assert.That(Base().WithMotion().Facing, Is.False);
            Assert.That(Base().WithStandingOrders().Motion, Is.False);
            Assert.That(Base().WithBlockAi().Standing, Is.False);
        }

        private static WorldRecipe Base() =>
            new WorldRecipe(41, MoverKind.InterimCrowd, new[] { new UnitSpawn(1, 0, 1, 1000, 1000, 1000) }, new BridgeCommand[0]);
    }
}
