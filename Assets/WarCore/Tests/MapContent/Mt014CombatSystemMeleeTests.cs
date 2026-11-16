using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt014CombatSystemMeleeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void EqualDistanceEnemiesLockHeadingToSmallerStableId(bool reverse)
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[]
            {
                MeleeWorld.Unit(1, 1, 20000, 20000, melee: 0),
                MeleeWorld.Unit(5, 2, 21000, 20000),
                MeleeWorld.Unit(3, 2, 19000, 20000),
            }, reverse: reverse);
            long expectedDirX = new StableId(w.Id(5)).CompareTo(new StableId(w.Id(3))) < 0 ? SimFix.Scale : -SimFix.Scale;

            w.Step();
            MeleeState state = w.W.Read<MeleeState>(1);
            Assert.That(state.StartTick, Is.EqualTo(1));
            Assert.That(state.DirX, Is.EqualTo(expectedDirX));
            Assert.That(state.DirZ, Is.Zero);
        }
    }
}
