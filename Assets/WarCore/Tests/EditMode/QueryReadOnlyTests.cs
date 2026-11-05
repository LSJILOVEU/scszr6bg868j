using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class QueryReadOnlyTests
    {
        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void EveryQueryPreservesAllIndexFieldsAcrossDeathsReuseAndRestore(SpatialQueryKind kind)
        {
            var sim = new Simulation(123, config: new SimConfig(entityCapacity: 8));
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var grid = new GridSpatialQuery(sim.Entities, positions, colliders, SimFix.FromInt(8), 8, 64);
            for (int i = 0; i < 4; i++)
            {
                EntityId id = sim.Entities.Create();
                sim.Entities.Set(positions, id, new Position(SimVec3.Metres(i * 2, 0, 0)));
                sim.Entities.Set(colliders, id, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
            }
            grid.Rebuild();
            byte[] snapshot = sim.SaveSnapshot();
            var request = Request(kind);
            var entities = new EntityId[8];
            var rays = new RayHit[8];
            var hits = new SpatialHit[8];
            for (int phase = 0; phase < 4; phase++)
            {
                if (phase == 1)
                    sim.Entities.Destroy(new EntityId(1, 1));
                if (phase == 2)
                {
                    // This independent index deliberately retains its dead generation to test filtering after reuse.
                    sim.Entities.RecycleDestroyed();
                    EntityId reused = sim.Entities.Create();
                    Assert.That(reused.Id, Is.EqualTo(1));
                }
                if (phase == 3)
                {
                    sim.RestoreSnapshot(snapshot);
                    grid.Rebuild();
                    var rebuilt = new GridSpatialQuery(sim.Entities, positions, colliders, SimFix.FromInt(8), 8, 64);
                    rebuilt.Rebuild();
                    GridIndexRegressionAudit.Equal(GridIndexRegressionAudit.Logical(grid), GridIndexRegressionAudit.Logical(rebuilt), "Logically equal rebuilt index including every layer mask and large member.");
                }
                int expected = phase == 1 || phase == 2 ? 3 : 4;
                for (int mode = 0; mode < 4; mode++)
                {
                    string[] before = Capture(grid);
                    ulong authority = Fixtures.Hash(sim.Entities);
                    if (mode == 0)
                        Assert.That(Legacy(grid, in request, entities, rays), Is.EqualTo(expected));
                    else if (mode == 1)
                        Assert.That(grid.Any(in request), Is.True);
                    else if (mode == 2)
                        Assert.That(grid.Nearest(in request, out _), Is.True);
                    else
                        Assert.That(grid.AllCapped(in request, 8, hits).TotalHits, Is.EqualTo(expected));
                    Assert.That(Fixtures.Hash(sim.Entities), Is.EqualTo(authority));
                    Assert.That(Capture(grid), Is.EqualTo(before), "Index write: phase=" + phase + " mode=" + mode);
                }
            }
            sim.Entities.Destroy(new EntityId(0, 1));
            int memberships = grid.MembershipCount;
            grid.Synchronize();
            Assert.That(grid.MembershipCount, Is.LessThan(memberships), "Synchronization must reclaim even without a query.");
        }

        [Test]
        public void FieldSnapshotDetectsIndexWritesAndContextWrapDoesNotTouchIndex()
        {
            var scene = new A5GridScenario();
            scene.CreateBody(default, 1, 2);
            string[] before = Capture(scene.Grid);
            FieldInfo used = typeof(GridSpatialIndex).GetField("used", BindingFlags.NonPublic | BindingFlags.Instance);
            int previous = (int)used.GetValue(scene.Grid.Index);
            used.SetValue(scene.Grid.Index, previous + 1);
            Assert.That(Capture(scene.Grid), Is.Not.EqualTo(before));
            used.SetValue(scene.Grid.Index, previous);
            FieldInfo stamp = typeof(GridQueryContext).GetField("stamp", BindingFlags.NonPublic | BindingFlags.Instance);
            stamp.SetValue(scene.Grid.QueryContext, uint.MaxValue);
            var output = new EntityId[8];
            Assert.That(scene.Grid.Sphere(default, SimFix.FromInt(1), output), Is.EqualTo(1));
            Assert.That(Capture(scene.Grid), Is.EqualTo(before));
            Assert.That(scene.Grid.Sphere(default, SimFix.FromInt(1), output), Is.EqualTo(1));
        }

        private static SpatialRequest Request(SpatialQueryKind kind)
        {
            return new SpatialRequest(kind, SimVec3.Metres(-4, 0, 0), SimFix.FromInt(16),
                kind == SpatialQueryKind.Capsule ? SimVec3.Metres(16, 0, 0) : SimVec3.Metres(1, 0, 0),
                SimFix.FromInt(16), SimFix.FromInt(4), 0);
        }

        private static int Legacy(GridSpatialQuery grid, in SpatialRequest q, EntityId[] ids, RayHit[] hits)
        {
            switch (q.Kind)
            {
                case SpatialQueryKind.Sphere: return grid.Sphere(q.Origin, q.Radius, ids);
                case SpatialQueryKind.Cylinder: return grid.Cylinder(q.Origin, q.Radius, q.HalfHeight, ids);
                case SpatialQueryKind.Sector: return grid.Sector(q.Origin, q.Radius, q.Direction, q.Cosine, q.HalfHeight, ids);
                case SpatialQueryKind.Capsule: return grid.Capsule(q.Origin, q.Origin + q.Direction, q.Radius, ids);
                default: return grid.Ray(q.Origin, q.Direction, q.Range, hits);
            }
        }

        internal static string[] Capture(GridSpatialQuery grid)
        {
            return CaptureObject(grid.Index);
        }

        internal static string[] CaptureObject(object value)
        {
            var values = new List<string>();
            CaptureValue(value, "root", values, new HashSet<object>());
            return values.ToArray();
        }

        private static void CaptureValue(object value, string path, List<string> values, HashSet<object> visited)
        {
            if (value == null)
            {
                values.Add(path + "=null");
                return;
            }
            Type type = value.GetType();
            // Component handles also point at external authority; compare that authority separately.
            if (value is EntityStore || value is IComponents)
                return;
            if (type.IsPrimitive || type.IsEnum || value is string)
            {
                values.Add(path + "=" + Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (!type.IsValueType && !visited.Add(value))
                return;
            if (value is Array array)
            {
                values.Add(path + ".Length=" + array.Length);
                for (int i = 0; i < array.Length; i++)
                    CaptureValue(array.GetValue(i), path + "[" + i + "]", values, visited);
                return;
            }
            for (Type current = type; current != null; current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    CaptureValue(field.GetValue(value), path + "." + field.Name, values, visited);
        }
    }
}
