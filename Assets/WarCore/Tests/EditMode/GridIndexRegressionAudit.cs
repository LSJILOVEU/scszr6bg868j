using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Compare logical membership and masks at every level, independent of transaction-bank history.
    internal static class GridIndexRegressionAudit
    {
        internal static object Field(object value, string name)
        {
            if (value is GridSpatialQuery adapter)
                value = adapter.Index;
            FieldInfo field = value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Audit adapter must be updated explicitly for field " + name);
            return field.GetValue(value);
        }

        private static string CellKey(object cell)
        {
            return Field(cell, "Level") + ":" + Field(cell, "X") + ":" + Field(cell, "Y") + ":" + Field(cell, "Z");
        }

        internal static string[] Logical(GridSpatialQuery grid)
        {
            var rows = new List<string>();
            SpatialGridState state = (SpatialGridState)Field(grid, "active");
            rows.Add("memberships=" + grid.MembershipCount);
            rows.Add("union=" + state.LayerUnion);
            PartitionRows(rows, state, state.Static, true);
            PartitionRows(rows, state, state.Unit, false);
            for (int slot = 0; slot < state.Bodies.Length; slot++)
            {
                if (!state.Present[slot])
                    continue;
                SpatialBody body = state.Bodies[slot];
                rows.Add("body=" + body.Entity.Id + ":" + body.Entity.Generation + ";position="
                    + body.Position.X.Raw + ":" + body.Position.Y.Raw + ":" + body.Position.Z.Raw
                    + ";shape=" + body.Collider.ShapeKind + ":" + body.Collider.Radius.Raw + ":" + body.Collider.Height.Raw
                    + ":" + body.Collider.HalfDepth.Raw + ":" + body.Collider.Yaw.M + ":" + body.Collider.Yaw.N
                    + ";layer=" + body.Collider.Layer + ";static=" + body.Collider.IsStatic);
            }
            // Physical hash slots and spare-bank history cannot affect any simulation output.
            rows.Sort(StringComparer.Ordinal);
            return rows.ToArray();
        }

        private static void PartitionRows(List<string> rows, SpatialGridState state, SpatialPartitionState partition, bool stationary)
        {
            SpatialCellTable table = partition.Table;
            uint union = 0;
            int bodies = 0;
            for (int i = 0; i < state.Bodies.Length; i++)
                if (state.Present[i] && state.Bodies.Colliders[i].IsStatic == stationary)
                {
                    union |= state.Bodies.Colliders[i].Layer;
                    bodies++;
                }
            Assert.That(partition.LayerUnion, Is.EqualTo(union), "Exact partition mask.");
            Assert.That(partition.BodyCount, Is.EqualTo(bodies));
            string prefix = stationary ? "static:" : "unit:";
            rows.Add(prefix + "union=" + union + ";bodies=" + bodies);
            for (int i = 0; i < table.Count; i++)
            {
                SpatialCell cell = table.Cells[table.Occupied[i]];
                var members = new List<string>();
                uint mask = 0;
                for (int j = 0; j < cell.Count; j++)
                {
                    int slot = table.Members[cell.Start + j];
                    Assert.That(slot, Is.InRange(0, state.Bodies.Length - 1));
                    Assert.That(state.Present[slot], Is.True);
                    Assert.That(state.Bodies.Colliders[slot].IsStatic, Is.EqualTo(stationary));
                    mask |= state.Bodies.Colliders[slot].Layer;
                    EntityId entity = state.Bodies.Entities[slot];
                    members.Add(entity.Id + ":" + entity.Generation);
                }
                Assert.That(cell.Mask, Is.EqualTo(mask), "Exact cell mask.");
                members.Sort(StringComparer.Ordinal);
                rows.Add(prefix + "cell=" + CellKey(cell) + ";count=" + cell.Count + ";mask=" + cell.Mask + ";members=" + string.Join(",", members));
            }
            for (int i = 0; i < partition.LargeCount; i++)
            {
                int slot = partition.LargeIds[i];
                Assert.That(state.Present[slot], Is.True);
                Assert.That(state.Bodies.Colliders[slot].IsStatic, Is.EqualTo(stationary));
                EntityId entity = state.Bodies.Entities[slot];
                rows.Add(prefix + "large=" + entity.Id + ":" + entity.Generation);
            }
        }

        internal static void Equal(string[] expected, string[] actual, string reason)
        {
            CollectionAssert.AreEqual(expected, actual, reason);
        }

        internal static void Unchanged(string[] before, GridSpatialQuery grid)
        {
            Equal(before, QueryReadOnlyTests.Capture(grid), "Query changed index state (context is external scratch).");
        }

        internal static void NoQueryFields(Type type)
        {
            var forbidden = new List<string>();
            for (Type current = type; current != null; current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    string name = field.Name.ToLowerInvariant();
                    if (typeof(GridQueryContext).IsAssignableFrom(field.FieldType) || name.Contains("stamp")
                        || name.Contains("seen") || name.Contains("last") || name.Contains("diagnostic"))
                        forbidden.Add(field.Name);
                }
            Assert.That(forbidden, Is.Empty, "Shared index must not retain a query context or last-query state.");
        }

        internal static void SimHasQuerySurface(Type type)
        {
            bool found = false;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                found |= typeof(ISpatialQuery).IsAssignableFrom(property.PropertyType);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                found |= typeof(ISpatialQuery).IsAssignableFrom(method.ReturnType);
            Assert.That(found, Is.True, "Sim must expose its owned spatial query through a public read interface.");
        }
    }
}
