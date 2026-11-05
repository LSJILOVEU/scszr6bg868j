using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class HybridStorageTests
    {
        [TestCase(3)]
        [TestCase(8)]
        public void ReservedIndexPayloadForTwoThousandBodiesFitsEightMiB(int metres)
        {
            var store = new EntityStore(2000);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromInt(metres), 2000);
            Assert.That(HybridStorageAudit.Payload(new byte[4096]), Is.EqualTo(4096), "Payload counter positive control.");
            long bytes = HybridStorageAudit.Payload(grid.Index) + HybridStorageAudit.Payload(grid.QueryContext);
            TestContext.WriteLine("2bB reserved payload: entities=2000; cell={0}m; allocated_bytes={1}; limit_bytes=8388608; excludes object headers/store.", metres, bytes);
            Assert.That(bytes, Is.LessThanOrEqualTo(8L * 1024 * 1024));
        }

        [Test]
        public void SimConfigExposesEntityMemberAndCellBudgetsBeforeWarmup()
        {
            Assert.That(typeof(SimConfig).GetProperty("SpatialMembershipCapacity"), Is.Not.Null, "Missing membership budget.");
            Assert.That(typeof(SimConfig).GetProperty("SpatialCellCapacity"), Is.Not.Null, "Missing cell table budget.");
            Assert.That(typeof(SimConfig).GetProperty("SpatialMaxCellsPerBody"), Is.Not.Null, "Missing large-body threshold.");
            var config = HybridStorageAudit.Config(4, 8, 16);
            var sim = new Simulation(1, config: config);
            var p = sim.Entities.Register<Position>("position");
            var c = sim.Entities.Register<Collider>("collider");
            sim.ConfigureSpatial(p, c, SimFix.FromInt(3));
            Assert.That(HybridStorageAudit.Number(sim.SpatialIndex, "MembershipCapacity"), Is.EqualTo(8));
            Assert.That(HybridStorageAudit.Number(sim.SpatialIndex, "CellCapacity"), Is.EqualTo(16));
        }

        [Test]
        public void ExhaustedSynchronizationRejectsBeforePublishingAnyBody()
        {
            var sim = new Simulation(1, config: new SimConfig(entityCapacity: 4));
            var p = sim.Entities.Register<Position>("position");
            var c = sim.Entities.Register<Collider>("collider");
            var view = (SpatialQueryView)sim.ConfigureSpatial(p, c, SimFix.FromInt(8), 1);
            var grid = new GridSpatialQuery(sim.SpatialIndex, view.QueryContext);
            EntityId first = Add(sim.Entities, p, c, SimVec3.Metres(1, 1, 1));
            sim.CompleteSpatialBoundary();
            string[] before = GridIndexRegressionAudit.Logical(grid);
            sim.Entities.Set(p, first, new Position(SimVec3.Metres(17, 1, 1)));
            Add(sim.Entities, p, c, SimVec3.Metres(33, 1, 1));
            Exception error = Assert.Catch(() => sim.CompleteSpatialBoundary());
            Assert.That(error, Is.InstanceOf<SimBoundaryException>());
            Assert.That(error.Message, Does.Contain("membership").And.Contain("1"));
            GridIndexRegressionAudit.Equal(before, GridIndexRegressionAudit.Logical(grid), "Failed sync published a partial index.");
            Assert.That(grid.Sphere(SimVec3.Metres(1, 1, 1), default, new EntityId[4]), Is.EqualTo(1));
        }

        [Test]
        public void LargeBodyBudgetAvoidsOverflowAndBypassesLeafMemberships()
        {
            var store = new EntityStore(1);
            var p = store.Register<Position>("position");
            var c = store.Register<Collider>("collider");
            EntityId id = Add(store, p, c, default);
            store.Set(c, id, new Collider(SimFix.FromInt(16384), SimFix.FromInt(32768)));
            var grid = new GridSpatialQuery(store, p, c, SimFix.FromRaw(1), 1, 8);
            Assert.DoesNotThrow(() => grid.Upsert(id));
            Assert.That(HybridStorageAudit.Number(grid.Index, "LargeBodyCount"), Is.EqualTo(1));
            Assert.That(grid.Sphere(default, default, new EntityId[1]), Is.EqualTo(1));
        }

        [Test]
        public void LazyMasksEqualMemberUnionAfterLayerMoveAndDeath()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId a = scene.CreateBody(SimVec3.Metres(1, 1, 1), 0, 0);
            EntityId b = scene.CreateBody(SimVec3.Metres(2, 1, 1), 0, 0);
            scene.InitializeIndex();
            HybridStorageAudit.Masks(scene.Grid.Index);
            scene.Store.Set(scene.Colliders, a, new Collider(default, default, 0x80000000u));
            scene.Sim.CompleteSpatialBoundary();
            HybridStorageAudit.Masks(scene.Grid.Index);
            scene.Store.Set(scene.Positions, a, new Position(SimVec3.Metres(-65, 1, 1)));
            scene.Sim.CompleteSpatialBoundary();
            HybridStorageAudit.Masks(scene.Grid.Index);
            scene.Store.Destroy(a);
            scene.Sim.CompleteSpatialBoundary();
            HybridStorageAudit.Masks(scene.Grid.Index);
            scene.Store.Set(scene.Colliders, b, new Collider(default, default, 0u));
            scene.Sim.CompleteSpatialBoundary();
            HybridStorageAudit.Masks(scene.Grid.Index);
            Assert.That(HybridStorageAudit.Number(scene.Grid.Index, "LayerUnion"), Is.Zero);
        }

        [TestCase(1000, false)]
        [TestCase(5000, false)]
        [TestCase(16384, false)]
        [TestCase(1000, true)]
        [TestCase(5000, true)]
        [TestCase(16384, true)]
        public void SparseWorldQueriesBoundMembersAndEmptyProbes(int range, bool ray)
        {
            var store = new EntityStore(2000);
            var p = store.Register<Position>("position");
            var c = store.Register<Collider>("collider");
            var rng = new A1MathInputs(0xB30002);
            var bodies = new List<SpatialBody>();
            for (int i = 0; i < 2000; i++)
            {
                SimVec3 point = SimVec3.Metres((long)(rng.Next() % 32001) - 16000,
                    (long)(rng.Next() % 32001) - 16000, (long)(rng.Next() % 32001) - 16000);
                EntityId id = Add(store, p, c, point);
                var collider = new Collider(SimFix.FromRaw(16384), SimFix.FromRaw(196608));
                store.Set(c, id, collider);
                bodies.Add(new SpatialBody(id, point, collider));
            }
            var grid = new GridSpatialQuery(store, p, c, SimFix.FromInt(3), 2000);
            grid.Rebuild();
            var counters = new SpatialQueryDiagnostics();
            grid.Diagnostics = counters;
            SpatialRequest request = ray ? SpatialQueries.Ray(SimVec3.Metres(-16000, -16000, -16000), SimVec3.Metres(1, 0, 0), SimFix.FromInt(range))
                : SpatialQueries.Sphere(SimVec3.Metres(-16000, -16000, -16000), SimFix.FromInt(range));
            string expected = GridOwnershipTestFixture.Results(new NaiveSpatialQuery(bodies), in request, 2000);
            GridOwnershipTestFixture.EqualResults(expected, GridOwnershipTestFixture.Results(grid, in request, 2000), "Sparse output.");
            TestContext.WriteLine("2bB sparse: seed=0xB30002; range_m={0}; ray={1}; members={2}; lookups={3}; candidates={4}; exclusions=none.",
                range, ray, counters.LeafMembers, counters.DictionaryLookups, grid.LastCandidateCount);
            Assert.That(counters.LeafMembers, Is.LessThan(1600));
            Assert.That(counters.DictionaryLookups, Is.LessThanOrEqualTo(128));
            Assert.That(counters.EmptyLookups, Is.LessThanOrEqualTo(128));
            Assert.That(counters.DirectorySlotsScanned, Is.Zero, "Metre-scale Hybrid queries must not scan the directory.");
            TestContext.WriteLine("2bB probes: empty={0}; hash_slots={1}; directory_scan={2}.", counters.EmptyLookups, counters.HashProbes, counters.DirectorySlotsScanned);
        }

        internal static EntityId Add(EntityStore store, ComponentType<Position> p, ComponentType<Collider> c, SimVec3 point)
        {
            EntityId id = store.Create();
            store.Set(p, id, new Position(point));
            store.Set(c, id, new Collider(default, default));
            return id;
        }
    }

    internal static class HybridStorageAudit
    {
        internal static long Payload(object value) => Payload(value, new HashSet<object>());

        private static long Payload(object value, HashSet<object> seen)
        {
            if (value == null || value is EntityStore || value is IComponents || value is string || !seen.Add(value))
                return 0;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum)
                return 0;
            if (value is Array array)
            {
                Type element = type.GetElementType();
                if (element.IsValueType)
                {
                    var method = new DynamicMethod("ElementSize", typeof(int), Type.EmptyTypes, typeof(HybridStorageAudit).Module, true);
                    ILGenerator il = method.GetILGenerator();
                    il.Emit(OpCodes.Sizeof, element);
                    il.Emit(OpCodes.Ret);
                    return array.LongLength * ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
                }
                long total = array.LongLength * IntPtr.Size;
                foreach (object child in array)
                    total += Payload(child, seen);
                return total;
            }
            long bytes = 0;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                bytes += Payload(field.GetValue(value), seen);
            return bytes;
        }

        internal static long Number(object value, string name)
        {
            PropertyInfo property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, "Missing Hybrid capability: " + name);
            return Convert.ToInt64(property.GetValue(value));
        }

        internal static SimConfig Config(int entities, int members, int cells)
        {
            ConstructorInfo ctor = typeof(SimConfig).GetConstructors()[0];
            ParameterInfo[] parameters = ctor.GetParameters();
            var values = new object[parameters.Length];
            for (int i = 0; i < values.Length; i++)
                values[i] = parameters[i].Name == "entityCapacity" ? entities : parameters[i].Name == "spatialMembershipCapacity" ? members
                    : parameters[i].Name == "spatialCellCapacity" ? cells : parameters[i].DefaultValue;
            return (SimConfig)ctor.Invoke(values);
        }

        internal static void Masks(GridSpatialIndex index)
        {
            object state = GridIndexRegressionAudit.Field(index, "active");
            var bodies = (SpatialBodyColumns)GridIndexRegressionAudit.Field(state, "Bodies");
            var table = GridIndexRegressionAudit.Field(GridIndexRegressionAudit.Field(state, "Unit"), "Table");
            var cells = (Array)GridIndexRegressionAudit.Field(table, "Cells");
            var members = (int[])GridIndexRegressionAudit.Field(table, "Members");
            uint union = 0;
            int occupied = 0;
            foreach (object cell in cells)
            {
                int count = (int)GridIndexRegressionAudit.Field(cell, "Count");
                if (count == 0)
                    continue;
                occupied++;
                int start = (int)GridIndexRegressionAudit.Field(cell, "Start");
                uint expected = 0;
                for (int i = 0; i < count; i++)
                    expected |= bodies[members[start + i]].Collider.Layer;
                Assert.That(GridIndexRegressionAudit.Field(cell, "Mask"), Is.EqualTo(expected), "Mask must equal the exact member union.");
                union |= expected;
            }
            Assert.That(occupied, Is.GreaterThan(0), "Nonvacuous mask fixture.");
            Assert.That(Number(index, "LayerUnion"), Is.EqualTo(union));
        }
    }
}
