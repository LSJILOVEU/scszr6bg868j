using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;
using static WarCore.Tests.Down1.Down1World;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D9a: how the match layer counts a casualty today (DOWN-1 does not touch Battle). DOWN-1b flips these numbers as
    // decided for it; each constant has one test that asserts it.
    public sealed class Down1Kb23Tests
    {
        // K1: a casualty stands on a point (capture, respawn and reinforcement presence).
        internal const bool DownedStands = true;
        // K2: a casualty counts as alive (last team standing, the head count at the time limit).
        internal const bool DownedCountsAlive = true;
        // K3: a downed player may respawn (the respawn waits for death).
        internal const bool DownedPlayerRespawns = false;
        // K4: a casualty may be picked as a reserve (no position and not dead).
        internal const bool DownedIsReserve = false;

        // Unit 0 (team 1) downed on tick 1, unit 1 (team 1) stunned on tick 1, unit 2 (team 2) living.
        private static Down1World Casualties()
        {
            var w = new Down1World(Pack(Rules(), new ScriptedDamage()), new[] { U(0, 0, 1), U(2 * M, 0, 1), U(4 * M, 0, 2) });
            w.DownAt(1, 0);
            w.At(1, SimSystemPhase.Queries, 26, c => w.Combat.Stun(c, w.Entity(1), 600));
            w.Step();
            Assert.That(w.Downed(0), Is.True);
            Assert.That(w.Stunned(1), Is.True);
            return w;
        }

        [Test]
        public void K1_ACasualtyStandsOnAPoint()
        {
            Down1World w = Casualties();
            var presence = new PointPresence(w.Combat, 2, null);
            Assert.That(presence.Stands(w.Entity(0), out int team, out _), Is.EqualTo(DownedStands));
            Assert.That(team, Is.EqualTo(1));
            Assert.That(presence.Stands(w.Entity(1), out _, out _), Is.EqualTo(DownedStands));
            Assert.That(presence.Stands(w.Entity(2), out _, out _), Is.True);
            Assert.That(w.Combat.Allows(w.Entity(0), UnitAction.Capture), Is.False);
            Assert.That(w.Combat.Allows(w.Entity(1), UnitAction.Capture), Is.False);
            Assert.That(w.Combat.Allows(w.Entity(2), UnitAction.Capture), Is.True);
        }

        [Test]
        public void K2_ACasualtyCountsAsAlive()
        {
            Down1World w = Casualties();
            // CountAlive reads only the units (the pack, capture and mode are not used by it).
            new MatchSystem(w.Combat, null, null, null).CountAlive(out int one, out int two);
            Assert.That(one, Is.EqualTo(DownedCountsAlive ? 2 : 0));
            Assert.That(two, Is.EqualTo(1));
        }

        // The player respawn waits until the player's life state is Dead (RespawnSystem); a downed unit is not dead.
        [Test]
        public void K3_ADownedPlayerDoesNotRespawn()
        {
            Down1World w = Casualties();
            bool respawns = w.Sim.GameplayLife(w.Id(0)).State == GameplayLifeState.Dead;
            Assert.That(respawns, Is.EqualTo(DownedPlayerRespawns));
            Assert.That(w.Combat.IsDead(w.Entity(0)), Is.False);
        }

        // A reserve is a unit of the team without a position that is not dead (RespawnSystem, ReinforceSystem).
        [Test]
        public void K4_ACasualtyIsNoReserve()
        {
            Down1World w = Casualties();
            var positions = w.Sim.Entities.Resolve<Position>();
            for (int i = 0; i < 2; i++)
            {
                bool reserve = !w.Sim.Entities.TryGet(positions, w.Entity(i), out _) && !w.Combat.IsDead(w.Entity(i));
                Assert.That(reserve, Is.EqualTo(DownedIsReserve), "unit " + i);
            }
        }
    }
}
