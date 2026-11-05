using WarCore.Sim;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification
{
    internal sealed class ProfilerSpatialWorkload
    {
        private readonly string name;
        private readonly EntityStore entities = new EntityStore(2000);
        private readonly ComponentType<Position> positions;
        private readonly GridSpatialQuery grid;
        private readonly EntityId[] hits = new EntityId[2000];
        private readonly RayHit[] rays = new RayHit[2000];
        private long hitCount;
        internal long QueryCalls;
        internal int OperationsPerFrame => name == "move-grid" ? 2000 : 1;

        internal ProfilerSpatialWorkload(string selected)
        {
            name = selected;
            positions = entities.Register<Position>("position", new PositionSerializer());
            var colliders = entities.Register<Collider>("collider", new ColliderSerializer());
            for (int i = 0; i < 2000; i++)
            {
                EntityId entity = entities.Create();
                entities.Set(positions, entity, new Position(SimVec3.Metres(i % 50 * 8, 0, i / 50 * 8)));
                entities.Set(colliders, entity, new Collider(SimFix.Ratio(1, 2), SimFix.FromInt(2)));
            }
            grid = new GridSpatialQuery(entities, positions, colliders, SimFix.FromInt(8), 2000, 24128);
            grid.Rebuild();
            entities.CompleteWarmup();
        }

        internal void Step(int tick)
        {
            int found;
            switch (name)
            {
                case "ray": found = grid.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(10), rays); break;
                case "sphere": found = grid.Sphere(default, SimFix.FromInt(3), hits); break;
                case "cylinder": found = grid.Cylinder(default, SimFix.FromInt(3), SimFix.FromInt(2), hits); break;
                case "capsule": found = grid.Capsule(default, SimVec3.Metres(8, 0, 0), SimFix.FromInt(1), hits); break;
                case "sector": found = grid.Sector(default, SimFix.FromInt(10), SimVec3.Metres(1, 0, 0), 600, SimFix.FromInt(2), hits); break;
                default:
                    foreach (EntityId entity in entities)
                    {
                        entities.Get(positions, entity).Value += new SimVec3(SimFix.FromRaw(tick % 2 == 0 ? -1 : 1), default, default);
                        grid.Upsert(entity);
                    }
                    return;
            }
            hitCount += found;
            QueryCalls++;
        }

        internal void Verify(int frames)
        {
            HashScenarios.Require(QueryCalls == (name == "move-grid" ? 0 : frames), "Spatial bisect calls missing.");
            int expected = name == "ray" || name == "capsule" || name == "sector" ? 2 : 1;
            if (name != "move-grid") HashScenarios.Require(hitCount == frames * expected, "Spatial bisect outputs changed.");
            else HashScenarios.Require(entities.Get(positions, new EntityId(0, 1)).Value.X.Raw == frames % 2, "Spatial movement changed.");
        }
    }
}
