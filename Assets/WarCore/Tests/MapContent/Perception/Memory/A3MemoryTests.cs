using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 3-9 and 13 on the memory fixture (A3Fixture.cs). A thinks on ticks 6, 16, 26, ... (asserted). B holds from
    // tick 1 and gets its only move command on tick 20, after A has thought twice. "s" is the last thinking tick on which A
    // selects B; P is compared with B's point at the end of tick s - 1 (the AI records in Requests, movement comes after).
    public sealed class A3MemoryTests
    {
        private const long Think = 6, MoveTick = 20;
        private static readonly SimVec3 BStart = A2.Cell(21, 18);  // dirt, 8.06 m from A, nothing between
        private static readonly SimVec3 BGrass = A2.Cell(21, 24);  // grass, 14.04 m from A: beyond Lc = 10 m, within L = 20 m
        private static readonly SimVec3 WallStart = A2.Cell(18, 19), WallEnd = A2.Cell(12, 19);  // dirt; the wall x 15, z 13..17 hides the end
        private static readonly SimVec3 FarStart = A2.Cell(28, 16), FarEnd = A2.Cell(36, 28);    // dirt; the end is 22.8 m away

        private static M6World World(SimVec3 bStart, int memory, params UnitSpawn[] more)
        {
            var units = new UnitSpawn[2 + more.Length];
            units[0] = A3.Observer(); units[1] = A3.Target(A3.KeyB, 2, bStart);
            Array.Copy(more, 0, units, 2, more.Length);
            M6World w = A3.World(units, memory);
            Assert.That(A3.FirstThink(w, A3.KeyA), Is.EqualTo(Think), "A's thinking phase");
            Assert.That(w.Read<Position>(A3.KeyA).Value, Is.EqualTo(A3.AAt));
            return w;
        }

        private static InputFrame Script(long tick, SimVec3 to) =>
            tick == 1 ? A3.Command(SquadCommandKind.Hold, 2) : tick == MoveTick ? A3.Command(SquadCommandKind.MoveTo, 2, to) : default;

        // Steps one tick (the scripted command, else a hold for a target whose move just ended); returns B's point at the end
        // of the previous tick. A never moves; B (and C from tick 2) stay under squad command.
        private static SimVec3 Step(M6World w, InputFrame scripted, List<long> holds = null, bool withC = false)
        {
            SimVec3 before = w.Read<Position>(A3.KeyB).Value;
            w.Sim.Step(A3.Drive(w, scripted, holds, withC));
            long tick = w.Sim.Clock.Tick;
            Assert.That(w.Read<Position>(A3.KeyA).Value, Is.EqualTo(A3.AAt), "A stands still, tick " + tick);
            Assert.That(A3.Commanded(w, A3.KeyB), Is.True, "B under squad command, tick " + tick);
            if (withC && tick >= 2) Assert.That(A3.Commanded(w, A3.KeyC), Is.True, "C under squad command, tick " + tick);
            return before;
        }

        private static void AssertEntry(M6World w, long tick, ulong key, SimVec3 at, long seen)
        {
            Assert.That(A3.Recall(w, A3.KeyA, tick, out StableId target, out SimVec3 p, out long s), Is.True, "recall, tick " + tick);
            Assert.That(target, Is.EqualTo(A3.Id(w, key)), "target, tick " + tick);
            Assert.That(new[] { p.X.Raw, p.Y.Raw, p.Z.Raw }, Is.EqualTo(new[] { at.X.Raw, at.Y.Raw, at.Z.Raw }), "P, tick " + tick);
            Assert.That(s, Is.EqualTo(seen), "S, tick " + tick);
        }

        private static bool InGrass(SimVec3 p) => A2.InPatch((int)(p.X.Raw / A2.S), (int)(p.Z.Raw / A2.S));

        // 3: recorded on the thinking tick from the point before the step; the next thinking tick records again.
        [Test]
        public void Saw_RecordsThePointBeforeTheStep()
        {
            const long s = 26;
            M6World w = World(BStart, 60);
            SimVec3 p = default;
            for (long t = 1; t <= s + 10; t++)
            {
                SimVec3 before = Step(w, Script(t, BGrass));
                if (t == s || t == s + 10)
                {
                    AssertEntry(w, t, A3.KeyB, before, t);
                    Assert.That(w.Read<Position>(A3.KeyB).Value, Is.Not.EqualTo(before), "B walks during tick " + t);
                    p = before;
                }
                if (t > s && t < s + 10) AssertEntry(w, t, A3.KeyB, p, s);
            }
        }

        // The reader's refusals: a tick before S, an observer without an entry; every output is the default.
        [Test]
        public void Recall_RefusesWithDefaults()
        {
            M6World w = World(BStart, 60);
            for (long t = 1; t <= 30; t++) Step(w, Script(t, BGrass));
            Assert.That(A3.Recall(w, A3.KeyA, 30, out _, out _, out long seen), Is.True);
            Assert.That(seen, Is.EqualTo(26));
            Assert.That(A3.Recall(w, A3.KeyA, 25, out StableId target, out SimVec3 at, out seen), Is.False, "before S");
            Assert.That(target, Is.EqualTo(default(StableId))); Assert.That(at, Is.EqualTo(default(SimVec3))); Assert.That(seen, Is.EqualTo(0L));
            Assert.That(A3.Recall(w, A3.KeyB, 30, out target, out at, out seen), Is.False, "no entry");
            Assert.That(target, Is.EqualTo(default(StableId))); Assert.That(at, Is.EqualTo(default(SimVec3))); Assert.That(seen, Is.EqualTo(0L));
        }

        // 4, 5, 6: after the last sighting s the entry is frozen for M ticks, readable through s+M-1, refused at s+M before it
        // is deleted, and deleted by the step of s+M. why(w) states, on each later thinking tick, why A no longer sees B.
        private static void LoseAndForget(SimVec3 start, SimVec3 to, long s, long hold, int dx, int dz, Func<M6World, bool> why)
        {
            const int M = 60;
            M6World w = World(start, M);
            SimVec3 p = default;
            bool apart = false;
            var holds = new List<long>();
            for (long t = 1; t <= s + M; t++)
            {
                SimVec3 before = Step(w, Script(t, to), holds);
                if (t == s) p = before;
                if (t >= s && t <= s + M - 1)
                {
                    AssertEntry(w, t, A3.KeyB, p, s);
                    if (!w.Read<Position>(A3.KeyB).Value.Equals(p)) apart = true;
                    if (t > s && (t - Think) % A2.Think == 0) Assert.That(why(w), Is.True, "why B is lost, tick " + t);
                }
                if (t == s + M - 1)
                {
                    Assert.That(A3.Recall(w, A3.KeyA, s + M, out _, out _, out _), Is.False, "window closed at s+M");
                    Assert.That(A3.Count(w), Is.EqualTo(1), "still stored at the end of s+M-1");
                }
            }
            Assert.That(apart, Is.True, "B's real point differs from P while forgotten");
            // B ended its move on 'to' (hold sent on the next tick) and stands there under the hold, dx, dz metres from A.
            Assert.That(holds, Is.EqualTo(new[] { hold }), "the hold after arrival");
            SimVec3 end = w.Read<Position>(A3.KeyB).Value;
            Assert.That(end, Is.EqualTo(to));
            Assert.That(w.Read<SquadStanding>(A3.KeyB).Kind, Is.EqualTo(StandingKind.Hold));
            Assert.That(A3.Flat(end, A3.AAt), Is.EqualTo(new SimVec3(SimFix.FromInt(dx), default, SimFix.FromInt(dz)).LengthSquared));
            Assert.That(A3.Count(w), Is.EqualTo(0), "deleted by the step of s+M");
        }

        [Test]
        public void Grass_LostThenForgotten()
        {
            LoseAndForget(BStart, BGrass, 46, 87, 1, 14, w =>
            {
                SimVec3 b = w.Read<Position>(A3.KeyB).Value;
                SimWide d = A3.Flat(b, A3.AAt);
                return InGrass(b) && d > A3.Squared(10) && d <= A3.Squared(20);
            });
        }

        [Test]
        public void Wall_LostThenForgotten()
        {
            LoseAndForget(WallStart, WallEnd, 56, 81, 10, 9, w =>
            {
                SimVec3 b = w.Read<Position>(A3.KeyB).Value;
                return !InGrass(b) && A3.Flat(b, A3.AAt) <= A3.Squared(20) && !w.Combat.Visible(w.Unit(A3.KeyA), w.Unit(A3.KeyB));
            });
        }

        [Test]
        public void Range_LostThenForgotten()
        {
            LoseAndForget(FarStart, FarEnd, 136, 172, 14, 18, w =>
            {
                SimVec3 b = w.Read<Position>(A3.KeyB).Value;
                return !InGrass(b) && A3.Flat(b, A3.AAt) > A3.Squared(20);
            });
        }

        // 7: while B is remembered (seen last on 46), C walks into range and is selected on 66: the whole entry becomes C's.
        [Test]
        public void NewTarget_ReplacesTheEntry()
        {
            const long lastB = 46, firstC = 66, cMove = 21;
            M6World w = World(BStart, 60, A3.Target(A3.KeyC, 3, A2.Cell(38, 28)));
            SimVec3 pB = default;
            var holds = new List<long>();
            for (long t = 1; t <= 200; t++)
            {
                InputFrame input = t == 2 ? A3.Command(SquadCommandKind.Hold, 3)
                    : t == cMove ? A3.Command(SquadCommandKind.MoveTo, 3, A2.Cell(30, 12)) : Script(t, BGrass);
                SimVec3 cBefore = w.Read<Position>(A3.KeyC).Value;
                SimVec3 before = Step(w, input, holds, true);
                if (t == lastB) pB = before;
                if (t >= lastB && t < firstC) AssertEntry(w, t, A3.KeyB, pB, lastB);
                if (t == firstC) AssertEntry(w, t, A3.KeyC, cBefore, firstC);
                if (t >= firstC)
                {
                    Assert.That(A3.Recall(w, A3.KeyA, t, out StableId target, out _, out _), Is.True, "tick " + t);
                    Assert.That(target, Is.EqualTo(A3.Id(w, A3.KeyC)), "never B again, tick " + t);
                }
            }
            Assert.That(holds, Is.EqualTo(new long[] { 87 }), "B's hold after arrival; C is still walking at 200");
        }

        // 8: B (seen last on 46) walks back out of the grass and is selected again on 66, 76 and 86 before hiding again:
        // the entry never lapses, S follows the new sightings, and the window ends at 86 + M - 1.
        [Test]
        public void SeenAgain_RestartsTheWindow()
        {
            const int M = 60;
            const long first = 46, again = 66, s2 = 86;
            M6World w = World(BStart, M);
            SimVec3 p = default;
            long seenAt = 0;
            var holds = new List<long>();
            for (long t = 1; t <= s2 + M; t++)
            {
                InputFrame input = t == 60 ? A3.Command(SquadCommandKind.MoveTo, 2, A2.Cell(21, 20))
                    : t == 80 ? A3.Command(SquadCommandKind.MoveTo, 2, BGrass) : Script(t, BGrass);
                SimVec3 before = Step(w, input, holds);
                if (t == first || t == again || t == 76 || t == s2) { p = before; seenAt = t; }
                if (t >= first && t <= s2 + M - 1) AssertEntry(w, t, A3.KeyB, p, seenAt);
                if (t == again) Assert.That(again - first, Is.LessThan(M), "seen again inside the window");
                if (t == s2 + M - 1)
                {
                    Assert.That(A3.Recall(w, A3.KeyA, s2 + M, out _, out _, out _), Is.False);
                    Assert.That(A3.Count(w), Is.EqualTo(1));
                }
            }
            Assert.That(A3.Count(w), Is.EqualTo(0));
            Assert.That(holds, Is.EqualTo(new long[] { 126 }), "B's hold after its last arrival");
            Assert.That(w.Read<Position>(A3.KeyB).Value, Is.EqualTo(BGrass));
        }

        // 9(a): B killed on tick 70 (death settles after Queries): unreadable at the end of 70, stored until the step of 71.
        [Test]
        public void TargetDeath_ReadAndDelete()
        {
            M6World w = World(BStart, 60);
            Wp16TestWorld.KillAt(w, 70, A3.KeyB);
            for (long t = 1; t <= 69; t++) Step(w, Script(t, BGrass));
            Assert.That(A3.Recall(w, A3.KeyA, 69, out _, out _, out long seen), Is.True);
            Assert.That(seen, Is.EqualTo(46));
            Step(w, default);
            Assert.That(w.Dead(A3.KeyB), Is.True);
            Assert.That(A3.Recall(w, A3.KeyA, 70, out _, out _, out _), Is.False, "the reader refuses a dead target");
            Assert.That(A3.Count(w), Is.EqualTo(1), "the entry is still stored at the end of 70");
            w.Sim.Step(default);
            Assert.That(A3.Count(w), Is.EqualTo(0), "deleted on 71");
        }

        // 9(b): A killed on tick 70: deleted on 71.
        [Test]
        public void ObserverDeath_Deletes()
        {
            M6World w = World(BStart, 60);
            Wp16TestWorld.KillAt(w, 70, A3.KeyA);
            for (long t = 1; t <= 70; t++)
            {
                w.Sim.Step(Script(t, BGrass));
                if (t == 69) Assert.That(A3.Count(w), Is.EqualTo(1));
            }
            Assert.That(w.Dead(A3.KeyA), Is.True);
            w.Sim.Step(default);
            Assert.That(A3.Count(w), Is.EqualTo(0));
        }

        // 9(c): A changed to team 3 between ticks 69 and 70; only the AI's Forget can clear it (the memory's step ignores teams).
        [Test]
        public void ObserverLeavesTheTeams_Forgets()
        {
            M6World w = World(BStart, 60);
            for (long t = 1; t <= 69; t++) Step(w, Script(t, BGrass));
            Assert.That(A3.Count(w), Is.EqualTo(1));
            Combatant c = w.Read<Combatant>(A3.KeyA);
            w.Set(A3.KeyA, new Combatant(3, c.Weapon, c.Ammo, c.DueMilli, c.SpreadStored, c.SpreadLastMilli));
            w.Sim.Step(default);
            Assert.That(A3.Count(w), Is.EqualTo(0));
        }

        // 9(d): A's position removed between ticks 69 and 70 (no reinforcement is due on 70, so A stays without one).
        [Test]
        public void ObserverLosesItsPosition_Forgets()
        {
            M6World w = World(BStart, 60);
            for (long t = 1; t <= 69; t++) Step(w, Script(t, BGrass));
            Assert.That(A3.Count(w), Is.EqualTo(1));
            Assert.That(w.Reinforce.NextTick(1), Is.GreaterThan(70L));
            w.Sim.Entities.Remove(w.Sim.Entities.Resolve<Position>(), w.Unit(A3.KeyA));
            Assert.That(w.Has<Position>(A3.KeyA), Is.False);
            w.Sim.Step(default);
            Assert.That(w.Has<Position>(A3.KeyA), Is.False);
            Assert.That(A3.Count(w), Is.EqualTo(0));
        }

        // 13: M = 5 under a thinking interval of 10, B always visible and still: the memory breaks between thinking ticks.
        [Test]
        public void ShorterThanTheThinkingInterval()
        {
            const long s = 16;
            M6World w = World(BStart, 5);
            for (long t = 1; t <= s + 10; t++)
            {
                Step(w, t == 1 ? A3.Command(SquadCommandKind.Hold, 2) : default);
                bool read = A3.Recall(w, A3.KeyA, t, out _, out _, out long seen);
                if (t >= s && t <= s + 4) { Assert.That(read, Is.True, "tick " + t); Assert.That(seen, Is.EqualTo(s)); }
                if (t == s + 4)
                {
                    Assert.That(A3.Recall(w, A3.KeyA, s + 5, out _, out _, out _), Is.False);
                    Assert.That(A3.Count(w), Is.EqualTo(1));
                }
                if (t == s + 5) Assert.That(A3.Count(w), Is.EqualTo(0));
                if (t >= s + 5 && t <= s + 9) Assert.That(read, Is.False, "tick " + t);
                if (t == s + 10) { Assert.That(read, Is.True); Assert.That(seen, Is.EqualTo(s + 10)); }
            }
            Assert.That(w.Read<Position>(A3.KeyB).Value, Is.EqualTo(BStart));
        }
    }
}
