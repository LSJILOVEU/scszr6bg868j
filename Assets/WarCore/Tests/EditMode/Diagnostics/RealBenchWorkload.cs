using System;
using System.Collections.Generic;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Diagnostics
{
    internal enum QueryRole
    {
        AiRay,
        Sphere,
        Sector,
        SingleShot,
        ShotgunPellet,
        Volley,
        Melee,
        Explosion,
        Cover
    }

    internal readonly struct RealQuery
    {
        internal readonly BenchQuery Query;
        internal readonly QueryRole Role;
        internal RealQuery(QueryRole role, BenchQuery query)
        {
            Role = role;
            Query = query;
        }
    }

    // The same living bodies persist for every peak tick: this measures query load, not damage gameplay.
    internal class RealBenchWorkload
    {
        internal const ulong Seed = 0xB30001UL;
        internal const int EntityCount = 2000;
        internal readonly Simulation Sim;
        internal readonly GridSpatialQuery Grid;
        internal readonly SpatialBody[] Bodies;
        internal readonly RealQuery[] Queries;
        internal readonly EntityId[] Entities = new EntityId[EntityCount];
        internal readonly RayHit[] Rays = new RayHit[EntityCount];
        internal readonly int[] ExpectedCounts;
        internal readonly ulong[] ExpectedSignatures;
        internal readonly bool Dense;
        internal readonly bool Peak;
        internal readonly SimVec3 ExplosionOrigin;
        internal readonly int Affected;
        internal readonly int RayCount;
        internal int LastAffected;
        internal int LastRays;
        internal long Calls;
        internal long TotalHits;
        internal ulong Checksum;

        internal RealBenchWorkload(bool dense, bool peak)
        {
            Dense = dense;
            Peak = peak;
            Sim = new Simulation(Seed, config: new SimConfig(4, 16, 16, EntityCount, 16));
            var positions = Sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = Sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            Bodies = new SpatialBody[EntityCount];
            long spacing = dense ? SimFix.Scale * 3 / 4 : SimFix.Scale * 3;
            for (int i = 0; i < EntityCount; i++)
            {
                EntityId entity = Sim.Entities.Create();
                var position = new SimVec3(SimFix.FromRaw(i % 50 * spacing), SimFix.Ratio(3, 2), SimFix.FromRaw(i / 50 * spacing));
                var collider = new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(3));
                Sim.Entities.Set(positions, entity, new Position(position));
                Sim.Entities.Set(colliders, entity, collider);
                Bodies[i] = new SpatialBody(entity, position, collider);
            }

            var view = (SpatialQueryView)Sim.ConfigureSpatial(positions, colliders, SimFix.FromInt(3), EntityCount * 16 + 128);
            Grid = new GridSpatialQuery(Sim.SpatialIndex, view.QueryContext);
            Sim.CompleteSpatialBoundary();
            ExplosionOrigin = new SimVec3(SimFix.FromRaw(49 * spacing / 2), SimFix.Ratio(3, 2), SimFix.FromRaw(39 * spacing / 2));
            var queries = new List<RealQuery>();
            var random = new A1MathInputs(Seed);
            AddRays(queries, QueryRole.AiRay, 660, ref random, spacing);
            AddAreas(queries, QueryRole.Sphere, 165, ref random);
            AddAreas(queries, QueryRole.Sector, 165, ref random);
            if (peak)
            {
                AddRays(queries, QueryRole.SingleShot, 40, ref random, spacing);
                AddShotgun(queries, ref random, spacing);
                AddRays(queries, QueryRole.Volley, 100, ref random, spacing);
                AddAreas(queries, QueryRole.Melee, 60, ref random);
                queries.Add(new RealQuery(QueryRole.Explosion, new BenchQuery(1, ExplosionOrigin, default, SimFix.FromInt(8))));
                Affected = Grid.Sphere(ExplosionOrigin, SimFix.FromInt(8), Entities);
                for (int i = 0; i < Affected; i++)
                    queries.Add(Cover(Entities[i]));
            }

            RayCount = 660 + (peak ? 172 + Affected : 0);
            Queries = queries.ToArray();
            ValidateComposition();
            ExpectedCounts = new int[Queries.Length];
            ExpectedSignatures = new ulong[Queries.Length];
            for (int i = 0; i < Queries.Length; i++)
            {
                ExpectedCounts[i] = Queries[i].Query.Run(Grid, Entities, Rays);
                ExpectedSignatures[i] = Signature(Queries[i].Query.Kind, ExpectedCounts[i]);
            }

            VerifyOracle();
        }

        private void AddRays(List<RealQuery> queries, QueryRole role, int count, ref A1MathInputs random, long spacing)
        {
            for (int i = 0; i < count; i++)
            {
                int index = (int)(random.Next() % EntityCount);
                SimVec3 target = Bodies[index].Position;
                BenchQuery query;
                if (Dense)
                {
                    var origin = new SimVec3(SimFix.FromInt(-4), target.Y, SimFix.FromRaw((20 + index % 7 - 3) * spacing));
                    SimVec3 aim = Bodies[(20 + index % 5 - 2) * 50 + 25].Position;
                    query = new BenchQuery(0, origin, aim - origin, SimFix.FromInt(48));
                }
                else if (role == QueryRole.AiRay && i % 4 == 0)
                    query = new BenchQuery(0, target, SimVec3.Metres(1, 0, 0), SimFix.FromInt(12));
                else if (role == QueryRole.AiRay && i % 4 == 2)
                    query = new BenchQuery(0, target + SimVec3.Metres(0, 8, 0), SimVec3.Metres(0, 1, 0), SimFix.FromInt(12));
                else if (i % 2 == 0)
                    query = new BenchQuery(0, target + SimVec3.Metres(-4, 0, -2), SimVec3.Metres(2, 0, 1), SimFix.FromInt(12));
                else
                    query = new BenchQuery(0, target + SimVec3.Metres(-4, 0, 0), SimVec3.Metres(1, 0, 0), SimFix.FromInt(12));
                queries.Add(new RealQuery(role, query));
            }
        }

        private void AddShotgun(List<RealQuery> queries, ref A1MathInputs random, long spacing)
        {
            for (int shot = 0; shot < 4; shot++)
            {
                int index = (int)(random.Next() % EntityCount);
                SimVec3 target = Bodies[index].Position;
                SimVec3 origin = Dense
                    ? new SimVec3(SimFix.FromInt(-4), target.Y, SimFix.FromRaw((20 + index % 7 - 3) * spacing))
                    : target + SimVec3.Metres(-4, 0, 0);
                SimVec3 aim = Dense ? Bodies[(20 + index % 5 - 2) * 50 + 25].Position : target;
                for (int pellet = 0; pellet < 8; pellet++)
                {
                    var spread = new SimVec3(default, SimFix.Ratio((pellet & 1) == 0 ? -1 : 1, 64), SimFix.Ratio(pellet * 2 - 7, 64));
                    queries.Add(new RealQuery(QueryRole.ShotgunPellet,
                        new BenchQuery(0, origin, aim - origin + spread, SimFix.FromInt(Dense ? 48 : 12))));
                }
            }
        }

        private void ValidateComposition()
        {
            var counts = new int[9];
            SimVec3 shotOrigin = default;
            for (int i = 0; i < Queries.Length; i++)
            {
                RealQuery input = Queries[i];
                int role = (int)input.Role;
                if (input.Role == QueryRole.ShotgunPellet)
                {
                    if (counts[role] % 8 == 0)
                        shotOrigin = input.Query.Origin;
                    else if (!input.Query.Origin.Equals(shotOrigin))
                        throw new InvalidOperationException("Each shotgun burst must share one origin across eight pellets.");
                }
                counts[role]++;
            }

            int[] expected = { 660, 165, 165, Peak ? 40 : 0, Peak ? 32 : 0, Peak ? 100 : 0, Peak ? 60 : 0, Peak ? 1 : 0, Affected };
            for (int role = 0; role < counts.Length; role++)
                if (counts[role] != expected[role])
                    throw new InvalidOperationException("Real workload composition does not match the fixed contract.");
        }

        private void AddAreas(List<RealQuery> queries, QueryRole role, int count, ref A1MathInputs random)
        {
            for (int i = 0; i < count; i++)
            {
                int index = (int)(random.Next() % EntityCount);
                int kind = role == QueryRole.Sphere ? 1 : 2;
                int radius = role == QueryRole.Melee ? 3 : Dense ? kind == 1 ? 2 : 3 : 6;
                queries.Add(new RealQuery(role, new BenchQuery(kind, Bodies[index].Position, SimVec3.Metres(1, 0, 1), SimFix.FromInt(radius))));
            }
        }

        internal RealQuery Cover(EntityId target)
        {
            SimVec3 delta = Bodies[target.Id].Position - ExplosionOrigin;
            SimFix distance = SimMath.Sqrt(delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
            return new RealQuery(QueryRole.Cover, new BenchQuery(0, ExplosionOrigin, delta, distance));
        }

        private void VerifyOracle()
        {
            var oracle = new NaiveSpatialQuery(Bodies);
            var samples = new int[9];
            for (int i = 0; i < Queries.Length; i++)
            {
                int role = (int)Queries[i].Role;
                if (samples[role] >= 8)
                    continue;
                samples[role]++;
                int found = Queries[i].Query.Run(oracle, Entities, Rays);
                if (found != ExpectedCounts[i] || Signature(Queries[i].Query.Kind, found) != ExpectedSignatures[i])
                    throw new InvalidOperationException("Real workload differs from the independent oracle.");
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

    }
}
