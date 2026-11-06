using System;
using System.Text;
using NUnit.Framework;
using Simulation = WarCore.Sim.Sim;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The per-unit facing component: absent unless the recipe opens it, default until something writes it, and part of
    // the snapshot and the state hash when present. Nothing reads it for gameplay yet.
    public sealed class FacingTests
    {
        private static readonly byte[] Map = WcMapWriter.Write(Wp23.Make(40, 24, 0, 6 * Wp23.S, null, 4 * Wp23.S));
        private static readonly UnitSpawn[] Units = { Wp23.Unit(1, 1, 3500, 3500), Wp23.Unit(2, 1, 3500, 6500), Wp23.Unit(3, 2, 30500, 3500) };
        private static readonly BridgeCommand[] Script = { BridgeCommand.MoveTo(1, 1, 20500, 3500), BridgeCommand.MoveTo(2, 2, 3500, 15500) };

        private static SimSession World(bool facing, MoverKind mover = MoverKind.InterimCrowd)
        {
            var recipe = new WorldRecipe(41, mover, Units, Script);
            return SimSession.Create(facing ? recipe.WithFacing() : recipe, Map);
        }

        private static void Face(SimSession s, ulong key, long xRaw, long zRaw)
        {
            Simulation sim = M3TestWorld.Sim(s);
            sim.Entities.Set(sim.Entities.Resolve<UnitFacing>(), M3TestWorld.Unit(s, key), new UnitFacing(SimFix.FromRaw(xRaw), SimFix.FromRaw(zRaw)));
        }

        [TestCase(MoverKind.Interim), TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void WithoutTheRecipeFlag_NoFacingExists(MoverKind mover)
        {
            SimSession s = World(false, mover);
            for (int i = 0; i < 30; i++) s.StepOnce();
            Assert.That(s.TryFacing(1, out _, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => M3TestWorld.Sim(s).Entities.Resolve<UnitFacing>());
            Assert.That(Encoding.ASCII.GetString(M3TestWorld.Sim(s).SaveRetainedSnapshot()), Does.Not.Contain(UnitFacing.ComponentId));
        }

        [Test]
        public void WithTheFlag_EveryUnitReadsTheDefault_AndNothingMovesIt()
        {
            SimSession s = World(true);
            for (int tick = 0; tick <= 120; tick++)
            {
                if (tick != 0) s.StepOnce();
                foreach (var spawn in Units)
                {
                    Assert.That(s.TryFacing(spawn.Key, out long x, out long z), Is.True);
                    Assert.That((x, z), Is.EqualTo((0L, Wp23.S)), "unit " + spawn.Key + " at tick " + tick);
                }
            }
            Assert.That(s.TryFacing(99, out _, out _), Is.False, "an unknown unit");
            Assert.That(Encoding.ASCII.GetString(M3TestWorld.Sim(s).SaveRetainedSnapshot()), Does.Contain(UnitFacing.ComponentId));
        }

        [Test]
        public void TheValueIsInTheStateHash()
        {
            SimSession a = World(true), b = World(true);
            Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            Face(b, 2, 1, Wp23.S);
            Assert.That(b.StateHash, Is.Not.EqualTo(a.StateHash), "X alone");
            Face(b, 2, 0, Wp23.S + 1);
            Assert.That(b.StateHash, Is.Not.EqualTo(a.StateHash), "Z alone");
            Face(b, 2, 0, Wp23.S);
            Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            Assert.That(World(false).StateHash, Is.Not.EqualTo(a.StateHash), "opening the component changes the world");
        }

        [Test]
        public void SnapshotsRoundTrip_AndResumeToTheSameHashes()
        {
            SimSession whole = World(true), a = World(true);
            Face(whole, 1, -23170, 45000); Face(a, 1, -23170, 45000);
            for (int i = 0; i < 60; i++) { whole.StepOnce(); a.StepOnce(); }
            byte[] snapshot = M3TestWorld.Sim(a).SaveRetainedSnapshot();
            SimSession b = World(true);
            M3TestWorld.Sim(b).RestoreRetainedSnapshot(snapshot);
            Assert.That(b.StateHash, Is.EqualTo(whole.StateHash));
            Assert.That(b.TryFacing(1, out long x, out long z), Is.True);
            Assert.That((x, z), Is.EqualTo((-23170L, 45000L)));
            for (int i = 0; i < 90; i++)
            {
                whole.StepOnce(); b.StepOnce();
                Assert.That(b.StateHash, Is.EqualTo(whole.StateHash), "step " + (61 + i));
            }
        }

        [Test]
        public void ASnapshotWithFacing_IsRefusedByAWorldWithout_AndTheOtherWayAround()
        {
            byte[] with = M3TestWorld.Sim(World(true)).SaveRetainedSnapshot(), without = M3TestWorld.Sim(World(false)).SaveRetainedSnapshot();
            Assert.Throws<SimSnapshotException>(() => M3TestWorld.Sim(World(false)).RestoreRetainedSnapshot(with));
            Assert.Throws<SimSnapshotException>(() => M3TestWorld.Sim(World(true)).RestoreRetainedSnapshot(without));
        }
    }
}
