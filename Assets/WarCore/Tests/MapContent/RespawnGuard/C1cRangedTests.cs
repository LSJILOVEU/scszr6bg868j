using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // C1c (2), (4), (5), (6): ranged hits on the unit switched to are zeroed on ticks [6, 6 + G - 1]. Within a tick, requests
    // (respawn 4, MoveAt 19) run before queries (the match judges first, damage settles after), so a unit killed on tick t
    // counts as dead in the judgement of tick t + 1.
    public sealed class C1cRangedTests
    {
        private static void Geometry()
        {
            Assert.That(CombatSystem.Horizontal(P(40, 20) - C1c.Spot).Length.Raw, Is.LessThan(SimFix.FromInt(20).Raw), "shooter reaches the spot");
            Assert.That(CombatSystem.Horizontal(P(40, 20) - P(20, 38)).Length.Raw, Is.GreaterThan(SimFix.FromInt(20).Raw), "shooter cannot reach the ally");
            Assert.That(CombatSystem.Horizontal(P(40, 20) - P(20, 20)).Length, Is.EqualTo(SimFix.FromInt(20)), "the player stands on the range edge");
        }

        [Test]
        public void C1cRanged_GuardZeroesHitsForTenTicks_ThenTheHitKills()
        {
            Geometry();
            var log = new LethalRollLog();
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(10), C1c.Lethal(log));
            w.Step();
            Assert.That(w.Dead(Player), Is.True, "tick 1: the kill table and the shooter both hit the player");
            Assert.That(C1c.Hits(w, C1c.Shooter, Player), Is.EqualTo(new[] { 100L * SimFix.Scale }));
            w.Step(C1c.Switch - 2);
            Assert.That(w.Has<Position>(C1c.Reserve), Is.False, "tick 5: not deployed yet");
            for (long tick = C1c.Switch; tick <= C1c.Switch + 9; tick++)
            {
                w.Step();
                Assert.That(Controlled(w), Is.EqualTo(C1c.Reserve), "tick " + tick);
                Assert.That(C1c.Holds(w, C1c.Reserve), Is.True, "alive after tick " + tick);
                Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve), Is.EqualTo(new[] { 0L }), "a zeroed hit on tick " + tick);
            }
            Assert.That(Wp16TestWorld.Point(w, C1c.Reserve), Is.EqualTo(C1c.Spot));
            w.Step();
            Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve), Is.EqualTo(new[] { 100L * SimFix.Scale }), "tick 16: the guard has ended");
            Assert.That(w.Dead(C1c.Reserve), Is.True);
            // (14b) the resolver was asked on every hit, shielded ones included: tick 1 and ticks 6..16.
            Assert.That(log.Rolls.Count, Is.EqualTo(12));
            // (14a) the rolls are the hit stream drawn in order; the next draw of the world's stream is the replay's 13th.
            Assert.That(w.Recipe.Seed, Is.EqualTo(41UL));
            var replay = new SimRandom(41);
            RandomStreamId stream = replay.Stream("combat.hit");
            var expected = new List<int>();
            for (int i = 0; i < 12; i++) expected.Add(replay.NextInt(stream, 65536));
            Assert.That(log.Rolls, Is.EqualTo(expected));
            Assert.That(w.Sim.Rng.NextInt(w.Sim.Rng.Stream("combat.hit"), 65536), Is.EqualTo(replay.NextInt(stream, 65536)));
        }

        [Test]
        public void C1cRanged_NoGuard_TheFirstHitKills()
        {
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(0));
            w.Step(C1c.Switch);
            Assert.That(Controlled(w), Is.EqualTo(C1c.Reserve));
            Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve), Is.EqualTo(new[] { 100L * SimFix.Scale }));
            Assert.That(w.Dead(C1c.Reserve), Is.True, "tick 6: shot on the switch tick");
        }

        // (5) G = 1: only the switch tick is shielded.
        [Test]
        public void C1cRanged_GuardOne_ShieldsOnlyTheSwitchTick()
        {
            var w = C1c.World(C1c.ShooterUnits(), C1c.Pack(1));
            w.Step(C1c.Switch);
            Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve), Is.EqualTo(new[] { 0L }));
            Assert.That(C1c.Holds(w, C1c.Reserve), Is.True, "tick 6");
            w.Step();
            Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve), Is.EqualTo(new[] { 100L * SimFix.Scale }));
            Assert.That(w.Dead(C1c.Reserve), Is.True, "tick 7");
        }

        // (4) Only the unit switched to is shielded: shooter 12 is moved next to the ally on tick 8 and kills it inside the window.
        [Test]
        public void C1cRanged_OnlyTheNewUnitIsShielded()
        {
            var units = C1c.ShooterUnits(U(C1c.Shooter2, 78, 4, team: 2, weapon: 0));
            Assert.That(CombatSystem.Horizontal(P(78, 4) - P(78, 38)).Length.Raw, Is.GreaterThan(SimFix.FromInt(20).Raw), "nobody in reach at the start");
            Assert.That(CombatSystem.Horizontal(P(20, 30) - P(20, 38)).Length, Is.EqualTo(SimFix.FromInt(8)));
            Assert.That(CombatSystem.Horizontal(P(20, 30) - C1c.Spot).Length.Raw, Is.GreaterThan(SimFix.FromInt(8).Raw), "the ally is shooter 12's nearest enemy");
            var w = C1c.World(units, C1c.Pack(10));
            w.Sim.RegisterGameplay("test.respawn-move", new MoveAt(w, 8, C1c.Shooter2, P(20, 30)), SimSystemPhase.Requests);
            w.Step(7);
            Assert.That(w.Dead(Ally), Is.False);
            w.Step();
            Assert.That(C1c.Hits(w, C1c.Shooter2, Ally), Is.EqualTo(new[] { 100L * SimFix.Scale }), "tick 8");
            Assert.That(w.Dead(Ally), Is.True, "tick 8: the ally is not shielded");
            Assert.That(C1c.Holds(w, C1c.Reserve), Is.True, "tick 8: the new unit is");
            for (long tick = 9; tick <= 15; tick++)
            {
                w.Step();
                Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve), Is.EqualTo(new[] { 0L }), "shooter 11, tick " + tick);
                Assert.That(C1c.Hits(w, C1c.Shooter2, C1c.Reserve), Is.EqualTo(new[] { 0L }), "shooter 12, tick " + tick);
            }
            w.Step();
            Assert.That(w.Dead(C1c.Reserve), Is.True, "tick 16");
            Assert.That(w.Match.Winner, Is.EqualTo(0), "team 1 is all dead after tick 16, but the judgement ran before the damage settled");
            w.Step();
            Assert.That(w.Match.Winner, Is.EqualTo(2));
            Assert.That(w.Match.EndReason, Is.EqualTo(MatchEndReason.Exhausted));
        }

        // (6) Each switch starts its own window.
        [Test]
        public void C1cRanged_EverySwitchHasItsOwnWindow()
        {
            var w = C1c.World(C1c.ShooterUnits(U(C1c.Reserve2, 0, 0, reserve: true)), C1c.Pack(10));
            w.Step(16);
            Assert.That(w.Dead(C1c.Reserve), Is.True, "tick 16");
            w.Step(4);
            Assert.That(Controlled(w), Is.EqualTo(C1c.Reserve), "tick 20: still waiting");
            for (long tick = 21; tick <= 30; tick++)
            {
                w.Step();
                Assert.That(Controlled(w), Is.EqualTo(C1c.Reserve2), "tick " + tick);
                Assert.That(C1c.Hits(w, C1c.Shooter, C1c.Reserve2), Is.EqualTo(new[] { 0L }), "tick " + tick);
                Assert.That(C1c.Holds(w, C1c.Reserve2), Is.True, "tick " + tick);
            }
            w.Step();
            Assert.That(w.Dead(C1c.Reserve2), Is.True, "tick 31");
            Assert.That(w.Sim.Entities.StableOf(w.Unit(C1c.Reserve2)), Is.Not.EqualTo(w.Sim.Entities.StableOf(w.Unit(C1c.Reserve))));
        }
    }
}
