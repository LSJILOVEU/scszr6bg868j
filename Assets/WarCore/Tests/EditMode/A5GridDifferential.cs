using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal sealed class A5GridDifferential
    {
        private const ulong Seed = 0xA5D1FF04;
        private readonly A5GridScenario scene = new A5GridScenario();
        private readonly EntityId[] latest = new EntityId[A5GridScenario.Capacity];
        private readonly SimVec3[] lastPosition = new SimVec3[A5GridScenario.Capacity];
        private readonly List<string> examples = new List<string>();
        private readonly int[] operations = new int[4];
        private readonly int[] queryMismatches = new int[4];
        private readonly int[] reuseComponents = new int[4];
        private A1MathInputs random = new A1MathInputs(Seed);
        private int comparisons;
        private int mismatches;
        private int membershipMismatches;
        private int rebuiltNaiveMismatches;

        private int Next(int exclusiveMax) => (int)(random.Next() % (ulong)exclusiveMax);
        private SimVec3 Position() => SimVec3.Metres(Next(49) - 24, Next(17) - 8, Next(49) - 24);

        internal void Run()
        {
            CheckSharedSphereBoundary();
            for (int i = 0; i < latest.Length; i++)
            {
                lastPosition[i] = Position();
                latest[i] = scene.CreateBody(lastPosition[i], 1 + Next(3), 10);
            }

            GridSpatialQuery rebuilt = scene.NewGrid();
            SpatialQueryKind[] kinds = A5GridScenario.QueryKinds();
            int completed = 0;
            // Shuffle balanced blocks so every mutation executes 50 times without exhausting live slots.
            for (int block = 0; block < 50; block++)
            {
                int[] order = {0, 1, 2, 3};
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = Next(i + 1);
                    int swap = order[i];
                    order[i] = order[j];
                    order[j] = swap;
                }

                foreach (int operation in order)
                {
                    SimVec3 focus = Mutate(operation);
                    operations[operation]++;
                    rebuilt.Rebuild();
                    NaiveSpatialQuery naive = scene.Naive();
                    for (int kind = 0; kind < kinds.Length; kind++)
                    {
                        A5GridScenario.QueryResult incrementalResult = A5GridScenario.Query(scene.Grid, kinds[kind], focus);
                        A5GridScenario.QueryResult rebuiltResult = A5GridScenario.Query(rebuilt, kinds[kind], focus);
                        A5GridScenario.QueryResult naiveResult = A5GridScenario.Query(naive, kinds[kind], focus);
                        string context = "step=" + (completed + 1) + " op=" + OperationName(operation) + " query=" + kinds[kind];
                        Compare(incrementalResult, rebuiltResult, context + " incremental/rebuilt", kind, false);
                        Compare(incrementalResult, naiveResult, context + " incremental/naive", kind, false);
                        Compare(rebuiltResult, naiveResult, context + " rebuilt/naive", kind, true);
                    }

                    scene.Grid.Synchronize();
                    if (scene.Grid.MembershipCount != rebuilt.MembershipCount)
                    {
                        membershipMismatches++;
                        Record("step=" + (completed + 1) + " op=" + OperationName(operation) + " memberships actual=" + scene.Grid.MembershipCount + " expected=" + rebuilt.MembershipCount);
                    }

                    completed++;
                }
            }

            TestContext.WriteLine("A5 differential: seed=0x{0:X8}; steps={1}; Upsert={2}, Remove={3}, Destroy={4}, Reuse={5}; query sets={6}; provider calls={7}; pair comparisons={8}; membership comparisons={1}; pair mismatches={9}; membership mismatches={10}; rebuilt/naive mismatches={11}.",
                Seed, completed, operations[0], operations[1], operations[2], operations[3], completed * 4, completed * 12, comparisons, mismatches, membershipMismatches, rebuiltNaiveMismatches);
            TestContext.WriteLine("A5 reuse components: neither={0}, position-only={1}, collider-only={2}, both={3}.", reuseComponents[0], reuseComponents[1], reuseComponents[2], reuseComponents[3]);
            for (int kind = 0; kind < kinds.Length; kind++)
                TestContext.WriteLine("A5 {0}: queries per provider={1}; pair comparisons={2}; pair mismatches={3}.", kinds[kind], completed, completed * 3, queryMismatches[kind]);
            Assert.That(completed, Is.EqualTo(200));
            Assert.That(operations, Is.EqualTo(new[]{50, 50, 50, 50}));
            Assert.That(comparisons, Is.EqualTo(2400));
            foreach (int count in reuseComponents)
                Assert.That(count, Is.GreaterThan(0), "A5 reuse must exercise every missing-component combination.");
            Assert.That(mismatches + membershipMismatches, Is.Zero,
                "A5 incremental grid diverged after Upsert/Remove/Destroy/slot reuse; compare ordered id:generation@distanceRaw. All 200 steps ran. First differences:\n" + string.Join("\n", examples));
        }

        private static void CheckSharedSphereBoundary()
        {
            var fixture = new A5GridScenario();
            EntityId id = fixture.Store.Create();
            var position = A2SpatialInputs.V(3, 0, 4);
            var collider = new Collider(SimFix.FromRaw(2), SimFix.FromRaw(4));
            fixture.Store.Set(fixture.Positions, id, new Position(position));
            fixture.Store.Set(fixture.Colliders, id, collider);
            fixture.Grid.Upsert(id);
            var body = new SpatialBody(id, position, collider);
            var output = new EntityId[1];
            foreach (ISpatialQuery provider in new ISpatialQuery[] { fixture.Grid, fixture.Naive() })
                for (int radius = 2; radius <= 4; radius++)
                {
                    // Feed the original raw request to the oracle, outside both public wrappers.
                    var request = new SpatialRequest(SpatialQueryKind.Sphere, default, SimFix.FromRaw(radius), default, default, default, 0);
                    bool expected = Oracle.OracleSpatialGeometry.Intersects(body, request, out _);
                    Assert.That(expected, Is.EqualTo(radius >= 3), "Hand-calculated 3-4-5 boundary.");
                    Assert.That(provider.Sphere(default, SimFix.FromRaw(radius), output), Is.EqualTo(expected ? 1 : 0), "Shared Sphere wrapper changed the request.");
                    if (expected)
                        Assert.That(output[0], Is.EqualTo(id));
                }
        }

        private EntityId PickAlive()
        {
            var alive = new List<EntityId>();
            foreach (EntityId entity in scene.Store)
                alive.Add(entity);
            Assert.That(alive.Count, Is.GreaterThan(0), "A5 mutation fixture must retain live entities.");
            return alive[Next(alive.Count)];
        }

        private SimVec3 Mutate(int operation)
        {
            if (operation == 3)
                return Reuse();
            EntityId entity = PickAlive();
            SimVec3 focus = lastPosition[entity.Id];
            if (operation == 0)
            {
                focus = Position();
                lastPosition[entity.Id] = focus;
                scene.SetBody(entity, focus, 1 + Next(4), 12);
            }
            else if (operation == 1)
            {
                // Removing the collider keeps a full rebuild semantically equal to explicit deindexing.
                scene.Store.Remove(scene.Colliders, entity);
                scene.Grid.Remove(entity);
            }
            else
                scene.Store.Destroy(entity);
            return focus;
        }

        private SimVec3 Reuse()
        {
            int freeSlot = -1;
            for (int i = 0; i < latest.Length; i++)
            {
                if (!scene.Store.IsAlive(latest[i]))
                {
                    freeSlot = i;
                    break;
                }
            }

            if (freeSlot == -1)
            {
                EntityId victim = PickAlive();
                freeSlot = victim.Id;
                scene.Store.Destroy(victim);
            }

            scene.Store.RecycleDestroyed();
            EntityId previous = latest[freeSlot];
            EntityId replacement = scene.Store.Create();
            Assert.That(replacement.Id, Is.EqualTo(freeSlot));
            Assert.That(replacement.Generation, Is.Not.EqualTo(previous.Generation));
            latest[freeSlot] = replacement;
            int components = Next(4);
            reuseComponents[components]++;
            SimVec3 focus = lastPosition[freeSlot];
            if ((components & 1) != 0)
                scene.Store.Set(scene.Positions, replacement, new Position(focus));
            if ((components & 2) != 0)
                scene.Store.Set(scene.Colliders, replacement, new Collider(SimFix.FromInt(2), SimFix.FromInt(6)));
            scene.Grid.Upsert(replacement);
            return focus;
        }

        private void Compare(A5GridScenario.QueryResult actual, A5GridScenario.QueryResult expected, string context, int kind, bool rebuiltNaive)
        {
            comparisons++;
            if (actual.SameAs(expected))
                return;
            mismatches++;
            queryMismatches[kind]++;
            if (rebuiltNaive)
                rebuiltNaiveMismatches++;
            Record(context + " actual=" + actual + " expected=" + expected);
        }

        private void Record(string message)
        {
            // Bound diagnostic text, never the number of executed comparisons or counted failures.
            if (examples.Count < 8)
                examples.Add(message);
        }

        private static string OperationName(int operation)
        {
            switch (operation)
            {
                case 0:
                    return "Upsert";
                case 1:
                    return "Remove";
                case 2:
                    return "Destroy";
                default:
                    return "Reuse";
            }
        }
    }
}
