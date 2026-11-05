using System;
using System.Globalization;
using WarCore.Sim;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification
{
    internal static class SpatialVectors
    {
        internal static void Run(Report report)
        {
            VectorJson rows = VectorJson.Parse(VectorData2.Text)["cases"];
            if (rows.Count != 733) throw new InvalidOperationException("Expected all 733 spatial vectors.");
            foreach (bool useGrid in new[] { false, true })
            {
                var digest = new StableDigest();
                for (int i = 0; i < rows.Count; i++)
                    digest.Row(i.ToString("D4", CultureInfo.InvariantCulture) + "=" + Evaluate(rows[i], useGrid));
                report.Digest(useGrid ? "spatial.grid" : "spatial.naive", digest);
            }
        }

        private static string Evaluate(VectorJson row, bool useGrid)
        {
            VectorJson data = row["body"];
            var entity = new EntityId(0, 1);
            var body = new SpatialBody(entity, MathVectors.Vector(data["position"].Integers()),
                new Collider(Fix(data["radius"]), Fix(data["height"])));
            ISpatialQuery query = useGrid ? (ISpatialQuery)Grid(body) : new NaiveSpatialQuery(new[] { body });
            VectorJson input = row["query"];
            SimVec3 origin = MathVectors.Vector(input["origin"].Integers());
            SimVec3 direction = MathVectors.Vector(input["direction"].Integers());
            var entities = new EntityId[1];
            var hits = new RayHit[1];
            string kind = row["kind"].Text;
            int count;
            switch (kind)
            {
                case "sphere": count = query.Sphere(origin, Fix(input["radius"]), entities); break;
                case "cylinder": count = query.Cylinder(origin, Fix(input["radius"]), Fix(input["halfHeight"]), entities); break;
                case "sector": count = query.Sector(origin, Fix(input["radius"]), direction, checked((int)input["cosinePermille"].Integer), Fix(input["halfHeight"]), entities); break;
                case "capsule": count = query.Capsule(origin, origin + direction, Fix(input["radius"]), entities); break;
                case "ray": count = query.Ray(origin, direction, Fix(input["range"]), hits); break;
                default: throw new InvalidOperationException("Unknown spatial kind " + kind);
            }
            if (count != (row["expectHit"].Boolean ? 1 : 0)) throw new InvalidOperationException("Spatial count: " + row["note"].Text);
            string result = kind + ";count=" + count;
            if (count != 0)
            {
                EntityId found = kind == "ray" ? hits[0].Entity : entities[0];
                if (!found.Equals(entity)) throw new InvalidOperationException("Spatial entity mismatch");
                result += ";id=" + found.Id + ";generation=" + found.Generation;
                if (kind == "ray")
                {
                    if (hits[0].Distance.Raw != row["expectDistanceRaw"].Integer) throw new InvalidOperationException("Spatial distance: " + row["note"].Text);
                    result += ";distanceRaw=" + hits[0].Distance.Raw;
                }
            }
            return result;
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
            long cell = body.Collider.Height.Raw > 8 * 65536 ? body.Collider.Height.Raw : 8 * 65536;
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(cell), 1, 64);
            grid.Rebuild();
            return grid;
        }
    }
}
