using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L5 acceptance 1-4 on fixture G. The gate is this package's own engineering target (each team >= 9 of 10 arrived, no
    // stuck unit at any time); it is not comparable with the flat M7 "inside the circle" count.
    public sealed class L5GarrisonTests
    {
        private const long S = L5.S;

        private sealed class Run
        {
            internal M6World World;
            internal L5.Orders Orders;
            internal GuardSlots Slots;
            internal L5StuckMeter Meter;
            internal SimVec3[] Start;
            internal int[] FirstArrived;
            internal bool[] ArrivedEnd;
            internal int[] SlotEnd;
        }

        // Runs fixture G for T ticks, checking every tick that nobody died, fired or swung. pin: key whose position is written
        // back to its start after every step (the stuck counter-example), or 0.
        private static Run Go(ulong pin = 0)
        {
            var r = new Run { World = L5.World() };
            M6World w = r.World;
            r.Orders = L5.Install(w);
            r.Slots = new GuardSlots(w.Recipe.Match.Points);
            int n = L5.Table.Length;
            r.Meter = new L5StuckMeter(n);
            r.Start = L5.Positions(w);
            r.FirstArrived = new int[n];
            for (int i = 0; i < n; i++) r.FirstArrived[i] = -1;
            var arrived = new bool[n];
            for (int tick = 0; tick <= L5.Ticks; tick++)
            {
                if (tick > 0)
                {
                    w.Step();
                    if (pin != 0)
                    {
                        Position p = w.Read<Position>(pin);
                        p.Value = r.Start[(int)pin - 1];
                        w.Set(pin, p);
                    }
                }
                Assert.That(w.Combat.Fires.Length, Is.EqualTo(0), "no shot at tick " + tick);
                Assert.That(w.Combat.Strikes.Length, Is.EqualTo(0), "no swing at tick " + tick);
                for (int i = 0; i < n; i++)
                {
                    ulong key = L5.Table[i].key;
                    Assert.That(w.Dead(key), Is.False, "unit " + key + " alive at tick " + tick);
                    Assert.That(w.Read<Combatant>(key).DueMilli, Is.EqualTo(0), "unit " + key + " never fired or swung, tick " + tick);
                    arrived[i] = L5.Arrived(w, r.Orders, r.Slots, i, out _);
                    if (arrived[i] && r.FirstArrived[i] < 0) r.FirstArrived[i] = tick;
                }
                r.Meter.Sample(tick, L5.Positions(w), arrived);
            }
            r.ArrivedEnd = new bool[n]; r.SlotEnd = new int[n];
            for (int i = 0; i < n; i++) r.ArrivedEnd[i] = L5.Arrived(w, r.Orders, r.Slots, i, out r.SlotEnd[i]);
            return r;
        }

        [Test]
        public void L5_EveryGuardSlotIsStandable()
        {
            M6World w = L5.World();
            L5.Orders orders = L5.Install(w);
            var slots = new GuardSlots(w.Recipe.Match.Points);
            Assert.That(slots.Count(0), Is.EqualTo(37));
            Assert.That(slots.Count(1), Is.EqualTo(37));
            for (int p = 0; p < 2; p++)
                for (int s = slots.Start(p); s < slots.Start(p) + slots.Count(p); s++)
                    Assert.That(orders.CanStandAt(slots.At(s)), Is.True, "point " + p + " slot " + s);
        }

        // Acceptance 1: the gate.
        [Test]
        public void L5_Gate_EachTeamArrives_NobodyStuck()
        {
            Run r = Go();
            int[] arrived = new int[3];
            for (int i = 0; i < L5.Table.Length; i++)
            {
                if (r.ArrivedEnd[i]) arrived[L5.Table[i].team]++;
                TestContext.Out.WriteLine($"L5 G key={L5.Table[i].key} team={L5.Table[i].team} arrived={r.ArrivedEnd[i]} slot={r.SlotEnd[i]} first={r.FirstArrived[i]} stuckEver={r.Meter.Ever(i)}");
            }
            TestContext.Out.WriteLine($"L5 G team1={arrived[1]}/10 team2={arrived[2]}/10 stuckNow={r.Meter.CurrentCount} stuckEver={r.Meter.EverCount}");
            Assert.That(arrived[1], Is.GreaterThanOrEqualTo(9), "team 1 arrived; report and stop, do not tune");
            Assert.That(arrived[2], Is.GreaterThanOrEqualTo(9), "team 2 arrived; report and stop, do not tune");
            Assert.That(r.Meter.CurrentCount, Is.Zero, "stuck at the end");
            Assert.That(r.Meter.EverCount, Is.Zero, "stuck at any time");
            var used = new HashSet<int>();
            for (int i = 0; i < L5.Table.Length; i++)
                if (r.ArrivedEnd[i]) Assert.That(used.Add(r.SlotEnd[i]), Is.True, "two arrived units share slot " + r.SlotEnd[i]);
        }

        // Acceptance 2: not an empty run, and the observer does not change the world.
        [Test]
        public void L5_NoFire_AndEveryUnitWasOrderedAndMoved()
        {
            Run r = Go();
            M6World w = r.World;
            for (int i = 0; i < L5.Table.Length; i++)
            {
                var (key, team, _, _) = L5.Table[i];
                Assert.That(r.Orders.CountFor(L5.Id(w, key).Value), Is.GreaterThanOrEqualTo(1), "unit " + key + " was ordered");
                long moved2 = L5.Horizontal2(w.Read<Position>(key).Value, r.Start[i]);
                long floor = team == 1 ? 12 * S : 3 * S;
                Assert.That(moved2, Is.GreaterThanOrEqualTo(floor * floor), "unit " + key + " moved far enough");
            }
        }

        [Test]
        public void L5_ObserverDoesNotChangeTheWorld()
        {
            M6World plain = L5.World(), watched = L5.World();
            L5.Install(watched);
            for (int tick = 0; tick <= L5.Ticks; tick++)
            {
                if (tick > 0) { plain.Step(); watched.Step(); }
                Assert.That(watched.Hash, Is.EqualTo(plain.Hash), "tick " + tick);
            }
            Assert.That(watched.Sim.SaveRetainedSnapshot(), Is.EqualTo(plain.Sim.SaveRetainedSnapshot()));
        }

        // Acceptance 3: the run went where it should.
        [Test]
        public void L5_Semantics_RidgeHeight_Mud_NoCapture_RightPoints()
        {
            Run r = Go();
            M6World w = r.World;
            int mud = 0;
            for (int i = 0; i < L5.Table.Length; i++)
            {
                if (!r.ArrivedEnd[i]) continue;
                var (key, team, _, _) = L5.Table[i];
                SimVec3 p = w.Read<Position>(key).Value;
                Assert.That(r.Slots.PointOf(r.SlotEnd[i]), Is.EqualTo(L5.Target(team)), "unit " + key + " guards its own point");
                if (team == 1) Assert.That(p.Y.Raw, Is.GreaterThanOrEqualTo(4 * S), "unit " + key + " stands on the ridge");
                else
                {
                    long cx = p.X.Raw >> 16, cz = p.Z.Raw >> 16;
                    if (cx >= 36 && cx < 40 && cz >= 4 && cz < 12) { mud++; Assert.That(p.Y.Raw, Is.EqualTo(0), "unit " + key + " in the mud is on flat ground"); }
                }
            }
            Assert.That(mud, Is.GreaterThanOrEqualTo(1), "at least one team-2 unit stands in a mud cell");
            Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(0));
            Assert.That(w.Capture.Points[1].Owner, Is.EqualTo(0));
        }

        // Acceptance 4: the definitions themselves.
        private static readonly SimVec3 Zero = default;
        private static SimVec3 X(long raw) => new SimVec3(SimFix.FromRaw(raw), default, default);

        // Feeds one unit's positions for ticks 0..last and returns the per-tick "stuck now" flags.
        private static bool[] Feed(Func<long, SimVec3> at, int last, Func<long, bool> arrived = null)
        {
            var meter = new L5StuckMeter(1);
            var flags = new bool[last + 1];
            for (long t = 0; t <= last; t++)
            {
                meter.Sample(t, new[] { at(t) }, new[] { arrived != null && arrived(t) });
                flags[t] = meter.Now(0);
            }
            return flags;
        }

        [Test]
        public void L5_Meter_StandingStill_IsStuckFromTick300()
        {
            bool[] f = Feed(t => Zero, 301);
            Assert.That(f[299], Is.False); Assert.That(f[300], Is.True); Assert.That(f[301], Is.True);
        }

        [Test]
        public void L5_Meter_OneMetreIn300Ticks_IsNotStuck_OneRawLessIs()
        {
            Assert.That(Feed(t => t >= 300 ? X(S) : Zero, 300)[300], Is.False, "exactly 1 m is progress");
            Assert.That(Feed(t => t >= 300 ? X(S - 1) : Zero, 300)[300], Is.True, "1 raw short of 1 m is a stall");
        }

        [Test]
        public void L5_Meter_ArrivedAndStill_IsNotStuck()
        {
            Assert.That(Feed(t => Zero, 400, t => true)[400], Is.False);
        }

        [Test]
        public void L5_Meter_BackAndForth_IsStuck()
        {
            // 50-tick legs, 2 m out and back: the window of 300 ticks spans whole round trips, the net displacement is 0.
            bool[] f = Feed(t => (t / 50) % 2 == 1 ? X(2 * S) : Zero, 400);
            Assert.That(f[300], Is.True); Assert.That(f[400], Is.True);
        }

        [Test]
        public void L5_Meter_GiveUpThenStill_IsStuckAfterTheWindow()
        {
            // Synthetic 1 m per tick until tick 100, then a give-up: no order, not arrived, standing still.
            bool[] f = Feed(t => X(Math.Min(t, 100) * S), 400);
            Assert.That(f[399], Is.False, "p(399) - p(99) = 1 m");
            Assert.That(f[400], Is.True);
        }

        [Test]
        public void L5_Meter_QueueThenWalk_WindowRolls()
        {
            // Standing 350 ticks (a queue), then 1024 raw (1/64 m) per tick: 64 ticks of walking make 1 m.
            var meter = new L5StuckMeter(1);
            bool[] f = new bool[601];
            for (long t = 0; t <= 600; t++)
            {
                meter.Sample(t, new[] { X(Math.Max(0, t - 350) * 1024) }, new[] { false });
                f[t] = meter.Now(0);
            }
            Assert.That(f[300], Is.True); Assert.That(f[413], Is.True);
            Assert.That(f[414], Is.False, "p(414) - p(114) = 1 m");
            Assert.That(f[600], Is.False);
            Assert.That(meter.EverCount, Is.EqualTo(1));
        }

        [Test]
        public void L5_Meter_StillFor300ThenWalk_Tick301IsStillStuck()
        {
            bool[] f = Feed(t => X(Math.Max(0, t - 300) * 1024), 301);
            Assert.That(f[300], Is.True);
            Assert.That(f[301], Is.True, "p(301) - p(1) = 1/64 m");
        }

        [Test]
        public void L5_Meter_InTheWorld_APinnedUnitIsStuck_AFreeOneIsNot()
        {
            Run pinned = Go(pin: 1);
            Assert.That(pinned.Meter.Ever(0), Is.True, "key 1 written back to its start every tick");
            Run free = Go();
            Assert.That(free.Meter.Ever(0), Is.False);
        }

        [Test]
        public void L5_Arrival_MeasuredFromTheSlotCentre()
        {
            var slots = new GuardSlots(L5.Points);
            SimVec3 c = slots.At(slots.Start(0));
            int s;
            Assert.That(L5.ArrivedAt(slots, 0, false, c + X(S / 4), out s), Is.True, "0.25 m off the slot centre is arrived");
            Assert.That(L5.ArrivedAt(slots, 0, false, c + X(S * 3 / 10), out s), Is.False, "0.3 m off is not");
            Assert.That(L5.ArrivedAt(slots, 0, true, c, out s), Is.False, "a held order is not arrived");
            Assert.That(L5.ArrivedAt(slots, 1, false, c, out s), Is.False, "the other point's slot is not arrived");
        }
    }
}
