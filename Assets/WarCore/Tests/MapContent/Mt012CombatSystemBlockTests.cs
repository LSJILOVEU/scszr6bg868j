using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt012CombatSystemBlockTests
    {
        private const long Ox = 20000, Oz = 20000;

        [Test]
        public void ReleaseWithoutBlockDataLeavesTheUnitUnchanged()
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz) });
            ulong before = w.W.Hash;
            Assert.That(w.W.Combat.CanBlock(w.W.Unit(1)), Is.False);
            Assert.DoesNotThrow(() => w.W.Combat.Release(w.W.Unit(1)));
            Assert.That(w.W.Hash, Is.EqualTo(before));
        }

        [Test]
        public void AnAttackerBeyondTheSwingRadiusStillThreatensFromItsLockedOrigin()
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) });
            w.Step();
            SimVec3 at = w.W.Read<Position>(1).Value;
            w.W.Set(1, new Position(at - new SimVec3(SimFix.Ratio(3, 2), default, default)));
            w.Step();
            Assert.That(w.W.Combat.TryMeleeThreat(w.W.Unit(2), out StableId attacker, out long startTick), Is.True);
            Assert.That(attacker.Value, Is.EqualTo(w.Id(1)));
            Assert.That(startTick, Is.EqualTo(1));
        }

        [Test]
        public void ATargetExactlyOnTheSwingRadiusIsThreatened()
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec(engage: 2) }), new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 2000, Oz) });
            w.Step();
            Assert.That(w.W.Combat.TryMeleeThreat(w.W.Unit(2), out StableId attacker, out long startTick), Is.True);
            Assert.That(attacker.Value, Is.EqualTo(w.Id(1)));
            Assert.That(startTick, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SimultaneousThreatsChooseTheLowerStableIdentity(bool reverse)
        {
            var w = new MeleeWorld(MeleeWorld.Pack(new[] { MeleeWorld.Spec() }), new[]
            {
                MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0),
                MeleeWorld.Unit(2, 2, Ox + 500, Oz + 1000),
                MeleeWorld.Unit(3, 1, Ox + 1000, Oz, melee: 0)
            }, reverse: reverse);
            w.Step();
            Assert.That(w.Count(StrikeKind.Started), Is.EqualTo(2));
            Assert.That(w.W.Combat.TryMeleeThreat(w.W.Unit(2), out StableId attacker, out long startTick), Is.True);
            Assert.That(attacker.Value, Is.EqualTo(System.Math.Min(w.Id(1), w.Id(3))));
            Assert.That(startTick, Is.EqualTo(1));
        }

        [Test]
        public void ABlowExactlyOnTheBlockArcIsStopped()
        {
            var melee = MeleeWorld.Pack(new[] { MeleeWorld.Spec() });
            var pack = new CombatPack(melee.Weapons.ToArray(), melee.Resolver, melee.Selector, melee.MaxHealthRaw,
                melee: melee.Melee.ToArray(), block: new[] { new BlockSpec(500, 1000) });
            var w = new MeleeWorld(pack, new[]
            { MeleeWorld.Unit(1, 1, Ox, Oz, melee: 0), MeleeWorld.Unit(2, 2, Ox + 1000, Oz) });
            w.W.Combat.SetBlock(w.W.Unit(2), 0);
            // Normalization preserves an exact half-scale component toward the attacker on -X.
            w.W.Combat.Hold(w.W.Unit(2), new SimVec3(SimFix.FromRaw(-32768), default, SimFix.FromRaw(56755)));
            Assert.That(w.W.Read<BlockState>(2).DirX, Is.EqualTo(-SimFix.Scale / 2));
            w.Step(7);
            Assert.That(w.Health(2), Is.EqualTo(MeleeWorld.Full));
            Assert.That(w.Count(StrikeKind.Blocked), Is.EqualTo(1));
        }
    }
}
