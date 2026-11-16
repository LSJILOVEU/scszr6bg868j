using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt168ShotPlanTests
    {
        [Test]
        public void FirstFireAtTickZeroDoesNotRepeatOnLaterShots()
        {
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            var fires = new[] { new ViewFire(0f, 0f, 8f, 4f, 2, true) };

            plan.Observe(new FrameView(0, new ViewEntity[0], fires), anchors);
            Assert.That(anchors.Select(a => (a.Kind, a.Tick)),
                Is.EquivalentTo(new[] { (ShotKind.Tick, 0L), (ShotKind.FirstFire, 0L) }));

            plan.Observe(new FrameView(1, new ViewEntity[0], fires), anchors);
            Assert.That(anchors, Is.Empty);
        }
    }
}
