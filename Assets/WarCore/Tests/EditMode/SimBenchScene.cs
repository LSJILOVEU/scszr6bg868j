using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public enum SimBenchWorkload
    {
        Light,
        Heavy,
        DenseAimedRays,
        SphereOnly
    }

    internal readonly struct BenchQuery
    {
        internal readonly int Kind;
        internal readonly SimVec3 Origin;
        internal readonly SimVec3 Direction;
        internal readonly SimFix Radius;
        internal BenchQuery(int kind, SimVec3 origin, SimVec3 direction, SimFix radius)
        {
            Kind = kind;
            Origin = origin;
            Direction = direction;
            Radius = radius;
        }

        internal int Run(ISpatialQuery query, EntityId[] entities, RayHit[] rays)
        {
            if (Kind == 0)
                return query.Ray(Origin, Direction, Radius, rays);
            if (Kind == 1)
                return query.Sphere(Origin, Radius, entities);
            return query.Sector(Origin, Radius, Direction, 600, SimFix.FromInt(2), entities);
        }
    }

    internal sealed class SimBenchScene : ISimSystem
    {
        internal readonly Simulation Sim;
        internal readonly GridSpatialQuery Grid;
        internal readonly BenchQuery[] Queries;
        internal readonly EntityId[] Entities;
        internal readonly RayHit[] Rays;
        internal readonly int[] Counts;
        internal readonly ulong[] Signatures;
        internal long Calls;
        internal long RayCalls;
        internal long NarrowCandidates;
        internal long BroadCandidates;
        internal long TotalHits;
        internal ulong Checksum;
        public int Order => 1;

        internal SimBenchScene(int count, SimBenchWorkload workload)
        {
            SimBench.Validate(count, 1);
            if (workload < SimBenchWorkload.Heavy || workload > SimBenchWorkload.SphereOnly)
                throw new ArgumentOutOfRangeException(nameof(workload));
            Sim = new Simulation(0xB20002, config: new SimConfig(4, 16, 16, count, 16));
            var positions = Sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var bodies = new SpatialBody[count];
            bool dense = workload == SimBenchWorkload.DenseAimedRays;
            int columns = count < 50 ? count : 50;
            long spacing = dense ? SimFix.Scale * 3 / 4 : SimFix.Scale * 3;
            for (int i = 0; i < count; i++)
            {
                EntityId entity = Sim.Entities.Create();
                var position = new SimVec3(SimFix.FromRaw((i % columns) * spacing), SimFix.Ratio(3, 2), SimFix.FromRaw((i / columns) * spacing));
                var collider = new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(3));
                Sim.Entities.Set(positions, entity, new Position(position));
                Sim.Entities.Set(colliders, entity, collider);
                bodies[i] = new SpatialBody(entity, position, collider);
            }

            var view = (SpatialQueryView)Sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(3), count * 16 + 128);
            Grid = new GridSpatialQuery(Sim.SpatialIndex, view.QueryContext);
            Sim.CompleteSpatialBoundary();
            int rayCount = workload == SimBenchWorkload.SphereOnly ? 0 : count;
            int sphereCount = workload == SimBenchWorkload.SphereOnly ? count : count / 2;
            int sectorCount = workload == SimBenchWorkload.SphereOnly ? 0 : count - sphereCount;
            Queries = new BenchQuery[rayCount + sphereCount + sectorCount];
            Entities = new EntityId[count];
            Rays = new RayHit[count];
            Counts = new int[Queries.Length];
            Signatures = new ulong[Queries.Length];
            for (int i = 0; i < rayCount; i++)
            {
                SimVec3 target = bodies[i].Position;
                if (dense)
                {
                    int rows = (count + columns - 1) / columns;
                    int row = rows / 2;
                    int aimedRow = row + i % 5 - 2;
                    if (aimedRow < 0)
                        aimedRow = 0;
                    if (aimedRow >= rows)
                        aimedRow = rows - 1;
                    int aimIndex = aimedRow * columns + columns / 2;
                    if (aimIndex >= count)
                        aimIndex = count - 1;
                    // Small or partial rows must still aim at an actual member of the crowd.
                    SimVec3 aim = bodies[aimIndex].Position;
                    var origin = new SimVec3(SimFix.FromInt(-4), target.Y, SimFix.FromRaw((row + i % 7 - 3) * spacing));
                    Queries[i] = new BenchQuery(0, origin, aim - origin, SimFix.FromInt(48));
                }
                else if (i % 4 == 0)
                    Queries[i] = new BenchQuery(0, target, SimVec3.Metres(1, 0, 0), SimFix.FromInt(12));
                else if (i % 4 == 1)
                    Queries[i] = new BenchQuery(0, target + SimVec3.Metres(-4, 0, 0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(12));
                else if (i % 4 == 2)
                    Queries[i] = new BenchQuery(0, target + SimVec3.Metres(0, 8, 0), SimVec3.Metres(0, 1, 0), SimFix.FromInt(12));
                else
                    Queries[i] = new BenchQuery(0, target + SimVec3.Metres(-4, 0, -2), SimVec3.Metres(2, 0, 1), SimFix.FromInt(12));
            }

            for (int i = 0; i < sphereCount; i++)
                Queries[rayCount + i] = new BenchQuery(1, bodies[i % count].Position, default, SimFix.FromInt(dense ? 2 : 6));
            for (int i = 0; i < sectorCount; i++)
                Queries[rayCount + sphereCount + i] = new BenchQuery(2, bodies[(i + sphereCount) % count].Position,
                    SimVec3.Metres(1, 0, 1), SimFix.FromInt(dense ? 3 : 6));
            ValidateQueries(bodies, rayCount, sphereCount);
            if (dense)
                for (int i = 0; i < rayCount; i++)
                    if (Counts[i] == 0)
                        throw new InvalidOperationException("A crowd-aimed ray must hit its target.");
            Sim.Register("bench-queries", this, SimSystemPhase.Queries);
        }

        private void ValidateQueries(SpatialBody[] bodies, int rayCount, int sphereCount)
        {
            for (int i = 0; i < Queries.Length; i++)
            {
                Counts[i] = Queries[i].Run(Grid, Entities, Rays);
                Signatures[i] = Signature(Queries[i].Kind, Counts[i]);
            }

            var oracle = new NaiveSpatialQuery(bodies);
            // Representative calls in every group compare complete sorted ids and distances
            // with the frozen independent oracle before any timing is collected.
            VerifyGroup(oracle, 0, rayCount);
            VerifyGroup(oracle, rayCount, sphereCount);
            VerifyGroup(oracle, rayCount + sphereCount, Queries.Length - rayCount - sphereCount);
        }

        private void VerifyGroup(ISpatialQuery oracle, int start, int count)
        {
            for (int i = 0; i < 32 && i < count; i++)
            {
                int index = start + (i * 37) % count;
                int found = Queries[index].Run(oracle, Entities, Rays);
                if (found != Counts[index] || Signature(Queries[index].Kind, found) != Signatures[index])
                    throw new InvalidOperationException("Benchmark input differs from the independent spatial oracle.");
            }
        }

        internal ulong Signature(int kind, int count)
        {
            ulong value = (ulong)count;
            for (int i = 0; i < count; i++)
            {
                EntityId entity = kind == 0 ? Rays[i].Entity : Entities[i];
                value = unchecked((value ^ (ulong)entity.Id) * 1099511628211UL);
                value = unchecked((value ^ entity.Generation) * 1099511628211UL);
                if (kind == 0)
                    value = unchecked((value ^ (ulong)Rays[i].Distance.Raw) * 1099511628211UL);
            }

            return value;
        }

        public void Step(SimContext context)
        {
            for (int i = 0; i < Queries.Length; i++)
            {
                int found = Queries[i].Run(Grid, Entities, Rays);
                ulong signature = Signature(Queries[i].Kind, found);
                if (found != Counts[i] || signature != Signatures[i])
                    throw new InvalidOperationException("Benchmark query result changed or was not written.");
                Checksum = unchecked(Checksum * 1099511628211UL ^ signature);
                Calls++;
                TotalHits += found;
                if (Queries[i].Kind == 0)
                {
                    RayCalls++;
                    NarrowCandidates += Grid.LastNarrowPhaseCount;
                    BroadCandidates += Grid.LastCandidateCount;
                }
            }

            context.Emit(new SimEvent(new EventTypeId(901), tag: Queries.Length));
        }

        internal void ResetCounters()
        {
            Calls = 0;
            RayCalls = 0;
            NarrowCandidates = 0;
            BroadCandidates = 0;
            TotalHits = 0;
            Checksum = 0;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Calls);
            hash.Add(Checksum);
        }
    }
}
