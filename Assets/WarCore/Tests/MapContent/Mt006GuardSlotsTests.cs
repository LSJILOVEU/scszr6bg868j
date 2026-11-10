using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt006GuardSlotsTests
    {
        [Test]
        public void NearestIncludesTheSlotExactlyOneMetreAway()
        {
            var point = new ControlPointDef(1, M6World.P(30, 20), SimFix.Ratio(1, 2));
            var slots = new GuardSlots(new[] { point });
            Assert.That(slots.Total, Is.EqualTo(1));
            Assert.That(slots.Nearest(point.Center + SimVec3.Metres(1, 0, 0)), Is.EqualTo(0));
        }
    }
}
