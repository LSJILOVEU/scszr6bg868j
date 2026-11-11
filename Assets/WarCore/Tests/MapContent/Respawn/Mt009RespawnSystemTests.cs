using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.RespawnSwitchTests;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Mt009RespawnSystemTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SkipsReserveKilledBeforeDeployment(bool reverse)
        {
            var w = World(Units(U(FirstReserve, 0, 0, reserve: true), U(SecondReserve, 0, 0, reserve: true)),
                Pack(Points(Spawn(1, 30, 20)), Bases(), 5), reverse);
            KillAt(w, 1, Player, FirstReserve);
            w.Step();
            Assert.That(w.Dead(FirstReserve), Is.True);
            Assert.That(w.Has<Position>(FirstReserve), Is.False);

            w.Step(5);
            Assert.That(Controlled(w), Is.EqualTo(SecondReserve));
            Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(w.Has<Position>(FirstReserve), Is.False);
            Assert.That(Point(w, SecondReserve), Is.EqualTo(P(29, 19)));
        }
    }
}
