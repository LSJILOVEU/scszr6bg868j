using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using static WarCore.Tests.Wp18TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp18LiveInputTests
    {
        [TestCase(0, 1)]
        [TestCase(0, 5)]
        [TestCase(4, 1)]
        [TestCase(4, 5)]
        public void ReleasedClick_SurvivesZeroTickFrames_ExactlyOnePrimaryTickAndOneShot(int zeroFrames, int ticks)
        {
            var w = new M6World(Recipe()); var live = new LivePlayerInput(); var s = w.Session;
            live.Apply(s, Aim(false), true);
            Assert.That(s.PlayerLog, Is.Empty);
            for (int frame = 0; frame < zeroFrames; frame++)
            {
                Assert.That(s.Advance(0), Is.Zero);
                live.Apply(s, Aim(false), false);
                Assert.That(s.PlayerLog, Is.Empty);
            }
            Assert.That(s.Advance(ticks == 1 ? 33334 : 166667), Is.EqualTo(ticks));
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(29));
            Assert.That(s.PlayerLog[0].AtTick, Is.EqualTo(1));
            Assert.That(s.PlayerLog[0].Input.Primary, Is.True);
            if (ticks == 1)
            {
                Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.True);
                live.Apply(s, Aim(false), false);
                Assert.That(s.Advance(33334), Is.EqualTo(1));
            }
            Assert.That(s.PlayerLog.Count, Is.EqualTo(2));
            Assert.That(s.PlayerLog[1].AtTick, Is.EqualTo(2));
            Assert.That(s.PlayerLog[1].Input.Primary, Is.False);
            Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.False);
            for (int frame = 0; frame < 5; frame++)
            {
                live.Apply(s, Aim(false), false); s.Advance(166667);
                Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(29), "a consumed click never repeats");
            }
        }

        [Test]
        public void PressThenReleaseBeforeFirstTick_PreservesOneTickPulse()
        {
            var w = new M6World(Recipe()); var live = new LivePlayerInput(); var s = w.Session;
            live.Apply(s, Aim(), true); Assert.That(s.Advance(10000), Is.Zero);
            live.Apply(s, Aim(false), false); Assert.That(s.Advance(10000), Is.Zero);
            live.Apply(s, Aim(false), false); Assert.That(s.Advance(13334), Is.EqualTo(1));
            Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.True); Assert.That(Shots(w), Is.EqualTo(1));
            live.Apply(s, Aim(false), false); s.Advance(33334);
            Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.False); Assert.That(Shots(w), Is.Zero);
            Assert.That(s.PlayerLog[0].AtTick, Is.EqualTo(1));
            Assert.That(s.PlayerLog[1].AtTick, Is.EqualTo(2));
        }

        [Test]
        public void PendingClickThenHold_OverwritesOldRelease_EvenAcrossMoreZeroTickFrames()
        {
            var w = new M6World(Recipe()); var live = new LivePlayerInput(); var s = w.Session;
            live.Apply(s, Aim(false), true); s.Advance(0); // queues tick 2 false
            live.Apply(s, Aim(), true); s.Advance(0); // tick 2 must now stay true
            live.Apply(s, Aim(), false); s.Advance(166667);
            Assert.That(s.Tick, Is.EqualTo(5));
            Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.True);
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(28), "shots at 1 and 5");
            Assert.That(s.PlayerLog.Count, Is.EqualTo(1));
        }

        [Test]
        public void HoldAcrossZeroOneFiveTickFrames_ThenRelease_DoesNotLeaveALatchedClick()
        {
            var w = new M6World(Recipe()); var live = new LivePlayerInput(); var s = w.Session;
            live.Apply(s, Aim(), true); Assert.That(s.Advance(0), Is.Zero);
            live.Apply(s, Aim(), false); Assert.That(s.Advance(33334), Is.EqualTo(1));
            live.Apply(s, Aim(), false); Assert.That(s.Advance(166667), Is.EqualTo(5));
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(28));
            Assert.That(s.PlayerLog.Count, Is.EqualTo(1));
            live.Apply(s, Aim(false), false); Assert.That(s.Advance(0), Is.Zero);
            live.Apply(s, Aim(false), false); Assert.That(s.Advance(166667), Is.EqualTo(5));
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(28));
            Assert.That(s.PlayerLog.Count, Is.EqualTo(2));
            Assert.That(s.PlayerLog[1].AtTick, Is.EqualTo(7));
            Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.False);
        }

        [Test]
        public void CooldownClick_IsConsumedWithoutDelayedShot_ThenNewReadyClickWorks()
        {
            var w = new M6World(Recipe()); var live = new LivePlayerInput(); var s = w.Session;
            live.Apply(s, Aim(false), true); s.Advance(33334); // first shot, ready again at 5
            live.Apply(s, Aim(false), true); s.Advance(166667); // trigger at 2, release at 3
            Assert.That(s.Tick, Is.EqualTo(6));
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(29));
            Assert.That(s.PlayerLog[1].AtTick, Is.EqualTo(3));
            Assert.That(s.PlayerLog[1].Input.Primary, Is.False);
            live.Apply(s, Aim(false), true); s.Advance(166667);
            Assert.That(w.Read<Combatant>(1).Ammo, Is.EqualTo(28), "new click fires only at tick 7");
            Assert.That(s.PlayerLog[2].AtTick, Is.EqualTo(7));
            Assert.That(s.PlayerLog[3].AtTick, Is.EqualTo(8));
        }

        [Test]
        public void LatestAimAndMovementSurviveSyntheticRelease()
        {
            var w = new M6World(Recipe()); var live = new LivePlayerInput(); var s = w.Session;
            live.Apply(s, Aim(false), true); s.Advance(0);
            var latest = new PlayerInput(-500, 250, 23000, 11000, false);
            live.Apply(s, latest, false); s.Advance(166667);
            Assert.That(s.PlayerLog.Count, Is.EqualTo(2));
            Assert.That(s.PlayerLog[0].Input, Is.EqualTo(new PlayerInput(-500, 250, 23000, 11000, true)));
            Assert.That(s.PlayerLog[1].Input, Is.EqualTo(latest));
            Assert.That(s.PlayerLog[1].AtTick, Is.EqualTo(2));
        }

        [Test]
        public void SetPlayerInput_RemainsLastWriteWinsWithoutClickLatching()
        {
            var w = new M6World(Recipe());
            w.Session.SetPlayerInput(1, Aim()); w.Session.SetPlayerInput(1, Aim(false));
            w.Session.StepOnce();
            Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.False); Assert.That(Shots(w), Is.Zero);
        }
    }
}
