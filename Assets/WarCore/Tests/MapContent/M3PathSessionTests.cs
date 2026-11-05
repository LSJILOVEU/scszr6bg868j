using System;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M3PathSessionTests
    {
        [Test]
        public void IndependentRuns_Match601Hashes_AndReachBothDemoTargets()
        {
            var first = SimSession.Create(DemoRecipes.M3(), DemoRecipes.M2bMapBytes());
            var second = SimSession.Create(DemoRecipes.M3(), DemoRecipes.M2bMapBytes());
            for (int tick = 0; tick <= 600; tick++)
            {
                if (tick != 0) { first.StepOnce(); second.StepOnce(); }
                Assert.That(second.StateHash, Is.EqualTo(first.StateHash), "tick=" + tick);
            }
            Assert.That(M3TestWorld.Position(first), Is.EqualTo(M3TestWorld.Point(60000, 12000)));
            Assert.That(M3TestWorld.Position(first, 2), Is.EqualTo(M3TestWorld.Point(60000, 28000)));
            TestContext.Out.WriteLine("M3 tick600 StateHash " + first.StateHash.ToString("X16"));
        }

        [Test]
        public void SnapshotAt150_RestoresWaypointAndMatchesEveryFollowingTick()
        {
            byte[] bytes = DemoRecipes.M2bMapBytes();
            var first = SimSession.Create(DemoRecipes.M3(), bytes);
            var restored = SimSession.Create(DemoRecipes.M3(), bytes);
            for (int tick = 1; tick <= 150; tick++) first.StepOnce();
            Assert.That(M3TestWorld.Component<MoveIntent>(first).Active, Is.True);
            Assert.That(M3TestWorld.Component<MoveWaypoint>(first).Has, Is.True);
            var sim = M3TestWorld.Sim(first);
            var recovery = M3TestWorld.Sim(restored);
            recovery.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot());
            Assert.That(recovery.StateHashRetained(), Is.EqualTo(sim.StateHashRetained()));
            Assert.That(recovery.SaveRetainedSnapshot(), Is.EqualTo(sim.SaveRetainedSnapshot()));
            Assert.That(M3TestWorld.Component<MoveWaypoint>(restored).Point, Is.EqualTo(M3TestWorld.Component<MoveWaypoint>(first).Point));
            for (int tick = 151; tick <= 300; tick++)
            {
                sim.Step(default); recovery.Step(default);
                Assert.That(recovery.StateHashRetained(), Is.EqualTo(sim.StateHashRetained()), "tick=" + tick);
            }
        }

        [Test]
        public void Configuration_V1V2BytesStayExact_V3StoresMaskAndDigestAndRejectsMismatch()
        {
            byte[] bytes = DemoRecipes.M2bMapBytes();
            var session = SimSession.Create(DemoRecipes.M3(), bytes);
            var entities = M3TestWorld.Sim(session).Entities;
            var grid = WcMapNavigation.Create(WcMapReader.Read(bytes));
            var path = M3TestWorld.Locomotion(session);
            var legacy1 = new InterimLocomotion(entities);
            var legacy2 = new InterimLocomotion(entities, 2);
            Assert.That(legacy1.StateVersion, Is.EqualTo(1));
            Assert.That(legacy2.StateVersion, Is.EqualTo(2));
            Assert.That(path.StateVersion, Is.EqualTo(3));
            byte[] Save(InterimLocomotion locomotion)
            {
                var writer = new SimStateWriter(); locomotion.SaveState(writer); return writer.ToArray();
            }
            Assert.That(Save(legacy1), Is.Empty);
            Assert.That(Save(legacy2), Is.EqualTo(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0 }));
            using var expected = new MemoryStream();
            using var binary = new BinaryWriter(expected);
            binary.Write(2L); binary.Write(grid.Digest);
            Assert.That(Save(path), Is.EqualTo(expected.ToArray()));
            path.LoadState(new SimStateReader(Save(path)));
            var different = new InterimLocomotion(entities, 2, WcMapNavigation.Create(M3TestWorld.UMap()));
            Assert.Throws<InvalidOperationException>(() => different.LoadState(new SimStateReader(Save(path))));
            var differentMask = new InterimLocomotion(entities, 4, grid);
            Assert.Throws<InvalidOperationException>(() => differentMask.LoadState(new SimStateReader(Save(path))));
            var a = new StateHash(); var b = new StateHash();
            path.WriteState(a); different.WriteState(b);
            Assert.That(a.Value, Is.Not.EqualTo(b.Value));
            var noWallsPath = new InterimLocomotion(entities, 0, grid);
            Assert.That(Save(noWallsPath).Length, Is.EqualTo(16));
        }

        [Test]
        public void OnlyPathMoverNeedsMapAndRegistersWaypoint()
        {
            Assert.That((int)MoverKind.InterimPath, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => SimSession.Create(DemoRecipes.M3()));
            var old = SimSession.Create(DemoRecipes.M2b(), DemoRecipes.M2bMapBytes());
            Assert.Throws<InvalidOperationException>(() => M3TestWorld.Sim(old).Entities.Resolve<MoveWaypoint>());
            var current = SimSession.Create(DemoRecipes.M3(), DemoRecipes.M2bMapBytes());
            Assert.That(M3TestWorld.Component<MoveWaypoint>(current).Has, Is.False);
            var serializer = new MoveWaypointSerializer();
            Assert.That((serializer.TypeId, serializer.Version), Is.EqualTo((2002, 1)));
            var point = new MoveWaypoint(M3TestWorld.Point(1234, 5678), true);
            var writer = new SimStateWriter(); serializer.Write(writer, point);
            var read = serializer.Read(new SimStateReader(writer.ToArray()));
            Assert.That(read.Point, Is.EqualTo(point.Point)); Assert.That(read.Has, Is.True);
            Assert.That(writer.ToArray().Length, Is.EqualTo(25));
            var a = new StateHash(); var b = new StateHash(); var c = new StateHash();
            point.WriteState(a); new MoveWaypoint(point.Point, false).WriteState(b);
            new MoveWaypoint(M3TestWorld.Point(1235, 5678), true).WriteState(c);
            Assert.That(a.Value, Is.Not.EqualTo(b.Value)); Assert.That(a.Value, Is.Not.EqualTo(c.Value));
        }

        [TestCase("wall")]
        [TestCase("enclosed")]
        [TestCase("outside")]
        [TestCase("start-wall")]
        public void InvalidOrders_StopAtomicallyAndRetainOldTarget(string scenario)
        {
            WcMap map = M3TestWorld.Boxes(12, 12, (4, 4, 1, 4), (7, 4, 1, 4), (4, 4, 4, 1), (4, 7, 4, 1));
            var session = M3TestWorld.Single(WcMapWriter.Write(map), scenario == "start-wall" ? 4500 : 1500,
                scenario == "start-wall" ? 4500 : 1500);
            var locomotion = M3TestWorld.Locomotion(session);
            EntityId unit = M3TestWorld.Unit(session);
            SimVec3 oldTarget = M3TestWorld.Component<MoveIntent>(session).Target;
            SimVec3 target = scenario == "wall" ? M3TestWorld.Point(4500, 4500)
                : scenario == "enclosed" ? M3TestWorld.Point(5500, 5500)
                : scenario == "outside" ? M3TestWorld.Point(12000, 1000) : M3TestWorld.Point(1500, 1500);
            locomotion.Order(unit, target);
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Active, Is.False);
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Target, Is.EqualTo(oldTarget));
            Assert.That(M3TestWorld.Component<MoveWaypoint>(session).Has, Is.False);
            SimVec3 position = M3TestWorld.Position(session);
            for (int i = 0; i < 30; i++) session.StepOnce();
            Assert.That(M3TestWorld.Position(session), Is.EqualTo(position));
        }

        [Test]
        public void UnreachableReplacementStopsOldOrder_AndStopAllowsNewOrder()
        {
            var session = M3TestWorld.Single(WcMapWriter.Write(M3TestWorld.UMap()), 1500, 1500);
            var locomotion = M3TestWorld.Locomotion(session);
            EntityId unit = M3TestWorld.Unit(session);
            SimVec3 oldTarget = M3TestWorld.Point(12500, 11500);
            locomotion.Order(unit, oldTarget);
            for (int i = 0; i < 8; i++) session.StepOnce();
            Assert.That(M3TestWorld.Position(session), Is.Not.EqualTo(M3TestWorld.Point(1500, 1500)));
            locomotion.Order(unit, M3TestWorld.Point(4500, 4500));
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Active, Is.False);
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Target, Is.EqualTo(oldTarget));
            Assert.That(M3TestWorld.Component<MoveWaypoint>(session).Has, Is.False);
            SimVec3 stopped = M3TestWorld.Position(session);
            for (int i = 0; i < 30; i++) session.StepOnce();
            Assert.That(M3TestWorld.Position(session), Is.EqualTo(stopped));
            locomotion.Order(unit, oldTarget);
            session.StepOnce(); locomotion.Stop(unit);
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Active, Is.False);
            Assert.That(M3TestWorld.Component<MoveWaypoint>(session).Has, Is.False);
            stopped = M3TestWorld.Position(session);
            session.StepOnce(); Assert.That(M3TestWorld.Position(session), Is.EqualTo(stopped));
            SimVec3 final = M3TestWorld.Point(1500, 1500);
            locomotion.Order(unit, final);
            for (int i = 0; i < 100; i++) session.StepOnce();
            Assert.That(M3TestWorld.Position(session), Is.EqualTo(final));
            Assert.That(locomotion.IsArrived(unit), Is.True);
        }

        [Test]
        public void CenterFirst_CompressedTurn_ExactTargetAndUnusedStepDiscard()
        {
            var session = M3TestWorld.Single(WcMapWriter.Write(M3TestWorld.Boxes(8, 8)), 1100, 1300, 60000);
            var locomotion = M3TestWorld.Locomotion(session);
            EntityId unit = M3TestWorld.Unit(session);
            SimVec3 target = M3TestWorld.Point(3600, 1700);
            locomotion.Order(unit, target);
            Assert.That(M3TestWorld.Component<MoveWaypoint>(session).Point, Is.EqualTo(M3TestWorld.Point(1500, 1500)));
            session.StepOnce();
            Assert.That(M3TestWorld.Position(session), Is.EqualTo(M3TestWorld.Point(1500, 1500)), "Discard unused movement.");
            Assert.That(M3TestWorld.Component<MoveWaypoint>(session).Point, Is.EqualTo(M3TestWorld.Point(3500, 1500)), "Compress straight cells.");
            session.StepOnce();
            Assert.That(M3TestWorld.Position(session), Is.EqualTo(M3TestWorld.Point(3500, 1500)));
            Assert.That(M3TestWorld.Component<MoveWaypoint>(session).Point, Is.EqualTo(target), "Select next waypoint in the arrival tick.");
            session.StepOnce();
            Assert.That(M3TestWorld.Position(session), Is.EqualTo(target));
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Active, Is.False);
            Assert.That(M3TestWorld.Component<MoveWaypoint>(session).Has, Is.False);
        }

        [Test]
        public void CapsuleSweepStillBlocksUnsafeTargetInsidePassableCell()
        {
            var session = M3TestWorld.Single(WcMapWriter.Write(M3TestWorld.Boxes(8, 8, (4, 0, 1, 8))), 1500, 3500);
            var locomotion = M3TestWorld.Locomotion(session);
            locomotion.Order(M3TestWorld.Unit(session), M3TestWorld.Point(3990, 3500));
            for (int i = 0; i < 60; i++) session.StepOnce();
            SimVec3 stopped = M3TestWorld.Position(session);
            Assert.That(stopped.X.Raw, Is.LessThan(4 * SimFix.Scale - SimFix.Scale / 4));
            Assert.That(M3TestWorld.Component<MoveIntent>(session).Active, Is.True);
            session.StepOnce(); Assert.That(M3TestWorld.Position(session), Is.EqualTo(stopped));
        }
    }
}
