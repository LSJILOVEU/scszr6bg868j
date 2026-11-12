using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt172BlockAiSystemTests
    {
        [Test]
        public void ChangingOnlyTheAttackerChangesTheChanceRoll()
        {
            Assert.That(BlockAiSystem.Raises(new StableId(1), new StableId(2), 1, 500), Is.True);
            Assert.That(BlockAiSystem.Raises(new StableId(3), new StableId(2), 1, 500), Is.False);
        }

        [TestCase(163, false)]
        [TestCase(164, true)]
        public void TheDefenderUsesTheConfiguredChanceForTheIncomingAttacker(int permille, bool holding)
        {
            var pack = new CombatPack(Array.Empty<WeaponDef>(), new ZeroResolver(), new NearestVisibleEnemy(), MeleeWorld.Full,
                false, false, 0, 30000, new[] { MeleeWorld.Spec() }, new[] { new BlockSpec(500, 1000, permille) });
            var w = new MeleeWorld(pack, new[]
            {
                MeleeWorld.Unit(1, 1, 20000, 20000, melee: 0),
                new UnitSpawn(2, 0, 2, 21000, 20000, 3000, 2, -1, false, -1, 0)
            }, blockAi: true);
            w.W.Set(2, new UnitFacing(SimFix.FromInt(-1), default));
            w.Step(2);

            var defender = w.W.Unit(2);
            Assert.That(w.W.Combat.TryMeleeThreat(defender, out StableId attacker, out long startTick), Is.True);
            Assert.That(attacker, Is.EqualTo(new StableId(w.Id(1))));
            Assert.That(startTick, Is.EqualTo(1));
            // This swing rolls 163; reversing the attacker and defender rolls 230.
            Assert.That(w.W.Combat.IsHolding(defender), Is.EqualTo(holding));
        }
    }
}
