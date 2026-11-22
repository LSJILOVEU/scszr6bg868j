using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnAi2;
using static WarCore.Tests.RespawnAi2World;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 acceptance A1-A6 and A8-A10 on the fixture of RespawnAi2World (team 1's list is [B1, P], reentryTicks 60).
    // A unit killed "at D" has its death entered at tick D; "after step T" is after the step whose clock tick is T.
    public sealed class RespawnAi2Tests
    {
        private const long D = 10;
        // The fixture's pack digest with reentryTicks 60 (tag battle.pack.reentry.v1 and the number after the old fields).
        private const ulong ReentryDigest = 14904222673286456709UL;

        // A1: reentryTicks 0 is the field left out: the digest, the run and the reinforcement state recorded on the old code.
        [Test]
        public void A1_ZeroIsLeftOut_AndKeepsTheLockedRun()
        {
            MatchPack zero = Pack(reentry: 0);
            Assert.That(zero.PolicyDigest, Is.EqualTo(RespawnAi2LockTests.PackDigest));
            Assert.That((zero.ReentryTicks, zero.ReserveCount, zero.ReserveType, OldPack().ReentryTicks), Is.EqualTo((0, 0, (string)null, 0)));
            Assert.That(Pack().PolicyDigest, Is.Not.EqualTo(RespawnAi2LockTests.PackDigest));
            Assert.That(Pack(reentry: 61).PolicyDigest, Is.Not.EqualTo(Pack().PolicyDigest), "the number is in the digest");
            Assert.That(Pack().PolicyDigest, Is.EqualTo(ReentryDigest), "the digest with the switch, first recorded by this card");
            M6World w = World(Units(), zero);
            Kill(w, D, 2);
            Assert.That(Run(w, 300), Is.EqualTo(RespawnAi2LockTests.Run300));
            Assert.That((w.Reinforce.ByDebt, w.Reinforce.StateVersion, Saved(w.Reinforce).Length, HashOf(w.Reinforce)),
                Is.EqualTo((false, 1, 64, RespawnAi2LockTests.ReinforceHash300)));
            Assert.That(Counts(w, 1), Is.EqualTo((0L, 0L, 0L, 4L)), "by the clock nothing is counted as due or entered");
            Assert.Throws<InvalidOperationException>(() => w.Reinforce.NoteTakeover(1), "a takeover is counted only with the switch");
        }

        // A1: the player's respawn without the switch is the locked run of the old code.
        [Test]
        public void A1_ZeroKeepsTheLockedPlayerRun()
        {
            MatchPack zero = Pack(reentry: 0, respawn: 90);
            Assert.That(zero.PolicyDigest, Is.EqualTo(RespawnAi2LockTests.PlayerPackDigest));
            M6World w = World(Units(), zero, player: true);
            KillPlayer(w, D);
            Assert.That(Run(w, 300), Is.EqualTo(RespawnAi2LockTests.PlayerRun300));
        }

        // The constructor's bounds, and what a world with the switch needs.
        [TestCase(-1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(MatchPack.MaxReentryTicks, true)]
        [TestCase(MatchPack.MaxReentryTicks + 1, false)]
        public void A1_ThePackBoundsReentryTicks(int ticks, bool accepted)
        {
            if (accepted) Assert.That(Pack(reentry: ticks).ReentryTicks, Is.EqualTo(ticks));
            else Assert.Throws<ArgumentOutOfRangeException>(() => Pack(reentry: ticks));
        }

        [Test]
        public void A1_MaxReentryTicksIs36000() => Assert.That(MatchPack.MaxReentryTicks, Is.EqualTo(36000));

        // A2: one of team 1 dies at D. Nobody enters before the step of D + 60; that step one reserve enters.
        [Test]
        public void A2_TheReserveEntersOnTheTick()
        {
            M6World w = World(Units(), Pack());
            int[] start = AtStart(w);
            Kill(w, D, 2);
            GoTo(w, D, start);
            Assert.That((w.Dead(2), RespawnWorld.Entered(w, 2)), Is.EqualTo((true, D)), "dead at D");
            while (w.Sim.Clock.Tick < D + Reentry - 1)
            {
                Go(w, start);
                Assert.That((Counts(w, 1), Fielded(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((0L, 0L, 0L, 0L), 2, 0)), "tick " + w.Sim.Clock.Tick);
            }
            Go(w, start);
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(D + Reentry));
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((1L, 1L, 0L, 1L), 3, 3, 1)));
            DeployRecord record = w.Reinforce.Deployed[0];
            Assert.That((record.Tick, record.Team, record.Layer, record.PointIndex, Near(record.Point, B1)), Is.EqualTo((D + Reentry, 1, 0, -1, true)));
            Assert.That(w.Has<Position>(KeyOf(w, record.Unit)), Is.True, "the recorded unit stands on the field");
            Assert.That(Counts(w, 2), Is.EqualTo((0L, 0L, 0L, 0L)), "team 2 lost nobody");
            GoTo(w, 300, start);
            Assert.That((Counts(w, 1), Counts(w, 2), Fielded(w, 1), Fielded(w, 2)), Is.EqualTo(((1L, 1L, 0L, 1L), (0L, 0L, 0L, 0L), 3, 3)), "one death, one reserve, never by the clock");
            Assert.That((w.Reinforce.Attempts(1), w.Reinforce.NextTick(1)), Is.EqualTo((0L, (long)Interval)), "the clock's counters are not used");
        }

        // A3: two die on the same tick: one enters at D + 60, the other at D + 61.
        [Test]
        public void A3_OneAStep()
        {
            M6World w = World(Units(), Pack());
            int[] start = AtStart(w);
            Kill(w, D, 2, 3);
            GoTo(w, D + Reentry - 1, start);
            Assert.That(Counts(w, 1), Is.EqualTo((0L, 0L, 0L, 0L)));
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((2L, 1L, 1L, 1L), 2, 1)), "D + 60: one of the two");
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((2L, 2L, 0L, 2L), 3, 1)), "D + 61: the other");
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((2L, 2L, 0L, 2L), 3, 0)), "D + 62: nobody");
        }

        // A3: both teams lose a man on the same tick: each gets its one in the same step (the limit is per team).
        [Test]
        public void A3_EachTeamGetsItsOwn()
        {
            M6World w = World(Units(), Pack());
            Kill(w, D, 2, 5);
            GoTo(w, D + Reentry, AtStart(w));
            Assert.That((Counts(w, 1), Counts(w, 2), w.Reinforce.Deployed.Length), Is.EqualTo(((1L, 1L, 0L, 1L), (1L, 1L, 0L, 1L), 2)));
            Assert.That((w.Reinforce.Deployed[0].Team, w.Reinforce.Deployed[1].Team, Near(w.Reinforce.Deployed[1].Point, B2)), Is.EqualTo((1, 2, true)), "team 1 first, team 2 at its base");
        }

        // A4: nobody dies for 600 ticks: nobody enters. The same world by the clock (the control) deploys every reserve.
        [Test]
        public void A4_NoDeathNoEntry()
        {
            M6World w = World(Units(), Pack());
            int[] start = AtStart(w);
            int deploys = 0;
            while (w.Sim.Clock.Tick < 600) { Go(w, start); deploys += w.Reinforce.Deployed.Length; }
            Assert.That((deploys, Counts(w, 1), Counts(w, 2), Fielded(w, 1), Fielded(w, 2), Reserves(w, 1), Reserves(w, 2)),
                Is.EqualTo((0, (0L, 0L, 0L, 0L), (0L, 0L, 0L, 0L), 3, 3, 4, 4)));
            M6World clock = World(Units(), Pack(reentry: 0));
            Assert.DoesNotThrow(() => clock.Step(600));
            Assert.That((clock.Reinforce.Successes(1), Fielded(clock, 1), Reserves(clock, 1)), Is.EqualTo((4L, 7, 0)), "control: by the clock all four come out");
        }

        // A5: the list is [B1, P]; three die one after another: the entries are at B1, P, B1 (the rotation by successes).
        [Test]
        public void A5_TheDefaultIsTheRotation()
        {
            M6World w = World(Units(), Pack());
            Kills(w, (10, 1), (20, 2), (30, 3));
            var entries = Entries(w, 1, 200, AtStart(w));
            Assert.That(entries, Is.EqualTo(new List<(long, int)> { (70, -1), (80, 0), (90, -1) }));
        }

        // A5: a bound chooser is asked once per entry with the team's successes as the attempt and decides the first entry tried.
        [Test]
        public void A5_TheChooserIsAskedWithTheSuccesses()
        {
            M6World w = World(Units(), Pack());
            RespawnAi1.Recorder recorder = RespawnAi1.Record(w, RespawnAi1.TeamOne(1));
            Kills(w, (10, 1), (20, 2), (20, 5), (30, 3));
            var entries = Entries(w, 1, 200, AtStart(w));
            Assert.That(entries, Is.EqualTo(new List<(long, int)> { (70, 0), (80, 0), (90, 0) }), "the chooser sends team 1 to P");
            Assert.That(recorder.Calls.ConvertAll(c => (c.Tick, c.Team, c.Attempt, c.Options.Length)),
                Is.EqualTo(new List<(long, int, long, int)> { (70, 1, 0, 2), (80, 1, 1, 2), (80, 2, 0, 1), (90, 1, 2, 2) }));
            Assert.That((w.Reinforce.StateVersion, Saved(w.Reinforce).Length), Is.EqualTo((7, 56)), "the chooser's key is in the bytes");
        }

        // A5: the attempt is the team's successes, not its entered count: after a takeover by the player (entered 1, successes
        // 0) the first entry here still starts the rotation at B1.
        [Test]
        public void A5_TheAttemptIsNotTheEnteredCount()
        {
            M6World w = World(Units(), Pack(respawn: 30), player: true);
            KillPlayer(w, 10);
            Kill(w, 100, 2);
            var entries = Entries(w, 1, 200, AtStart(w));
            Assert.That(entries, Is.EqualTo(new List<(long, int)> { (160, -1) }), "attempt 0: B1");
            Assert.That(Counts(w, 1), Is.EqualTo((2L, 2L, 0L, 1L)));
        }

        // A6: no reserve: nothing is counted and nothing throws.
        [Test]
        public void A6_WithoutAReserveNothingMoves()
        {
            M6World w = World(Units(reserves: 0), Pack());
            Kill(w, D, 2);
            GoTo(w, 300, AtStart(w));
            Assert.That((Counts(w, 1), Fielded(w, 1)), Is.EqualTo(((1L, 0L, 1L, 0L), 2)), "owed one, for ever");
        }

        // A6: an empty list waits. Team 1 has no base and an enemy stands in P's circle (the first card's switch is on) until
        // it is moved away at tick 100: the list is empty up to the step of 100 and the reserve enters at 101.
        [Test]
        public void A6_AnEmptyListWaits()
        {
            M6World w = World(Units(4, U(30, 41000, 21000, 2)), Pack(teamOneBase: false, skip: true));
            int[] start = AtStart(w);
            Kill(w, D, 2);
            Move(w, 100, 30, SimVec3.Metres(70, 0, 10));
            GoTo(w, 100, start);
            Assert.That((Counts(w, 1), Fielded(w, 1)), Is.EqualTo(((1L, 0L, 1L, 0L), 2)), "due since 70, nowhere to enter");
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((1L, 1L, 0L, 1L), 3, 1)), "tick 101");
            Assert.That((w.Reinforce.Deployed[0].PointIndex, Near(w.Reinforce.Deployed[0].Point, PCentre)), Is.EqualTo((0, true)));
        }

        // A6: every cell of the first entry is taken: the reserve comes out at the next entry.
        [Test]
        public void A6_AFullEntryFallsToTheNext()
        {
            M6World w = World(Units(4, Blockers(40, B1)), Pack());
            Kill(w, D, 2);
            var entries = Entries(w, 1, D + Reentry, AtStart(w));
            Assert.That(entries, Is.EqualTo(new List<(long, int)> { (D + Reentry, 0) }), "B1 is full: P");
            Assert.That(Counts(w, 1), Is.EqualTo((1L, 1L, 0L, 1L)));
        }

        // A6: every cell of every entry is taken: nothing is counted and the next step tries again. One blocker of P is moved
        // away at tick 100: the reserve enters at 101 on that cell.
        [Test]
        public void A6_NoFreeCellRetries()
        {
            var blockers = new List<UnitSpawn>(Blockers(40, B1));
            blockers.AddRange(Blockers(50, PCentre));
            M6World w = World(Units(4, blockers.ToArray()), Pack());
            int[] start = AtStart(w);
            Kill(w, D, 2);
            Move(w, 100, 52, SimVec3.Metres(20, 0, 5));
            GoTo(w, 100, start);
            Assert.That((Counts(w, 1), Reserves(w, 1)), Is.EqualTo(((1L, 0L, 1L, 0L), 4)), "due since 70, every cell taken, nothing counted");
            Go(w, start);
            Assert.That((Counts(w, 1), Reserves(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((1L, 1L, 0L, 1L), 3, 1)), "tick 101");
            Assert.That(w.Reinforce.Deployed[0].Point, Is.EqualTo(SimVec3.Metres(39, 0, 21)), "the cell the blocker left");
        }

        // The dead of a team outside the match (0, or above TeamCount) are not counted and nothing throws.
        [TestCase(0)]
        [TestCase(3)]
        public void A6_TheDeadOfNoTeamAreNotCounted(int team)
        {
            M6World w = World(Units(4, U(30, 30000, 30000, team)), Pack());
            Kill(w, D, 30);
            GoTo(w, 200);
            Assert.That((w.Dead(30), Counts(w, 1), Counts(w, 2)), Is.EqualTo((true, (0L, 0L, 0L, 0L), (0L, 0L, 0L, 0L))));
        }

        // A8: with the player's respawn (90 ticks). The player dies at D: the body is not counted while it is controlled, so
        // nobody enters at D + 60. At D + 90 the player takes a reserve over: the body is due and one has entered. Then one
        // more of team 1 dies at 150 and is replaced at 210. Team 1 never has more than its three on the field, and each
        // death uses one reserve.
        [Test]
        public void A8_ThePlayersBodyIsReplacedByThePlayer()
        {
            M6World w = World(Units(), Pack(respawn: 90), player: true);
            int[] start = AtStart(w);
            KillPlayer(w, D);
            Kill(w, 150, 2);
            GoTo(w, D + Reentry - 1, start);
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((0L, 0L, 0L, 0L), 2, 4)), "D + 60: the player's body is not due");
            GoTo(w, D + 89, start);
            Assert.That((Counts(w, 1), RespawnWorld.Controlled(w)), Is.EqualTo(((0L, 0L, 0L, 0L), Player)), "D + 89");
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1), w.Reinforce.Deployed.Length), Is.EqualTo(((1L, 1L, 0L, 0L), 3, 3, 0)), "D + 90: the takeover entered");
            Assert.That(RespawnWorld.Controlled(w), Is.Not.EqualTo(Player));
            GoTo(w, 209, start);
            Assert.That(Counts(w, 1), Is.EqualTo((1L, 1L, 0L, 0L)), "tick 209");
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((2L, 2L, 0L, 1L), 3, 2)), "tick 210: the second death is replaced here");
            GoTo(w, 400, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1), DeadOf(w, 1)), Is.EqualTo(((2L, 2L, 0L, 1L), 3, 2, 2)), "two deaths, two reserves");
        }

        // A8: with a respawn of 30 ticks the takeover comes before the body is due: owed is -1 from D + 30 and 0 from D + 60,
        // and nobody else enters.
        [Test]
        public void A8_ATakeoverBeforeTheBodyIsDue()
        {
            M6World w = World(Units(), Pack(respawn: 30), player: true);
            int[] start = AtStart(w);
            KillPlayer(w, D);
            GoTo(w, D + 29, start);
            Assert.That(Counts(w, 1), Is.EqualTo((0L, 0L, 0L, 0L)));
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((0L, 1L, -1L, 0L), 3, 3)), "D + 30");
            GoTo(w, D + Reentry - 1, start);
            Assert.That(Counts(w, 1), Is.EqualTo((0L, 1L, -1L, 0L)), "D + 59");
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((1L, 1L, 0L, 0L), 3, 3)), "D + 60");
            GoTo(w, 300, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((1L, 1L, 0L, 0L), 3, 3)), "one death, one reserve");
        }

        // A9: a player and no respawn: the fallback moves control to a living teammate at once, the body is counted like any
        // other and the reinforcement replaces it at D + 60. The fallback itself enters nobody.
        [Test]
        public void A9_WithoutRespawnTheBodyIsReplacedHere()
        {
            M6World w = World(Units(), Pack(respawn: 0), player: true);
            int[] start = AtStart(w);
            KillPlayer(w, D);
            GoTo(w, D + 1, start);
            Assert.That((RespawnWorld.Controlled(w) != Player, w.Dead(RespawnWorld.Controlled(w)), Counts(w, 1)), Is.EqualTo((true, false, (0L, 0L, 0L, 0L))), "the fallback switched, nobody entered");
            GoTo(w, D + Reentry - 1, start);
            Assert.That((Counts(w, 1), Fielded(w, 1)), Is.EqualTo(((0L, 0L, 0L, 0L), 2)));
            Go(w, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((1L, 1L, 0L, 1L), 3, 3)), "D + 60");
            GoTo(w, 300, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((1L, 1L, 0L, 1L), 3, 3)));
        }

        // A10: a world saved while one death is waiting (tick 100: the first entered at 70, the second died at 80) and restored
        // into a fresh world runs on with the same hashes; the second entry then comes out at P, which needs the restored
        // successes. Two runs of one seed are equal.
        private static M6World Saving()
        {
            M6World w = World(Units(), Pack());
            Kills(w, (10, 2), (80, 3));
            return w;
        }

        [Test]
        public void A10_SaveAndRestoreMidWait()
        {
            M6World straight = Saving(), again = Saving();
            var hashes = new ulong[301];
            byte[] snapshot = null;
            var entries = new List<(long, int)>();
            for (int t = 1; t <= 300; t++)
            {
                Go(straight); Go(again);
                hashes[t] = straight.Hash;
                Assert.That(again.Hash, Is.EqualTo(hashes[t]), "the same seed twice, tick " + t);
                foreach (DeployRecord r in straight.Reinforce.Deployed) entries.Add((r.Tick, r.PointIndex));
                if (t == 100) { snapshot = straight.Sim.SaveRetainedSnapshot(); Assert.That(Counts(straight, 1), Is.EqualTo((1L, 1L, 0L, 1L)), "tick 100"); }
            }
            Assert.That(entries, Is.EqualTo(new List<(long, int)> { (70, -1), (140, 0) }));
            Assert.That((straight.Reinforce.ByDebt, straight.Reinforce.StateVersion, Saved(straight.Reinforce).Length), Is.EqualTo((true, 5, 48)));

            M6World restored = Saving();
            Assert.DoesNotThrow(() => restored.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(restored.Hash, Is.EqualTo(hashes[100]));
            Assert.That(Counts(restored, 1), Is.EqualTo((0L, 1L, -1L, 1L)), "entered and successes are restored; due is counted again by the next step");
            var after = new List<(long, int)>();
            for (int t = 101; t <= 300; t++)
            {
                Go(restored);
                Assert.That(restored.Hash, Is.EqualTo(hashes[t]), "tick " + t);
                foreach (DeployRecord r in restored.Reinforce.Deployed) after.Add((r.Tick, r.PointIndex));
            }
            Assert.That(after, Is.EqualTo(new List<(long, int)> { (140, 0) }));

            // Loaded into a world that has run on (to tick 150: two due, two entered), the counts are the saved ones and the
            // due count starts again.
            Assert.That(Counts(restored, 1), Is.EqualTo((2L, 2L, 0L, 2L)));
            Assert.DoesNotThrow(() => restored.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That((Counts(restored, 1), restored.Hash), Is.EqualTo(((0L, 1L, -1L, 1L), hashes[100])));
        }

        // A10: a world without the switch refuses a save made with it, and the other way round.
        [Test]
        public void A10_TheOtherWorldRefusesTheSave()
        {
            M6World on = Saving();
            Go(on); GoTo(on, 100);
            byte[] snapshot = on.Sim.SaveRetainedSnapshot();
            M6World off = World(Units(), Pack(reentry: 0));
            Kills(off, (10, 2), (80, 3));
            Assert.Catch(() => off.Sim.RestoreRetainedSnapshot(snapshot), "a world by the clock reads a save by debt");
            Assert.DoesNotThrow(() => off.Step(100));
            Assert.Catch(() => Saving().Sim.RestoreRetainedSnapshot(off.Sim.SaveRetainedSnapshot()), "a world by debt reads a save by the clock");
            M6World other = World(Units(), Pack(reentry: 61));
            Kills(other, (10, 2), (80, 3));
            Assert.Catch(() => other.Sim.RestoreRetainedSnapshot(snapshot), "another delay");
        }

        // A10: the entered count and the successes are in the bytes and in the hash, each on its own.
        [Test]
        public void A10_TheCountsAreSavedAndHashed()
        {
            M6World a = World(Units(), Pack()), b = World(Units(), Pack()), c = World(Units(), Pack());
            Go(a); Go(b); Go(c);
            Assert.That((Saved(a.Reinforce), HashOf(a.Reinforce), a.Hash), Is.EqualTo((Saved(b.Reinforce), HashOf(b.Reinforce), b.Hash)), "equal before");
            b.Reinforce.NoteTakeover(1);
            Assert.That(Counts(b, 1), Is.EqualTo((0L, 1L, -1L, 0L)));
            Assert.That(Saved(b.Reinforce), Is.Not.EqualTo(Saved(a.Reinforce)), "entered is in the bytes");
            Assert.That(HashOf(b.Reinforce), Is.Not.EqualTo(HashOf(a.Reinforce)), "entered is in the hash");
            Assert.That(b.Hash, Is.Not.EqualTo(a.Hash), "and in the world's hash");
            c.Reinforce.NoteTakeover(2);
            Assert.That(Saved(c.Reinforce), Is.Not.EqualTo(Saved(b.Reinforce)), "each team has its own");
            Assert.That(HashOf(c.Reinforce), Is.Not.EqualTo(HashOf(b.Reinforce)));
            // Entered 1 by a takeover against entered 1 by an entry here (successes 1): only the successes differ.
            M6World entry = World(Units(), Pack());
            Kill(entry, D, 2);
            GoTo(entry, D + Reentry);
            Assert.That((Counts(entry, 1).entered, Counts(entry, 1).successes, Counts(b, 1).entered, Counts(b, 1).successes), Is.EqualTo((1L, 1L, 1L, 0L)));
            Assert.That(Saved(entry.Reinforce), Is.Not.EqualTo(Saved(b.Reinforce)), "successes are in the bytes");
            Assert.That(HashOf(entry.Reinforce), Is.Not.EqualTo(HashOf(b.Reinforce)), "successes are in the hash");
            // The pack digest and the deployer's key lead the bytes, as without the switch.
            Assert.That(BitConverter.ToUInt64(Saved(a.Reinforce), 0), Is.EqualTo(a.Recipe.Match.PolicyDigest));
        }

        // The session reports each team's owed and entered counts only in a world with the switch, through SessionReentry
        // (the session's own public members are a locked list).
        [Test]
        public void TheSessionReportsTheCounts()
        {
            M6World w = World(Units(), Pack());
            Kill(w, D, 1, 2, 3);
            GoTo(w, D + Reentry);
            Assert.That(SessionReentry.Has(w.Session), Is.True);
            // Three died on one tick and one has entered: two are still owed.
            Assert.That((SessionReentry.TryCounts(w.Session, 1, out long owed, out long entered), owed, entered), Is.EqualTo((true, 2L, 1L)));
            Assert.That((SessionReentry.TryCounts(w.Session, 2, out owed, out entered), owed, entered), Is.EqualTo((true, 0L, 0L)));
            foreach (int outside in new[] { 0, 3, -1 })
            {
                (bool, long, long) got = (true, -1L, -1L);
                Assert.DoesNotThrow(() => { bool known = SessionReentry.TryCounts(w.Session, outside, out long o, out long e); got = (known, o, e); }, "team " + outside);
                Assert.That(got, Is.EqualTo((false, 0L, 0L)), "team " + outside);
            }
            M6World off = World(Units(), Pack(reentry: 0));
            Assert.DoesNotThrow(() => off.Step(Interval));
            Assert.That(off.Reinforce.Successes(1), Is.EqualTo(1L));
            Assert.That((SessionReentry.Has(off.Session), SessionReentry.TryCounts(off.Session, 1, out owed, out entered), owed, entered), Is.EqualTo((false, false, 0L, 0L)));
            Assert.Throws<ArgumentNullException>(() => SessionReentry.Has(null));
            Assert.Throws<ArgumentNullException>(() => SessionReentry.TryCounts(null, 1, out _, out _));
        }

        // With the switch the system needs the simulation; the player's respawn needs the system.
        [Test]
        public void TheSwitchNeedsItsParts()
        {
            M6World w = World(Units(), Pack(respawn: 90), player: true);
            Assert.Throws<ArgumentNullException>(() => new ReinforceSystem(w.Combat, w.Recipe.Match, w.Capture, null));
            Assert.Throws<ArgumentNullException>(() => new RespawnSystem(w.Sim, w.Combat, w.Recipe.Match, w.Capture, null, null));
            M6World off = World(Units(), Pack(reentry: 0, respawn: 90), player: true);
            Assert.DoesNotThrow(() => new ReinforceSystem(off.Combat, off.Recipe.Match, off.Capture, null));
            Assert.DoesNotThrow(() => new RespawnSystem(off.Sim, off.Combat, off.Recipe.Match, off.Capture, null, null));
        }
    }
}
