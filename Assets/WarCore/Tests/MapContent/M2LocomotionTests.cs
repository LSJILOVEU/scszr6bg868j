using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M2LocomotionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void StraightWall_StopsWithinOneStepAndNeverPenetratesFor600Ticks(bool reverse)
        {
            SimVec3 start = M2MapFixture.Point(reverse ? 7 : 1, 2);
            SimVec3 target = M2MapFixture.Point(reverse ? 1 : 7, 2);
            var world = new M2MapFixture.World(M2MapFixture.GoldenBytes, start);
            SimVec3 stop = default;
            for (int tick = 1; tick <= 600; tick++)
            {
                world.Tick(tick, target);
                AssertOutsideWalls(world, tick);
                Assert.That(world.Locomotion.IsArrived(world.Unit), Is.False, "tick=" + tick);
                Assert.That(world.Active, Is.True, "Blocked order must remain active.");
                if (tick == 1) Assert.That(world.Position, Is.Not.EqualTo(start));
                if (tick == 150) stop = world.Position;
                if (tick >= 150) Assert.That(world.Position, Is.EqualTo(stop), "tick=" + tick);
            }
            long contact = reverse ? 5 * SimFix.Scale + world.Radius.Raw : 4 * SimFix.Scale - world.Radius.Raw;
            Assert.That(Math.Abs(world.Position.X.Raw - contact), Is.LessThanOrEqualTo(world.Step.Raw + 1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Control_NoWallOrNoMask_ReachesTarget(bool keepWallButDisableMask)
        {
            byte[] bytes = keepWallButDisableMask ? M2MapFixture.GoldenBytes : WcMapWriter.Write(M2MapFixture.Model(false));
            var world = new M2MapFixture.World(bytes, M2MapFixture.Point(1, 2), wallMask: keepWallButDisableMask ? 0u : 2u);
            SimVec3 target = M2MapFixture.Point(7, 2);
            for (int tick = 1; tick <= 600; tick++) world.Tick(tick, target);
            Assert.That(world.Position, Is.EqualTo(target));
            Assert.That(world.Locomotion.IsArrived(world.Unit), Is.True);
            Assert.That(world.Active, Is.False);
        }

        [Test]
        public void DiagonalCorner_StopsWithoutSliding()
        {
            SimVec3 start = M2MapFixture.Point(2, 0), target = M2MapFixture.Point(6, 2);
            var world = new M2MapFixture.World(M2MapFixture.GoldenBytes, start);
            SimVec3 stop = default;
            for (int tick = 1; tick <= 600; tick++)
            {
                world.Tick(tick, target);
                AssertOutsideWalls(world, tick);
                Assert.That(world.Locomotion.IsArrived(world.Unit), Is.False);
                if (tick == 1) Assert.That(world.Position, Is.Not.EqualTo(start));
                if (tick == 150) stop = world.Position;
                if (tick >= 150) Assert.That(world.Position, Is.EqualTo(stop));
            }
            // The stop lies outside both faces of the lower-left corner.
            Assert.That(stop.X.Raw, Is.LessThan(4 * SimFix.Scale));
            Assert.That(stop.Z.Raw, Is.LessThan(SimFix.Scale));
            Assert.That(world.Active, Is.True);
        }

        [Test]
        public void StepLongerThanWallThickness_CannotTunnelEvenOnArrivalStep()
        {
            SimVec3 start = M2MapFixture.Point(1, 2), target = M2MapFixture.Point(7, 2);
            var world = new M2MapFixture.World(M2MapFixture.GoldenBytes, start, speed: 180);
            Assert.That(world.Step.Raw, Is.GreaterThan(2 * world.Map.Walls[0].HalfX.Raw));
            for (int tick = 1; tick <= 600; tick++)
            {
                world.Tick(tick, target);
                AssertOutsideWalls(world, tick);
                Assert.That(world.Position, Is.EqualTo(start));
                Assert.That(world.Active, Is.True);
                Assert.That(world.Locomotion.IsArrived(world.Unit), Is.False);
            }
        }

        [Test]
        public void RadiusComesFromUnitCollider()
        {
            var world = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2), radius: SimFix.Ratio(1, 2));
            for (int tick = 1; tick <= 150; tick++)
            {
                world.Tick(tick, M2MapFixture.Point(7, 2));
                AssertOutsideWalls(world, tick);
            }
            long gap = 4 * SimFix.Scale - world.Radius.Raw - world.Position.X.Raw;
            Assert.That(gap, Is.InRange(0, world.Step.Raw + 1));
        }

        [Test]
        public void SameBytesRecipeAndCommand_MatchAll601StateHashes()
        {
            var first = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2));
            var second = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2));
            for (int tick = 0; tick <= 600; tick++)
            {
                if (tick != 0)
                {
                    first.Tick(tick, M2MapFixture.Point(7, 2));
                    second.Tick(tick, M2MapFixture.Point(7, 2));
                }
                Assert.That(first.Sim.StateHashRetained(), Is.EqualTo(second.Sim.StateHashRetained()), "tick=" + tick);
            }
            TestContext.Progress.WriteLine("WP-06a tick 600 state hash: " + first.Sim.StateHashRetained().ToString("X16"));
        }

        [Test]
        public void BlockedOrder_SurvivesStableSnapshotAndCanBeRedirected()
        {
            var first = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2));
            var restored = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2));
            for (int tick = 1; tick <= 150; tick++) first.Tick(tick, M2MapFixture.Point(7, 2));
            restored.Sim.RestoreRetainedSnapshot(first.Sim.SaveRetainedSnapshot());
            // Restore can replace physical entity handles, so exercise the original world for redirection.
            Assert.That(restored.Sim.StateHashRetained(), Is.EqualTo(first.Sim.StateHashRetained()));
            SimVec3 blockedAt = first.Position;
            // The blocked order must still be live after restore: both worlds keep stepping and must stay hash-identical.
            for (int tick = 151; tick <= 300; tick++)
            {
                first.Tick(tick, M2MapFixture.Point(7, 2));
                restored.Tick(tick, M2MapFixture.Point(7, 2));
                Assert.That(restored.Sim.StateHashRetained(), Is.EqualTo(first.Sim.StateHashRetained()), "tick=" + tick);
            }
            // Restore replaces physical handles, so the restored unit is judged by hash only; the order stayed blocked and live.
            Assert.That(first.Position, Is.EqualTo(blockedAt));
            Assert.That(first.Active, Is.True);
            first.Locomotion.Order(first.Unit, M2MapFixture.Point(1, 2));
            for (int tick = 301; tick <= 450; tick++)
            {
                first.Tick(tick, M2MapFixture.Point(1, 2));
                AssertOutsideWalls(first, tick);
            }
            Assert.That(first.Position, Is.EqualTo(M2MapFixture.Point(1, 2)));
            Assert.That(first.Active, Is.False);
        }

        [Test]
        public void WallMask_IsSavedHashedAndValidatedWhileDefaultRetainsM1Encoding()
        {
            var first = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2));
            var different = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2), wallMask: 4u);
            Assert.That(first.Sim.StateHashRetained(), Is.Not.EqualTo(different.Sim.StateHashRetained()));
            var writer = new SimStateWriter();
            first.Locomotion.SaveState(writer);
            Assert.That(writer.ToArray().Length, Is.EqualTo(8));
            Assert.Throws<InvalidOperationException>(() => different.Locomotion.LoadState(new SimStateReader(writer.ToArray())));
            var legacy = new M2MapFixture.World(M2MapFixture.GoldenBytes, M2MapFixture.Point(1, 2), wallMask: 0u);
            var empty = new SimStateWriter();
            legacy.Locomotion.SaveState(empty);
            Assert.That(empty.ToArray(), Is.Empty);
            Assert.That(legacy.Locomotion.StateVersion, Is.EqualTo(1));
        }

        private static void AssertOutsideWalls(M2MapFixture.World world, int tick)
        {
            // Independent integer circle/rectangle distance. No Sim query or geometry helper.
            long x = world.Position.X.Raw, z = world.Position.Z.Raw;
            foreach (WcMapWall wall in world.Map.Walls)
            {
                long cx = world.Map.Min.X.Raw + wall.LocalCenter.X.Raw;
                long cz = world.Map.Min.Z.Raw + wall.LocalCenter.Z.Raw;
                long dx = Math.Max(Math.Abs(x - cx) - wall.HalfX.Raw, 0L);
                long dz = Math.Max(Math.Abs(z - cz) - wall.HalfZ.Raw, 0L);
                long squared = checked(dx * dx + dz * dz);
                Assert.That(squared, Is.GreaterThanOrEqualTo(checked(world.Radius.Raw * world.Radius.Raw)), "penetration tick=" + tick);
            }
        }
    }
}
