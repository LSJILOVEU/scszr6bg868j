using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    public sealed class A2SpatialTests
    {
        [Test]
        public void EveryPermilleSineMatchesTheFrozenIntegerSquareRoot()
        {
            Type type = typeof(SimFix).Assembly.GetType("WarCore.Sim.GeometrySinTable", true);
            var sine = (Func<int, long>)type.GetMethod("Sin", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(Func<int, long>));
            var field = type.GetField("Values", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(((long[])field.GetValue(null)).Length, Is.EqualTo(2001));
            Assert.That(SourceRules.TableChecksumMatches(field), Is.True);
            WideInt scale = WideInt.From(1L << 32);
            for (int cosine = -1000; cosine <= 1000; cosine++)
            {
                long expected = (WideInt.From(1000000 - (long)cosine * cosine) * scale * scale).Sqrt().ToLong();
                Assert.That(sine(cosine), Is.EqualTo(expected), "cosine=" + cosine);
            }

            TestContext.WriteLine("A2 geometry sine: 2001/2001 frozen-oracle entries; checksum=0x9B598C62F03A0860; separate from SimMath tables.");
        }

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void ValidatedWorldAndTangentInputsMatchIndependentOracle(SpatialQueryKind kind)
        {
            ulong seed = 0xA2000000UL + (ulong)kind;
            var inputs = new A2SpatialInputs(seed);
            A2SpatialInputs.Predicate product = A2SpatialInputs.Product();
            A2SpatialInputs.BoundsPredicate bounds = A2SpatialInputs.Bounds();
            int hits = 0;
            int rejected = 0;
            int preciseMisses = 0;
            for (int i = 0; i < 4000; i++)
            {
                A2SpatialInputs.Sample sample = inputs.Random(kind, i);
                bool expected = OracleSpatialGeometry.Intersects(sample.Body, sample.Request, out SimFix expectedDistance);
                bool actual = product(in sample.Body, in sample.Request, out SimFix actualDistance);
                string context = kind + " seed=" + seed + " case=" + i + " group=" + sample.Source;
                Assert.That(actual, Is.EqualTo(expected), context);
                Assert.That(actualDistance, Is.EqualTo(expectedDistance), context + " distance");
                hits += actual ? 1 : 0;
                if (kind == SpatialQueryKind.Ray || kind == SpatialQueryKind.Capsule)
                {
                    bool possible = bounds(in sample.Body, in sample.Request);
                    Assert.That(!possible && expected, Is.False, context + " false rejection");
                    rejected += possible ? 0 : 1;
                    preciseMisses += possible && !actual ? 1 : 0;
                }
            }

            TestContext.WriteLine("A2 geometry {0}: seed=0x{1:X8}, executed=4000 (1000 local + 1000 world + 1000 world-edge + 1000 tangent+-1), hits={2}, misses={3}, bounds rejections={4}, precise misses={5}, mismatches=0.", kind, seed, hits, 4000 - hits, rejected, preciseMisses);
        }

        [Test]
        public void PublicLimitsAndTinyCellCapacityRemainEnforced()
        {
            Assert.That(typeof(SimFix).Assembly.GetType("WarCore.Sim.WideInt"), Is.Null);
            var inputs = new A2SpatialInputs(1);
            SimVec3 origin = default;
            Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Capsule(origin, A2SpatialInputs.V(A2SpatialInputs.World, 1, 0), default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Ray(origin, A2SpatialInputs.V(A2SpatialInputs.World + 1, 0, 0), default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Sphere(origin, SimFix.FromRaw(A2SpatialInputs.World + 1), default));
            var store = new EntityStore(1);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            EntityId entity = store.Create();
            store.Set(positions, entity, new Position(origin));
            store.Set(colliders, entity, new Collider(SimFix.FromRaw(A2SpatialInputs.World), SimFix.FromRaw(2 * A2SpatialInputs.World)));
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(1), 1, 8);
            Assert.DoesNotThrow(() => grid.Upsert(entity));
            Assert.That(grid.Index.LargeBodyCount, Is.EqualTo(1), "Huge volume must use the bounded bypass list.");
            Assert.That(grid.Sphere(origin, default, new EntityId[1]), Is.EqualTo(1));
            store.Set(colliders, entity, new Collider(default, default));
            Assert.DoesNotThrow(() => grid.Upsert(entity));
            store.Set(colliders, entity, new Collider(SimFix.FromRaw(1), SimFix.FromRaw(2)));
            var exact = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(1), 1, 27);
            var shortByOne = new GridSpatialQuery(store, positions, colliders, SimFix.FromRaw(1), 1, 26);
            Assert.DoesNotThrow(() => exact.Upsert(entity));
            Assert.That(exact.MembershipCount, Is.EqualTo(27));
            Assert.Throws<SimBoundaryException>(() => shortByOne.Upsert(entity));
        }

        [TestCase(SpatialQueryKind.Ray)]
        [TestCase(SpatialQueryKind.Capsule)]
        public void HorizontalCrossSectionsPreserveRoundedRoots(SpatialQueryKind kind)
        {
            ulong seed = 0xA2001000UL + (ulong)kind;
            var inputs = new A2SpatialInputs(seed);
            var product = A2SpatialInputs.Product();
            int hits = 0;
            for (int i = 0; i < 2000; i++)
            {
                A2SpatialInputs.Sample source = inputs.Random(kind, i);
                SpatialRequest q = source.Request;
                SimVec3 direction = A2SpatialInputs.V(q.Direction.X.Raw, 0, q.Direction.Z.Raw);
                if (kind == SpatialQueryKind.Ray && direction.Equals(default(SimVec3)))
                    direction = A2SpatialInputs.V(1, 0, 0);
                A2SpatialInputs.Sample sample = inputs.Make(kind, source.Body.Position, source.Body.Collider.Radius.Raw,
                    source.Body.Collider.Height.Raw, q.Origin, direction, q.Radius.Raw, q.Range.Raw, q.HalfHeight.Raw, q.Cosine, source.Source);
                bool expected = OracleSpatialGeometry.Intersects(sample.Body, sample.Request, out SimFix expectedDistance);
                bool actual = product(in sample.Body, in sample.Request, out SimFix distance);
                Assert.That(actual, Is.EqualTo(expected), "horizontal " + kind + " case=" + i);
                Assert.That(distance, Is.EqualTo(expectedDistance), "horizontal distance " + kind + " case=" + i);
                hits += actual ? 1 : 0;
            }

            TestContext.WriteLine("A2 horizontal {0}: seed=0x{1:X8}, executed=2000, hits={2}, misses={3}, exact hit/distance mismatches=0.", kind, seed, hits, 2000 - hits);
        }
    }
}
