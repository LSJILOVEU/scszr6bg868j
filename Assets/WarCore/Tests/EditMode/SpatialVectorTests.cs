using System.Collections;
using System.Globalization;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class SpatialVectorTests
    {
        public static IEnumerable Cases
        {
            get
            {
                VectorJson rows = VectorJson.Load("spatial-narrowphase-vectors.json")["cases"];
                for (int i = 0; i < rows.Count; i++)
                {
                    VectorJson row = rows[i];
                    foreach (bool grid in new[]{false, true})
                        yield return new TestCaseData(grid, row)
                            .SetName("Spatial_" + (grid ? "Grid" : "Naive") + "." + row["kind"].Text + "[" + i.ToString("D4", CultureInfo.InvariantCulture) + "]")
                            .SetDescription(row["note"].Text + "; nearBoundary=" + row["nearBoundary"].Boolean + "; exactTangent=" + row["exactTangent"].Boolean);
                }
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void EveryGeometryVectorMatchesIncludingNearBoundary(bool useGrid, object record)
        {
            VectorJson row = (VectorJson)record;
            VectorJson bodyData = row["body"];
            var entity = new EntityId(0, 1);
            var body = new SpatialBody(entity, MathVectorChecks.Vector(bodyData["position"].Integers()),
                new Collider(Fix(bodyData["radius"]), Fix(bodyData["height"])));
            ISpatialQuery query = useGrid ? (ISpatialQuery)Grid(body) : new NaiveSpatialQuery(new[]{body});
            VectorJson input = row["query"];
            SimVec3 origin = MathVectorChecks.Vector(input["origin"].Integers());
            SimVec3 direction = MathVectorChecks.Vector(input["direction"].Integers());
            var entities = new EntityId[1];
            var hits = new RayHit[1];
            int count;
            switch (row["kind"].Text)
            {
                case "sphere":
                    count = query.Sphere(origin, Fix(input["radius"]), entities);
                    break;
                case "cylinder":
                    count = query.Cylinder(origin, Fix(input["radius"]), Fix(input["halfHeight"]), entities);
                    break;
                case "sector":
                    count = query.Sector(origin, Fix(input["radius"]), direction, checked((int)input["cosinePermille"].Integer), Fix(input["halfHeight"]), entities);
                    break;
                case "capsule":
                    count = query.Capsule(origin, origin + direction, Fix(input["radius"]), entities);
                    break;
                case "ray":
                    count = query.Ray(origin, direction, Fix(input["range"]), hits);
                    break;
                default:
                    throw new System.InvalidOperationException("Unknown spatial vector kind: " + row["kind"].Text);
            }

            Assert.That(count, Is.EqualTo(row["expectHit"].Boolean ? 1 : 0), row["note"].Text);
            if (count == 1)
            {
                bool ray = row["kind"].Text == "ray";
                Assert.That(ray ? hits[0].Entity : entities[0], Is.EqualTo(entity));
                if (ray)
                    Assert.That(hits[0].Distance.Raw, Is.EqualTo(row["expectDistanceRaw"].Integer), row["note"].Text);
            }
        }

        private static SimFix Fix(VectorJson value) => SimFix.FromRaw(value.Integer);

        private static GridSpatialQuery Grid(SpatialBody body)
        {
            var store = new EntityStore(1);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            EntityId id = store.Create();
            store.Set(positions, id, new Position(body.Position));
            store.Set(colliders, id, body.Collider);
            // Large fixtures must fit the membership budget without changing any input geometry.
            long cell = body.Collider.Height.Raw > 8 * 65536 ? body.Collider.Height.Raw : 8 * 65536;
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(cell), 1, 64);
            grid.Rebuild();
            return grid;
        }
    }
}
