using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class GridOwnershipRegressionTests
    {
        [Test]
        public void _2bA_SimExposesAnOwnedReadOnlyQuerySurface()
        {
            GridIndexRegressionAudit.SimHasQuerySurface(typeof(Simulation));
        }

        private static GridOwnershipTestFixture RestoreScene()
        {
            var scene = new GridOwnershipTestFixture();
            scene.CreateBody(SimVec3.Metres(-16, 8, -16));
            scene.CreateBody(SimVec3.Metres(16, 32, 16), 16, 40);
            scene.CreateBody(SimVec3.Metres(-16, 8, -16));
            scene.InitializeIndex();
            scene.Sim.Step(default);
            scene.Sim.Step(default);
            return scene;
        }

        private static void PoisonLaterIndex(GridOwnershipTestFixture scene)
        {
            foreach (EntityId entity in scene.Store)
            {
                scene.Store.Get(scene.Positions, entity).Value += SimVec3.Metres(256, 0, 256);
                // This establishes a legitimate later cache before rewind; nothing repairs it after Restore.
                scene.Grid.Upsert(entity);
            }
            scene.Sim.Step(default);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void _2bA_M31_RestoreWithoutPostureRebuildsGridForEveryFollowingTick(bool freshInstance)
        {
            var online = RestoreScene();
            var replay = RestoreScene();
            byte[] saved = replay.Sim.SaveSnapshot();
            if (freshInstance)
                replay = new GridOwnershipTestFixture();
            else
                PoisonLaterIndex(replay);
            replay.Sim.RestoreSnapshot(saved);
            var differences = new List<string>();
            for (int tick = 2; tick <= 8; tick++)
            {
                Assert.That(replay.Sim.StateHash(), Is.EqualTo(online.Sim.StateHash()), "Authority restored before grid comparison.");
                foreach (EntityId entity in online.Store)
                    foreach (SpatialQueryKind kind in Enum.GetValues(typeof(SpatialQueryKind)))
                    {
                        SpatialRequest request = GridOwnershipTestFixture.Request(kind, online.Store.Get(online.Positions, entity).Value);
                        string expected = GridOwnershipTestFixture.Results(online.Grid, in request);
                        string actual = GridOwnershipTestFixture.Results(replay.Grid, in request);
                        if (expected != actual)
                            differences.Add("tick=" + tick + " kind=" + kind + " entity=" + entity.Id + " expected=" + expected + " actual=" + actual);
                    }
                online.Sim.Step(default);
                replay.Sim.Step(default);
            }
            TestContext.WriteLine("2bA M3-1: no PostureSystem; fresh={0}; compared ticks=2..8; mismatches={1}", freshInstance, differences.Count);
            Assert.That(differences, Is.Empty, "M3-1: RestoreSnapshot did not rebuild the derived grid. " + string.Join(" | ", differences));
        }

        [TestCase(SpatialQueryKind.Sphere, false)]
        [TestCase(SpatialQueryKind.Cylinder, false)]
        [TestCase(SpatialQueryKind.Sector, false)]
        [TestCase(SpatialQueryKind.Capsule, false)]
        [TestCase(SpatialQueryKind.Ray, false)]
        [TestCase(SpatialQueryKind.Sphere, true)]
        [TestCase(SpatialQueryKind.Cylinder, true)]
        [TestCase(SpatialQueryKind.Sector, true)]
        [TestCase(SpatialQueryKind.Capsule, true)]
        [TestCase(SpatialQueryKind.Ray, true)]
        public void _2bA_M32_MovementMatchesNaiveAtEachOfTwelveTicks(SpatialQueryKind kind, bool large)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(SimVec3.Metres(-16, 32, -16), large ? 16 : 1, large ? 40 : 2);
            scene.Store.Set(scene.Movements, entity, new Movement(MoveMode.Flying, SimVec3.Metres(90, 30, 60)));
            scene.AddMovement();
            scene.InitializeIndex();
            Assert.That(scene.Grid.MembershipCount, Is.GreaterThan(0));
            Assert.That(scene.Grid.Index.LargeBodyCount, Is.EqualTo(large ? 1 : 0), "Bodies over 64 cells must use the bypass list.");
            var differences = new List<string>();
            for (int tick = 1; tick <= 12; tick++)
            {
                scene.Sim.Step(default);
                SimVec3 position = scene.Store.Get(scene.Positions, entity).Value;
                Assert.That(position, Is.EqualTo(SimVec3.Metres(-16 + 3 * tick, 32 + tick, -16 + 2 * tick)), "Real MovementSystem must run.");
                SpatialRequest request = GridOwnershipTestFixture.Request(kind, position);
                string expected = GridOwnershipTestFixture.Results(scene.Naive(), in request);
                Assert.That(expected, Is.Not.EqualTo("[]"), "The oracle must hit the mover.");
                string actual = GridOwnershipTestFixture.Results(scene.Grid, in request);
                if (actual != expected)
                    differences.Add("tick=" + tick + " expected=" + expected + " actual=" + actual);
            }
            TestContext.WriteLine("2bA M3-2: kind={0}; large={1}; ticks=12; mismatches={2}", kind, large, differences.Count);
            Assert.That(differences, Is.Empty, "M3-2: Movement wrote Position without engine grid synchronization. " + string.Join(" | ", differences));
        }

        [Test]
        public void _2bA_R21_RestoredCellsAndLargeBodyMembershipsEqualOnlineLogically()
        {
            var online = RestoreScene();
            var replay = RestoreScene();
            byte[] saved = replay.Sim.SaveSnapshot();
            PoisonLaterIndex(replay);
            replay.Sim.RestoreSnapshot(saved);
            GridIndexRegressionAudit.Equal(GridIndexRegressionAudit.Logical(online.Grid), GridIndexRegressionAudit.Logical(replay.Grid),
                "R2-1: same authority must produce equal cell sets, ancestor occupancy and cached bodies after restore.");
        }

        [Test]
        public void _2bA_RestoreOracleAcceptsExplicitColdRebuildAndDifferentInsertionHistory()
        {
            var online = RestoreScene();
            var replay = RestoreScene();
            byte[] saved = replay.Sim.SaveSnapshot();
            PoisonLaterIndex(replay);
            replay.Sim.RestoreSnapshot(saved);
            replay.Grid.Rebuild();
            EntityId first = new EntityId(0, 1);
            replay.Grid.Remove(first);
            replay.Grid.Upsert(first);
            Assert.That(QueryReadOnlyTests.Capture(replay.Grid), Is.Not.EqualTo(QueryReadOnlyTests.Capture(online.Grid)), "Control must change physical history.");
            GridIndexRegressionAudit.Equal(GridIndexRegressionAudit.Logical(online.Grid), GridIndexRegressionAudit.Logical(replay.Grid),
                "Logical equality ignores free-list and bucket ordering.");
            foreach (SpatialQueryKind kind in Enum.GetValues(typeof(SpatialQueryKind)))
            {
                SpatialRequest request = GridOwnershipTestFixture.Request(kind, SimVec3.Metres(16, 32, 16));
                GridOwnershipTestFixture.EqualResults(GridOwnershipTestFixture.Results(online.Grid, in request),
                    GridOwnershipTestFixture.Results(replay.Grid, in request), "Explicit repair is a separate positive control only.");
            }
        }
    }
}
