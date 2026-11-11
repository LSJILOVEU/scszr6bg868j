using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class Mt083SpatialQueryViewTests
    {
        [Test]
        public void ConstructorRejectsNullIndex()
        {
            Assert.Throws<ArgumentNullException>(() => new SpatialQueryView(null, new GridQueryContext(1)));
        }

        [Test]
        public void BindRejectsNullContext()
        {
            var scene = new GridOwnershipTestFixture();
            var view = (SpatialQueryView)scene.Sim.SpatialQuery;
            Assert.Throws<ArgumentNullException>(() => view.Bind(null));
        }

        [Test]
        public void BindRejectsContextOneSlotBelowIndexCapacity()
        {
            var scene = new GridOwnershipTestFixture();
            var view = (SpatialQueryView)scene.Sim.SpatialQuery;
            Assert.Throws<ArgumentException>(() => view.Bind(new GridQueryContext(scene.Capacity - 1)));
        }

        [Test]
        public void QueryRejectsPositionChangedThroughRetainedReference()
        {
            var scene = new GridOwnershipTestFixture();
            EntityId entity = scene.CreateBody(default);
            scene.InitializeIndex();
            ref Position borrowed = ref scene.Store.Get(scene.Positions, entity);
            Position original = borrowed;
            Position[] escaped = scene.Positions.Storage.Values;
            var output = new EntityId[scene.Capacity];
            scene.Sim.Register("retained-position", new GridPhaseProbeSystem(40, context =>
            {
                escaped[entity.Id].Value = SimVec3.Metres(32, 0, 0);
                try
                {
                    Assert.Throws<SimBoundaryException>(() =>
                        context.SpatialQuery.Sphere(default, SimFix.FromInt(1), output));
                }
                finally
                {
                    escaped[entity.Id] = original;
                }
            }), SimSystemPhase.Queries);
            scene.Sim.Step(default);
        }
    }
}
