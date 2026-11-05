using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M4ContractTests
    {
        private static IEnumerable<int> Counts()
        {
            for (int n = 1; n <= 20; n++) yield return n;
        }

        [TestCaseSource(nameof(Counts))]
        public void Slots_ExactFormula_RectangleCentered_Separated(int n)
        {
            int cols = (int)Math.Ceiling(Math.Sqrt(n)), rows = (n + cols - 1) / cols;
            var points = new SimVec3[n];
            long minX = long.MaxValue, maxX = long.MinValue, minZ = long.MaxValue, maxZ = long.MinValue;
            for (int i = 0; i < n; i++)
            {
                points[i] = InterimOrders.SlotOffset(i, n, SimFix.FromInt(1));
                Assert.That(points[i].X.Raw, Is.EqualTo((2L * (i % cols) - cols + 1) * 32768));
                Assert.That(points[i].Z.Raw, Is.EqualTo((2L * (i / cols) - rows + 1) * 32768));
                Assert.That(points[i].Y.Raw, Is.Zero);
                minX = Math.Min(minX, points[i].X.Raw); maxX = Math.Max(maxX, points[i].X.Raw);
                minZ = Math.Min(minZ, points[i].Z.Raw); maxZ = Math.Max(maxZ, points[i].Z.Raw);
                for (int j = 0; j < i; j++)
                    Assert.That(M4CrowdTests.Squared(points[i], points[j]) >= (System.Numerics.BigInteger)65536 * 65536, Is.True);
            }
            Assert.That(minX + maxX, Is.Zero); Assert.That(minZ + maxZ, Is.Zero);
        }

        [TestCase(0, 1, 1, 0, 0)]
        [TestCase(0, 10, 2, -196608, -131072)]
        [TestCase(9, 10, 2, -65536, 131072)]
        [TestCase(2, 3, 2, -65536, 65536)]
        public void Slots_GoldenVectors(int rank, int n, int spacing, long x, long z)
        {
            SimVec3 point = InterimOrders.SlotOffset(rank, n, SimFix.FromInt(spacing));
            Assert.That(point.X.Raw, Is.EqualTo(x)); Assert.That(point.Z.Raw, Is.EqualTo(z));
        }

        [Test]
        public void SlotRank_UsesCurrentPositionRowMajor_NotKeyOrAllocationOrder()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(32, 24), new[] {
                new UnitSpawn(1, 0, 7, 8500, 6500, 6000),
                new UnitSpawn(2, 0, 7, 6500, 4500, 6000),
                new UnitSpawn(3, 0, 7, 4500, 4500, 6000) }, reverse: true);
            world.Sim.Step(M4TestWorld.Command(7, 20000, 18000));
            Assert.That(world.Intent(2).Target, Is.EqualTo(M3TestWorld.Point(19000, 17000)));
            Assert.That(world.Intent(1).Target, Is.EqualTo(M3TestWorld.Point(21000, 17000)));
            Assert.That(world.Intent(0).Target, Is.EqualTo(M3TestWorld.Point(19000, 19000)));
            // Retarget while moving; compute rank from this command's positions, never from spawns.
            world.Order(0, 2500, 2500);
            for (int tick = 0; tick < 60; tick++) world.Sim.Step(default);
            Assert.That(world.Point(0).Z.Raw, Is.LessThan(world.Point(2).Z.Raw));
            world.Sim.Step(M4TestWorld.Command(7, 20000, 18000));
            Assert.That(world.Intent(0).Target, Is.EqualTo(M3TestWorld.Point(19000, 17000)));
        }

        [Test]
        public void SlotRank_EqualPositionsUseCaptureOrder()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(16, 16), new[] {
                new UnitSpawn(1, 0, 7, 4500, 4500, 6000), new UnitSpawn(2, 0, 7, 4500, 4500, 6000) }, reverse: true);
            var order = new List<EntityId>();
            foreach (var id in new EntityStage(world.Sim.Entities).Capture()) order.Add(id);
            world.Sim.Step(M4TestWorld.Command(7, 10000, 10000));
            Assert.That(world.Sim.Entities.Get(world.Intents, order[0]).Target, Is.EqualTo(M3TestWorld.Point(9000, 10000)));
            Assert.That(world.Sim.Entities.Get(world.Intents, order[1]).Target, Is.EqualTo(M3TestWorld.Point(11000, 10000)));
        }

        [Test]
        public void LegacyHashes_M1_M2b_M3_Unchanged()
        {
            var recipes = new[] {
                new WorldRecipe(41, MoverKind.Interim, new[] { new UnitSpawn(1, 0, 1, 0, 0, 3000) },
                    new[] { BridgeCommand.MoveTo(1, 1, 10000, 0), BridgeCommand.MoveTo(150, 1, 10000, 10000) }),
                DemoRecipes.M2b(), DemoRecipes.M3()
            };
            ulong[] hashes = { 0x62306703E44CF8EBUL, 0xCEDECB8218C40918UL, 0x53DA7E1D51CE24F1UL };
            for (int i = 0; i < recipes.Length; i++)
            {
                var session = i == 0 ? SimSession.Create(recipes[i]) : SimSession.Create(recipes[i], DemoRecipes.M2bMapBytes());
                for (int tick = 1; tick <= 600; tick++) session.StepOnce();
                Assert.That(session.StateHash, Is.EqualTo(hashes[i]), "recipe=" + i);
            }
        }

        [Test]
        public void LegacyPath_SameSquadStillSharesOneTarget()
        {
            var session = SimSession.Create(new WorldRecipe(41, MoverKind.InterimPath,
                new[] { new UnitSpawn(1, 0, 7, 1500, 1500, 6000), new UnitSpawn(2, 0, 7, 2500, 1500, 6000) },
                new[] { BridgeCommand.MoveTo(1, 7, 6500, 6500) }), WcMapWriter.Write(M3TestWorld.Boxes(8, 8)));
            for (int tick = 1; tick <= 100; tick++) session.StepOnce();
            for (ulong key = 1; key <= 2; key++)
                Assert.That(M3TestWorld.Position(session, key), Is.EqualTo(M3TestWorld.Point(6500, 6500)));
        }

        [Test]
        public void Squad_CaptureAndInterpolationRetainCurrentSquad()
        {
            var session = SimSession.Create(DemoRecipes.M4(), DemoRecipes.M2bMapBytes());
            session.StepOnce();
            var interpolated = FrameView.Interpolate(session.Previous, session.Current, 0.5f);
            for (int i = 0; i < 20; i++)
            {
                Assert.That(session.Current.Entities[i].Squad, Is.EqualTo(i < 10 ? 1 : 2));
                Assert.That(interpolated.Entities[i].Squad, Is.EqualTo(i < 10 ? 1 : 2));
            }
            Assert.That(new ViewEntity(1, 0, 0, 0, 0).Squad, Is.Zero);
        }

        [Test]
        public void CrowdRequiresMap_AndV4KeepsV3Payload()
        {
            Assert.That((int)MoverKind.InterimCrowd, Is.EqualTo(3));
            Assert.Throws<ArgumentException>(() => SimSession.Create(DemoRecipes.M4()));
            var session = SimSession.Create(DemoRecipes.M4(), DemoRecipes.M2bMapBytes());
            var entities = M3TestWorld.Sim(session).Entities;
            var path = new InterimLocomotion(entities, 2, WcMapNavigation.Create(M2DemoMap.Build()));
            var crowd = M3TestWorld.Locomotion(session);
            Assert.That(path.StateVersion, Is.EqualTo(3)); Assert.That(crowd.StateVersion, Is.EqualTo(4));
            var a = new SimStateWriter(); var b = new SimStateWriter();
            path.SaveState(a); crowd.SaveState(b);
            Assert.That(b.ToArray(), Is.EqualTo(a.ToArray())); Assert.That(b.ToArray().Length, Is.EqualTo(16));
            var legacy = new InterimOrders(entities, crowd);
            var spaced = new InterimOrders(entities, crowd, SimFix.FromInt(1));
            Assert.That(legacy.StateVersion, Is.EqualTo(1)); Assert.That(spaced.StateVersion, Is.EqualTo(2));
            var oldWriter = new SimStateWriter(); legacy.SaveState(oldWriter); Assert.That(oldWriter.ToArray(), Is.Empty);
            var writer = new SimStateWriter(); spaced.SaveState(writer);
            Assert.That(writer.ToArray(), Is.EqualTo(new byte[] { 0, 0, 1, 0, 0, 0, 0, 0 }));
            Assert.Throws<InvalidOperationException>(() => new InterimOrders(entities, crowd, SimFix.FromInt(2)).LoadState(new SimStateReader(writer.ToArray())));
            var h1 = new StateHash(); var h2 = new StateHash();
            spaced.WriteState(h1); new InterimOrders(entities, crowd, SimFix.FromInt(2)).WriteState(h2);
            Assert.That(h1.Value, Is.Not.EqualTo(h2.Value));
        }
    }
}
