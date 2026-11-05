using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class StableLoadHealthSerializer : IStableComponentSerializer<Health>
    {
        public int TypeId => 91001;
        public int Version => 1;
        public void Write(SimStateWriter writer,in Health value) => writer.Int32(value.Value);
        public Health Read(SimStateReader reader) => new Health {Value=reader.Int32()};
    }
    internal sealed class LoadSystem : ISimSystem, IStableStateSerializer
    {
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Health> healths;
        private readonly RandomStreamId random;
        private readonly GridSpatialQuery grid;
        private readonly EntityId[] queryResults;
        private readonly RayHit[] rayResults;
        private EntityId first;
        private readonly EntityStore store;
        private readonly EntityStage stage;
        private long appliedTasks;
        public int Order => 1;
        public long AppliedTasks => appliedTasks;
        public string StateTypeId => "test.load";
        public int StateVersion => 1;
        public void SaveState(SimStateWriter writer)
        {
            writer.Int64(appliedTasks);
            if (writer.IsStable) writer.Link(store.Link(first));
            else SnapshotValues.Entity(writer, first);
        }

        public void LoadState(SimStateReader reader)
        {
            appliedTasks = reader.Int64();
            if (reader.IsStable) first = store.Resolve(reader.Link());
            else SimStateReader.Require(SnapshotValues.Entity(reader).Equals(first), "Load target configuration mismatch.");

        }
        internal LoadSystem(Simulation sim, int count)
        {
            store = sim.Entities; stage = new EntityStage(store);
            positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            healths = sim.Entities.Register<Health>("health", store.IdentityMode == IdentityMode.StableWorld ? (IComponentSerializer<Health>)new StableLoadHealthSerializer() : new HealthSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            sim.Entities.Reserve(count);
            for (int i = 0; i < count; i++)
            {
                EntityId entity = sim.Entities.IdentityMode == IdentityMode.Legacy ? sim.Entities.Create() : (EntityId)sim.Entities.Create(StableEntityKey.Placed(1,(ulong)i));
                if (i == 0)
                    first = entity;
                sim.Entities.Set(positions, entity, new Position(SimVec3.Metres((i % 50) * 8, 0, (i / 50) * 8)));
                sim.Entities.Set(healths, entity, new Health{Value = 100000});
                sim.Entities.Set(colliders, entity, new Collider(SimFix.Ratio(1, 2), SimFix.FromInt(2)));
            }

            var view = (SpatialQueryView)sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(8), count * 12 + 128);
            grid = new GridSpatialQuery(sim.SpatialIndex, view.QueryContext);
            sim.CompleteSpatialBoundary();
            queryResults = new EntityId[count];
            rayResults = new RayHit[count];
            random = sim.Rng.Stream("load");
        }

        public void Step(SimContext context)
        {
            if (store.IdentityMode == IdentityMode.StableWorld)
                while (context.Scheduler.TryDequeueStable(out StableScheduledTask stableTask))
                {
                    context.Entities.Get(healths,store.Resolve(stableTask.Event.Target)).Value += stableTask.Event.Tag;
                    appliedTasks++;
                }
            else while (context.Scheduler.TryDequeue(out ScheduledTask task))
            {
                context.Entities.Get(healths, task.Event.Target).Value += task.Event.Tag;
                appliedTasks++;
            }

            foreach (EntityId entity in stage.Capture())
            {
                ref Health health = ref context.Entities.Get(healths, entity);
                health.Value -= context.Rng.NextInt(random, 3);
                ref Position position = ref context.Entities.Get(positions, entity);
                position.Value += new SimVec3(SimFix.FromRaw(context.Clock.Tick % 2 == 0 ? -1 : 1), default, default);
            }

            for (int i = 0; i < 4; i++)
                if (store.IdentityMode == IdentityMode.StableWorld)
                    context.Scheduler.ScheduleFor(context.Clock.Tick+2,0,store.StableOf(first),30,
                        new StableSimEvent(new EventTypeId(30),target:store.Link(first),tag:i+1));
                else context.Scheduler.Schedule(context.Clock.Tick + 2, new SimEvent(new EventTypeId(30), target: first, tag: i + 1));
            for (int i = 0; i < 16; i++)
                if (store.IdentityMode == IdentityMode.StableWorld)
                    context.Emit(new StableSimEvent(new EventTypeId(31),source:store.Link(first),tag:i));
                else context.Emit(new SimEvent(new EventTypeId(31), source: first, tag: i));
            grid.Sphere(default, SimFix.FromInt(3), queryResults);
            grid.Cylinder(default, SimFix.FromInt(3), SimFix.FromInt(2), queryResults);
            grid.Sector(default, SimFix.FromInt(10), SimVec3.Metres(1, 0, 0), 600, SimFix.FromInt(2), queryResults);
            grid.Capsule(default, SimVec3.Metres(8, 0, 0), SimFix.FromInt(1), queryResults);
            grid.Ray(default, SimVec3.Metres(1, 0, 0), SimFix.FromInt(10), rayResults);
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(appliedTasks);
        }
    }
}
