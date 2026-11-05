using System.IO;
using NUnit.Framework;

namespace WarCore.Tests
{
    [TestFixture, Category("S5Entrypoints"), Category("M0c1Mutation")]
    public sealed class RetainedEntrypointGuardTests
    {
        [Test]
        public void ProductCodeUsesOnlyRetainedEntrypoints()
        {
            string root = Path.Combine(ArchitectureTests.AssetRoot(),"WarCore");
            foreach (string file in Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories))
                Assert.That(RetainedEntrypointSourceRules.Violations(File.ReadAllText(file),file),Is.Empty,file);
        }

        [TestCase("sim.StateHashStable();")]
        [TestCase("sim.SaveStableSnapshot();")]
        [TestCase("sim.RestoreStableSnapshot(bytes);")]
        [TestCase("new Sim(1, identityMode: IdentityMode.StableWorld);")]
        [TestCase("new WarCore.Sim.Sim(1, config: new SimConfig(entityCapacity: 4),\n identityMode: IdentityMode /* gap */ . StableWorld);")]
        [TestCase("using World = WarCore.Sim.Sim; new World(1,identityMode:IdentityMode.StableWorld);")]
        [TestCase("StateHashStable /* gap */ ();")]
        public void InjectedForbiddenCallFailsEvenInsideCompatibilityFiles(string injected)
        {
            foreach (string path in new[] { "Content/Spawn.cs", "Sim/StableSimState.cs", "Sim/StableSchedulerState.cs", "Sim/RetainedWorld.cs" })
                Assert.That(RetainedEntrypointSourceRules.Violations(injected,path),Is.Not.Empty,path);
            Assert.That(RetainedEntrypointSourceRules.Violations(injected,"Tests/EditMode/Probe.cs"),Is.Empty);
            Assert.That(RetainedEntrypointSourceRules.Violations(injected,"Verification/Probe.cs"),Is.Empty);
        }

        [Test]
        public void RetainedCallsAndCommentsPassButDeclarationsCannotHideCalls()
        {
            const string valid = "var s = Sim.CreateRetainedWorld(1); s.StateHashRetained(); s.SaveRetainedSnapshot(); s.RestoreRetainedSnapshot(bytes); // s.StateHashStable();";
            Assert.That(RetainedEntrypointSourceRules.Violations(valid,"Content/Spawn.cs"),Is.Empty);
            const string declaration = "public ulong StateHashStable() { return 0; }";
            Assert.That(RetainedEntrypointSourceRules.Violations(declaration,"Sim/StableSimState.cs"),Is.Empty);
            Assert.That(RetainedEntrypointSourceRules.Violations(declaration+"StateHashStable();","Sim/StableSimState.cs"),Is.Not.Empty);
        }
    }
}
