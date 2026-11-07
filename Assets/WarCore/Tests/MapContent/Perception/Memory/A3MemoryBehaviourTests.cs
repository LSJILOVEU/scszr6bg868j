using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 2: the memory changes no behaviour. Each pair runs 600 ticks with M > 0 and M = 0 ("targetMemoryTicks": 0);
    // every unit's position (raw), life and every fire record are equal tick by tick, while the state hash differs from
    // tick 0 (the system table and the empty state are hashed). The counters prove the runs reach the branches that matter.
    public sealed class A3MemoryBehaviourTests
    {
        private static readonly SimWide Arrival = new SimVec3(SimFix.Ratio(1, 4), default, default).LengthSquared;

        // One line per unit (position raw, dead) and one per fire record.
        private static List<string> Frame(M6World w, int units, long tick)
        {
            var lines = new List<string>();
            for (ulong key = 1; key <= (ulong)units; key++)
            {
                SimVec3 p = w.Has<Position>(key) ? w.Read<Position>(key).Value : default;
                lines.Add(tick + " u" + key + " " + p.X.Raw + "," + p.Y.Raw + "," + p.Z.Raw + " " + w.Dead(key));
            }
            foreach (FireRecord f in w.Combat.Fires)
                lines.Add(tick + " f " + f.Shooter.Value + ">" + f.Target.Value + " " + f.Origin.X.Raw + "," + f.Origin.Y.Raw + "," + f.Origin.Z.Raw
                    + " " + f.End.X.Raw + "," + f.End.Y.Raw + "," + f.End.Z.Raw + " " + f.Distance.Raw + " " + f.DamageRaw + " " + f.ShooterTeam);
            return lines;
        }

        private static void Compare(M6World on, M6World off, int units, Func<long, InputFrame> script, Action<M6World, long> before, Action<M6World, long> after)
        {
            Assert.That(on.Hash, Is.Not.EqualTo(off.Hash), "the state hashes differ from tick 0");
            Assert.That(Frame(on, units, 0), Is.EqualTo(Frame(off, units, 0)));
            int fires = 0;
            for (long t = 1; t <= 600; t++)
            {
                before(on, t);
                on.Sim.Step(script(t)); off.Sim.Step(script(t));
                Assert.That(Frame(on, units, t), Is.EqualTo(Frame(off, units, t)), "tick " + t);
                fires += on.Combat.Fires.Length;
                after(on, t);
            }
            TestContext.Out.WriteLine("fires " + fires);
        }

        // Counts, on the M > 0 side, ticks with at least one entry, and selections made on a thinking tick by a unit that,
        // before that step, had a move order more than the arrival distance away (so the AI's stop on selection really runs).
        private static (int stored, int stoppedOnSight) Walkers(M6World on, M6World off, int units, Func<long, InputFrame> script)
        {
            Assert.That(off.System<TargetMemory>(), Is.Null);
            IMoveOrders moves = L4.Moves(on);
            var walking = new bool[units + 1];
            int stored = 0, stoppedOnSight = 0;
            Compare(on, off, units, script,
                (w, t) =>
                {
                    for (ulong key = 1; key <= (ulong)units; key++)
                    {
                        walking[key] = false;
                        if (!w.Has<Position>(key) || w.Dead(key)) continue;
                        walking[key] = moves.TryGetOrder(A3.Id(w, key), out SimVec3 target)
                            && CombatSystem.Horizontal(w.Read<Position>(key).Value - target).LengthSquared > Arrival;
                    }
                },
                (w, t) =>
                {
                    if (A3.Count(w) > 0) stored++;
                    for (ulong key = 1; key <= (ulong)units; key++)
                        if (walking[key] && A3.Recall(w, key, t, out _, out _, out long seen) && seen == t) stoppedOnSight++;
                });
            TestContext.Out.WriteLine("stored " + stored + " stoppedOnSight " + stoppedOnSight);
            return (stored, stoppedOnSight);
        }

        // 2(a): fixture D of A2 with grass 500 (damage, walls, grass), M = 300 against 0. In this fixture no unit selects while
        // walking: unit 4 takes the point's only slot first, so team 1 never gets an order, and unit 3 holds in the grass.
        [Test]
        public void FixtureD_SameRun()
        {
            (int stored, int stoppedOnSight) = Walkers(A2.D(A3.Pack(A3.Grass500, 300)), A2.D(A3.Pack(A3.Grass500, 0)), 4, t => default);
            Assert.That(stored, Is.EqualTo(StoredD));
            Assert.That(stoppedOnSight, Is.EqualTo(0));
        }

        // 2(c): the stop on selection, reached. Observer A (key 1, armed, 0.1 m per tick) walks from cell (10, 10) to A2's point
        // on cell (30, 10); B (key 2, unarmed, squad hold) stands on cell (32, 20), more than 20 m from A's start. A selects B
        // while walking (from x >= 15.2 m) and stops there. M = 300 against 0.
        [Test]
        public void WalkingObserver_SameRun()
        {
            Func<int, M6World> make = m => new M6World(A2.Recipe(new[] { A2.Soldier(1, 1, A2.Cell(10, 10), 0, 3000), A3.Target(2, 2, A2.Cell(32, 20)) },
                A2.Combat(0), A3.Pack(A3.Grass500, m), standing: true), WcMapWriter.Write(A2.Map(A2.DWalls)));
            (int stored, int stoppedOnSight) = Walkers(make(300), make(0), 2, t => t == 1 ? A3.Command(SquadCommandKind.Hold, 2) : default);
            Assert.That(stored, Is.EqualTo(StoredWalking));
            Assert.That(stoppedOnSight, Is.EqualTo(StoppedOnSightWalking));
        }

        internal const int StoredD = 418, StoredWalking = 545, StoppedOnSightWalking = 1, RememberedNotSeenF = 5;

        // 2(b): the memory fixture of acceptance 4 (B hides in the grass), M = 60 against 0. Counted: A's thinking ticks on which
        // the reader answers but the entry is older than the tick (remembered, not seen).
        [Test]
        public void MemoryFixture_SameRun()
        {
            Func<int, M6World> make = m => A3.World(new[] { A3.Observer(), A3.Target(A3.KeyB, 2, A2.Cell(21, 18)) }, m);
            M6World on = make(60), off = make(0);
            Assert.That(off.System<TargetMemory>(), Is.Null);
            int remembered = 0;
            Compare(on, off, 2,
                t => t == 1 ? A3.Command(SquadCommandKind.Hold, 2) : t == 20 ? A3.Command(SquadCommandKind.MoveTo, 2, A2.Cell(21, 24)) : default,
                (w, t) => { },
                (w, t) =>
                {
                    if ((t - 6) % A2.Think == 0 && A3.Recall(w, A3.KeyA, t, out _, out _, out long seen) && seen < t) remembered++;
                });
            TestContext.Out.WriteLine("remembered " + remembered);
            Assert.That(remembered, Is.EqualTo(RememberedNotSeenF));
        }
    }
}
