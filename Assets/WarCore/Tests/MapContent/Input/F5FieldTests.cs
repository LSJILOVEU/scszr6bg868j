using System;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // F5 (2) (4) (5): block, throw and skill slot travel from PlayerInput into the Sim frame.
    public sealed class F5FieldTests
    {
        [TestCase(true, false, 0)]
        [TestCase(false, true, 0)]
        [TestCase(false, false, 3)]
        public void EachNewFieldAlone_ReachesContextInput(bool block, bool @throw, int skillSlot)
        {
            var w = F5World.World();
            w.Session.SetPlayerInput(1, new PlayerInput(0, 0, block: block, @throw: @throw, skillSlot: skillSlot));
            w.Session.StepOnce();
            InputFrame frame = w.Sim.Context.Input;
            Assert.That(frame.Block, Is.EqualTo(block), "Block");
            Assert.That(frame.Throw, Is.EqualTo(@throw), "Throw");
            Assert.That(frame.SkillSlot, Is.EqualTo(skillSlot), "SkillSlot");
            Assert.That(frame.PrimaryAttack, Is.False);
            Assert.That(w.Session.PlayerLog.Count, Is.EqualTo(1));
            Assert.That(w.Session.PlayerLog[0].Input.Block, Is.EqualTo(block));
            Assert.That(w.Session.PlayerLog[0].Input.Throw, Is.EqualTo(@throw));
            Assert.That(w.Session.PlayerLog[0].Input.SkillSlot, Is.EqualTo(skillSlot));
        }

        [Test]
        public void NegativeSkillSlot_Throws()
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInput(0, 0, skillSlot: -1));
            Assert.That(e.ParamName, Is.EqualTo("skillSlot"));
        }

        [Test]
        public void EachNewFieldAlone_ChangesEquality()
        {
            var basis = new PlayerInput(10, -20, 300, 400, true);
            Assert.That(new PlayerInput(10, -20, 300, 400, true, block: true), Is.Not.EqualTo(basis));
            Assert.That(new PlayerInput(10, -20, 300, 400, true, @throw: true), Is.Not.EqualTo(basis));
            Assert.That(new PlayerInput(10, -20, 300, 400, true, skillSlot: 1), Is.Not.EqualTo(basis));
            Assert.That(new PlayerInput(10, -20, 300, 400, true, true, true, 4), Is.EqualTo(new PlayerInput(10, -20, 300, 400, true, true, true, 4)));
            Assert.That(new PlayerInput(10, -20, 300, 400, true, true, true, 4).GetHashCode(),
                Is.EqualTo(new PlayerInput(10, -20, 300, 400, true, true, true, 4).GetHashCode()));
        }

        [Test]
        public void SquadFollowAndBlock_OnTheSameTick_BothReachTheSim()
        {
            var w = F5World.World(standing: true);
            w.Session.Enqueue(BridgeCommand.Follow(2, 1, 4));
            w.Session.SetPlayerInput(2, new PlayerInput(0, 0, block: true));
            w.Session.StepOnce();
            Assert.That(w.Sim.Context.Input.Squad, Is.Null);
            Assert.That(w.Sim.Context.Input.Block, Is.False);
            w.Session.StepOnce();
            InputFrame frame = w.Sim.Context.Input;
            Assert.That(frame.Block, Is.True);
            Assert.That(frame.Squad.HasValue, Is.True);
            Assert.That(frame.Squad.Value.Kind, Is.EqualTo(SquadCommandKind.Follow));
            Assert.That(frame.Squad.Value.Squad, Is.EqualTo(1));
            Assert.That(frame.Squad.Value.TargetEntity, Is.EqualTo(w.Unit(4)));
            Assert.That(w.Sim.Entities.TryGet(w.Sim.Entities.Resolve<SquadStanding>(), w.Unit(2), out SquadStanding standing), Is.True);
            Assert.That((standing.Kind, standing.Target), Is.EqualTo((StandingKind.Follow, w.Sim.Entities.StableOf(w.Unit(4)).Value)));
            Assert.That(w.Session.CommandLog.Count, Is.EqualTo(1));
        }

        [Test]
        public void SyntheticClick_CopiesBlockThrowAndSkill_ThenReleasesOnlyPrimary()
        {
            var w = F5World.World();
            var held = new PlayerInput(0, 0, block: true, @throw: true, skillSlot: 2);
            Assert.That(held.Primary, Is.False, "must take the synthetic-click branch");
            new LivePlayerInput().Apply(w.Session, held, pressedThisFrame: true);
            w.Session.StepOnce();
            InputFrame click = w.Sim.Context.Input;
            Assert.That(click.PrimaryAttack, Is.True);
            Assert.That(click.Block, Is.True, "click tick Block");
            Assert.That(click.Throw, Is.True, "click tick Throw");
            Assert.That(click.SkillSlot, Is.EqualTo(2), "click tick SkillSlot");
            w.Session.StepOnce();
            InputFrame release = w.Sim.Context.Input;
            Assert.That(release.PrimaryAttack, Is.False);
            Assert.That(release.Block, Is.True, "release tick Block");
            Assert.That(release.Throw, Is.True, "release tick Throw");
            Assert.That(release.SkillSlot, Is.EqualTo(2), "release tick SkillSlot");
            Assert.That(w.Session.PlayerLog.Count, Is.EqualTo(2));
            Assert.That(w.Session.PlayerLog[1].Input, Is.EqualTo(held));
        }
    }
}
