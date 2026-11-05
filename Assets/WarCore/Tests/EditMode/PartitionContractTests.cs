using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("Partitions")]
    public sealed class PartitionContractTests
    {
        // Reflection lets the red tests compile against the pre-partition public API.
        internal static Collider Static(Collider value, bool flag)
        {
            MethodInfo method = typeof(Collider).GetMethod("WithStatic", new[] { typeof(bool) });
            Assert.That(method, Is.Not.Null, "Collider must own the runtime static flag.");
            return (Collider)method.Invoke(value, new object[] { flag });
        }

        internal static bool IsStatic(Collider value)
        {
            PropertyInfo property = typeof(Collider).GetProperty("IsStatic");
            Assert.That(property, Is.Not.Null);
            return (bool)property.GetValue(value);
        }

        internal static long Counter(object value, string name)
        {
            FieldInfo field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, "Missing partition work counter " + name);
            return Convert.ToInt64(field.GetValue(value));
        }

        internal static EntityId Add(GridOwnershipTestFixture scene, int x, uint layer, bool stationary, int radius = 0)
        {
            EntityId entity = scene.CreateBody(SimVec3.Metres(x, 0, 0), radius, radius * 2);
            scene.Store.Set(scene.Colliders, entity, Static(scene.Store.Read(scene.Colliders, entity).WithLayer(layer), stationary));
            return entity;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaticFlagHasVersionedExactBytesAndCompatibilityOmitsTheByte(bool flag)
        {
            Collider body = Static(new Collider(SimFix.FromInt(1), SimFix.FromInt(2), 0x80000000u), flag);
            Assert.That(IsStatic(default), Is.False);
            Assert.That(IsStatic(body.WithLayer(uint.MaxValue)), Is.EqualTo(flag));
            Assert.That(IsStatic(Static(body, !flag)), Is.EqualTo(!flag));
            var compatible = new StateHash(false);
            body.WriteState(compatible);
            var oldBytes = new StateHash();
            oldBytes.Add(0);
            oldBytes.Add(SimFix.Scale);
            oldBytes.Add(2 * SimFix.Scale);
            oldBytes.Add(0x80000000UL);
            Assert.That(compatible.Value, Is.EqualTo(oldBytes.Value));
            oldBytes.Add(flag);
            var current = new StateHash();
            body.WriteState(current);
            Assert.That(current.Value, Is.EqualTo(oldBytes.Value));
            Assert.That(current.Value, Is.EqualTo(flag ? 0xE1EFB953EBEB6C6DUL : 0xE1EFB853EBEB6ABAUL));
            Assert.That(current.Value, Is.Not.EqualTo(compatible.Value));
            var writer = new SimStateWriter();
            var serializer = new ColliderSerializer();
            serializer.Write(writer, in body);
            byte[] bytes = writer.ToArray();
            Assert.That(serializer.Version, Is.EqualTo(3));
            Assert.That(Simulation.SnapshotVersion, Is.EqualTo(4));
            Assert.That(bytes.Length, Is.EqualTo(29));
            Assert.That(bytes[28], Is.EqualTo(flag ? 1 : 0));
            var reader = new SimStateReader(bytes);
            Collider copy = serializer.Read(reader);
            reader.End();
            Assert.That(IsStatic(copy), Is.EqualTo(flag));
            Assert.That(copy.Layer, Is.EqualTo(0x80000000u));
            bytes[28] = 2;
            Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(bytes)));
        }

        [Test]
        public void StaticOnlySightSkipsAllUnitWorkAndDynamicBlockersInvalidateThatShortcut()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId wall = Add(scene, 20, 2, true);
            EntityId unit = Add(scene, 4, 1, false);
            // A nearby static miss proves partition work even when the nearer unit permits
            // a valid global stop before the distant wall's cell.
            EntityId staticMiss = scene.CreateBody(SimVec3.Metres(2, 0, 1), 0, 0);
            scene.Store.Set(scene.Colliders, staticMiss, Static(scene.Store.Read(scene.Colliders, staticMiss).WithLayer(2), true));
            scene.InitializeIndex();
            var counters = new SpatialQueryDiagnostics();
            scene.Grid.Diagnostics = counters;
            var ray = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(40), 2);
            Assert.That(scene.Grid.Nearest(in ray, out SpatialHit hit), Is.True);
            Assert.That(hit.Entity, Is.EqualTo(wall));
            Assert.That(Counter(counters, "StaticMembers"), Is.GreaterThan(0));
            Assert.That(Counter(counters, "UnitMembers"), Is.Zero);
            Assert.That(Counter(counters, "SkippedPartitions"), Is.EqualTo(1));
            scene.Store.Set(scene.Colliders, unit, scene.Store.Read(scene.Colliders, unit).WithLayer(2));
            scene.Sim.CompleteSpatialBoundary();
            Assert.That(scene.Grid.Nearest(in ray, out hit), Is.True);
            Assert.That(hit.Entity, Is.EqualTo(unit));
            Assert.That(Counter(counters, "UnitMembers"), Is.GreaterThan(0), "Positive control: dynamic blocking layer must be visited.");
            Assert.That(Counter(counters, "StaticMembers"), Is.GreaterThan(0));
            GridOwnershipTestFixture.EqualResults(GridOwnershipTestFixture.Results(scene.Naive(), in ray),
                GridOwnershipTestFixture.Results(scene.Grid, in ray), "Dynamic blocker oracle.");
            scene.Store.Destroy(unit);
            scene.Sim.CompleteSpatialBoundary();
            Assert.That(scene.Grid.Any(in ray), Is.True);
            Assert.That(Counter(counters, "UnitMembers"), Is.Zero);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void NearestSharesBestButMustSearchTheOtherPartitionAndLargeList(bool nearStatic, bool large)
        {
            var scene = new GridOwnershipTestFixture();
            EntityId near = Add(scene, large ? 33 : 5, 2, nearStatic, large ? 32 : 0);
            Add(scene, 1024, 2, !nearStatic);
            scene.InitializeIndex();
            var ray = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(5000), 2);
            Assert.That(scene.Grid.Nearest(in ray, out SpatialHit hit), Is.True);
            Assert.That(hit.Entity, Is.EqualTo(near));
            Assert.That(hit.Distance.Raw, Is.EqualTo((large ? 1 : 5) * SimFix.Scale));
            GridOwnershipTestFixture.EqualResults(GridOwnershipTestFixture.Results(scene.Naive(), in ray),
                GridOwnershipTestFixture.Results(scene.Grid, in ray), "Cross-partition nearest.");
        }

        [Test]
        public void EqualRawHitsInTheSecondPartitionStillUseTheSmallerEntityId()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId first = Add(scene, 5, 2, false);
            Add(scene, 5, 2, true);
            scene.InitializeIndex();
            var ray = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(40), 2);
            Assert.That(scene.Grid.Nearest(in ray, out SpatialHit hit), Is.True);
            Assert.That(hit.Entity, Is.EqualTo(first));
            var output = new SpatialHit[1];
            SpatialQueryResult result = scene.Grid.AllCapped(in ray, 1, output);
            Assert.That(result.TotalHits, Is.EqualTo(2));
            Assert.That(result.Truncated, Is.True);
            Assert.That(output[0].Entity, Is.EqualTo(first));
        }

        [Test]
        public void StaticRequestsPublishAtSyncAndSurviveSnapshotBeforeSettlement()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = Add(scene, 5, 2, false);
            scene.InitializeIndex();
            MethodInfo request = typeof(Simulation).GetMethod("RequestColliderStatic");
            Assert.That(request, Is.Not.Null, "Runtime changes must be requests, not query mutations.");
            request.Invoke(scene.Sim, new object[] { entity, true });
            Assert.That(IsStatic(scene.Store.Read(scene.Colliders, entity)), Is.False);
            byte[] pending = scene.Sim.SaveSnapshot();
            scene.Sim.Step(default);
            Assert.That(IsStatic(scene.Store.Read(scene.Colliders, entity)), Is.True);
            ulong expected = scene.Sim.StateHash();
            string[] logical = GridIndexRegressionAudit.Logical(scene.Grid);
            scene.Sim.RestoreSnapshot(pending);
            Assert.That(IsStatic(scene.Store.Read(scene.Colliders, entity)), Is.False);
            scene.Sim.Step(default);
            Assert.That(scene.Sim.StateHash(), Is.EqualTo(expected));
            GridIndexRegressionAudit.Equal(logical, GridIndexRegressionAudit.Logical(scene.Grid), "Restored request and both indexes.");
        }

        [Test]
        public void EachPartitionHasAnIndependentReservedBudget()
        {
            Assert.That(typeof(SimConfig).GetProperty("StaticSpatialMembershipCapacity"), Is.Not.Null);
            Assert.That(typeof(SimConfig).GetProperty("StaticSpatialCellCapacity"), Is.Not.Null);
        }
    }
}
