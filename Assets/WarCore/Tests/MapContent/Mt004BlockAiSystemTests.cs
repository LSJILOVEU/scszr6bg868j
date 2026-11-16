using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt004BlockAiSystemTests
    {
        private static CombatPack Pack(int permille) =>
            new CombatPack(Array.Empty<WeaponDef>(), new ZeroResolver(), new NearestVisibleEnemy(), MeleeWorld.Full,
                false, false, 0, 30000, new[] { MeleeWorld.Spec() }, new[] { new BlockSpec(500, 1000, permille) });

        [TestCase(2, false)]
        [TestCase(5, true)]
        public void AMiddleChance_MatchesTheFixedSwingRoll(long startTick, bool expected)
        {
            Assert.That(BlockAiSystem.Raises(new StableId(1), new StableId(2), startTick, 500), Is.EqualTo(expected));
        }

        [Test]
        public void TheControlledUnit_KeepsItsHoldWithoutAThreat()
        {
            var w = new MeleeWorld(Pack(1000), new[] { new UnitSpawn(2, 0, 2, 21000, 20000, 3000, 2, -1, false, -1, 0) },
                player: 2, match: M6World.Pack(), blockAi: true);
            w.W.Combat.Hold(w.W.Unit(2), SimVec3.Metres(-1, 0, 0));

            // Inspect the AI result before player input can overwrite it.
            w.W.System<BlockAiSystem>().Step(default);

            BlockState held = w.W.Read<BlockState>(2);
            Assert.That((held.Holding, held.DirX, held.DirZ), Is.EqualTo((true, -SimFix.Scale, 0L)));
        }

        [TestCase(2UL, 3UL, true)]
        [TestCase(3UL, 2UL, false)]
        public void TheIncomingSwing_RollsAttackerBeforeDefender(ulong attacker, ulong defender, bool expected)
        {
            var w = new MeleeWorld(Pack(500), new[]
            {
                MeleeWorld.Unit(attacker, 1, 20000, 20000, melee: 0),
                new UnitSpawn(defender, 0, 2, 21000, 20000, 3000, 2, -1, false, -1, 0),
            }, blockAi: true);
            w.W.Set(defender, new UnitFacing(SimFix.FromInt(-1), default));

            w.Step(2);

            Assert.That(w.W.Combat.IsHolding(w.W.Unit(defender)), Is.EqualTo(expected));
        }
    }
}
