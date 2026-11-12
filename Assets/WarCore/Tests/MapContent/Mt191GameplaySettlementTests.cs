using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt191GameplaySettlementTests
    {
        [Test]
        public void StaleBatchOwnerReturnsLifeMismatch()
        {
            var world = new Wp13TestWorld();
            GameplayFailure result = GameplayFailure.None;
            world.Produce = c =>
            {
                var owner = world.Sim.GameplayTarget(world.Id(0));
                var stale = new GameplayStamp(owner.Id, (ushort)(owner.Generation + 1));
                result = c.SubmitGameplay(stale, ReadOnlySpan<GameplayCost>.Empty, ReadOnlySpan<GameplayAction>.Empty);
            };
            world.Step();
            Assert.That(result, Is.EqualTo(GameplayFailure.TargetLifeMismatch));
        }

        [Test]
        public void StaleStatusTargetReturnsLifeMismatch()
        {
            var world = new Wp13TestWorld();
            GameplayFailure result = GameplayFailure.None;
            world.Produce = c =>
            {
                var owner = world.Sim.GameplayTarget(world.Id(0));
                var target = world.Sim.GameplayTarget(world.Id(1));
                var stale = new GameplayStamp(target.Id, (ushort)(target.Generation + 1));
                var key = new GameplayApplyKey(owner.Id, 1, 0, owner.Id, 1, 0);
                result = c.SubmitGameplay(owner, new[] { new GameplayCost(1, 1) },
                    new[] { GameplayAction.Dispel(stale, key, 1) });
            };
            world.Step();
            Assert.That(result, Is.EqualTo(GameplayFailure.TargetLifeMismatch));
            Assert.That(world.Sim.GameplayResource(world.Id(0), 1), Is.EqualTo(world.Pack.MaxHealthRaw));
        }

        [Test]
        public void UnaffordableBatchReturnsInsufficientResource()
        {
            var world = new Wp13TestWorld();
            GameplayFailure result = GameplayFailure.None;
            world.Produce = c =>
            {
                var owner = world.Sim.GameplayTarget(world.Id(0));
                var target = world.Sim.GameplayTarget(world.Id(1));
                var key = new GameplayApplyKey(owner.Id, 1, 0, owner.Id, 1, 0);
                result = c.SubmitGameplay(owner, new[] { new GameplayCost(1, world.Pack.MaxHealthRaw + 1) },
                    new[] { GameplayAction.ChangeResource(target, key, 1, -7) });
            };
            world.Step();
            Assert.That(result, Is.EqualTo(GameplayFailure.InsufficientResource));
            Assert.That(world.Sim.GameplayResource(world.Id(0), 1), Is.EqualTo(world.Pack.MaxHealthRaw));
            Assert.That(world.Sim.GameplayResource(world.Id(1), 1), Is.EqualTo(world.Pack.MaxHealthRaw));
        }
    }
}
