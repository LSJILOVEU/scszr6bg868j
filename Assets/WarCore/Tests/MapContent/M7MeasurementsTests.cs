using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M7MeasurementsTests
    {
        [Test]
        public void RealWindowReportsPeakTick()
        {
            var w = M7BehaviorLockTests.Real(); w.Step(100);
            var times = new double[600]; long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < times.Length; i++)
            {
                long start = Stopwatch.GetTimestamp(); w.Sim.Step(default);
                times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            int peak = 0;
            for (int i = 1; i < times.Length; i++) if (times[i] > times[peak]) peak = i;
            int peakTick = 101 + peak; double mean = times.Average(), maximum = times[peak]; Array.Sort(times);
            TestContext.Out.WriteLine($"WINDOW_PEAK mean={mean:F6} p95={times[569]:F6} max={maximum:F6} peakTick={peakTick} gc={allocated}");
            var replay = M7BehaviorLockTests.Real(); replay.Step(700);
            Assert.That(w.Hash, Is.EqualTo(replay.Hash));
        }
        [Category("Manual"), Test, Explicit("Separate uninstrumented Release performance run; no timing gate.")]
        public void WholeMatchReal100Vs100()
        {
            // Warm JIT and all systems on a separate world; timing includes only Step and timestamp writes.
            var warm = M7BehaviorLockTests.Real(endless: true); warm.Step(900);
            var w = M7BehaviorLockTests.Real(); var times = new double[9000]; int count = 0;
            do
            {
                long start = Stopwatch.GetTimestamp(); w.Sim.Step(default);
                times[count++] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            } while (w.Match.Winner == 0 && count < times.Length);
            var replay = M7BehaviorLockTests.Real(); int first = 0, last = 0;
            for (int tick = 1; tick <= count; tick++)
            {
                replay.Step(); if (replay.Combat.Fires.Length == 0) continue;
                if (first == 0) first = tick; last = tick;
            }
            Assert.That(replay.Hash, Is.EqualTo(w.Hash)); Assert.That(w.Match.Winner, Is.Not.Zero);
            void Report(string phase, int from, int to)
            {
                if (to < from) { TestContext.Out.WriteLine($"WHOLE phase={phase} n=0"); return; }
                double[] sample = times.Skip(from - 1).Take(to - from + 1).ToArray();
                int peak = from; for (int tick = from; tick <= to; tick++) if (times[tick - 1] > times[peak - 1]) peak = tick;
                double mean = sample.Average(); Array.Sort(sample);
                TestContext.Out.WriteLine($"WHOLE phase={phase} ticks={from}..{to} mean={mean:F6} p95={sample[(int)Math.Ceiling(sample.Length * .95) - 1]:F6} max={times[peak - 1]:F6} peakTick={peak}");
            }
            Report("all", 1, count); Report("approach", 1, first - 1); Report("engagement", first, last); Report("tail", last + 1, count);
            TestContext.Out.WriteLine($"WHOLE_END end={w.Match.EndTick} winner={w.Match.Winner} reason={w.Match.EndReason} hash={w.Hash:X16}");
        }
        [TestCase(100)] [TestCase(200)]
        public void GuardCrowding1200TicksIsMeasuredOnTheLatticeSlots(int count)
        {
            var units = new UnitSpawn[count];
            for (int i = 0; i < count; i++)
            {
                int slot = i % 100, team = i < 100 ? 1 : 2;
                long x = 10000 + slot % 10 * 750;
                if (team == 2) x = 80000 - x;
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, 30000 + slot / 10 * 1500, 6000, team, -1);
            }
            var point = new ControlPointDef(1, M6World.P(40, 40), SimFix.FromInt(6));
            // Test-only tiny sight keeps both teams outside enemy sight even when interleaved at the point.
            var pack = M6World.Pack(new[] { point }, ticks: 100000, think: 30, max: 100000, sight: SimFix.FromRaw(1));
            var w = new M6World(M6World.Endless(M6World.RecipeOf(units, pack)), WcMapWriter.Write(M3TestWorld.Boxes(80, 80)));
            var meter = new M7StuckMeter(w); meter.Sample();
            var lattice = GuardSlots.Build(point.Center, point.Radius);
            var maxOccupancy = new int[lattice.Length];
            var positions = new SimVec3[count]; int enemySightTicks = 0; var ordersBeforeArrival = new int[count];
            var lastTarget = new SimVec3[count]; var lastActive = new bool[count]; var arrivedTick = new int[count];
            int duplicateTargetTicks = 0, ordersAfterArrival = 0;
            const long oneSquared = SimFix.Scale * SimFix.Scale;
            long Squared(SimVec3 a, SimVec3 b) { long x = a.X.Raw - b.X.Raw, z = a.Z.Raw - b.Z.Raw; return x * x + z * z; }
            for (int tick = 1; tick <= 1200; tick++)
            {
                w.Step(); meter.Sample();
                var seen = new System.Collections.Generic.HashSet<(long, long)>(); bool duplicate = false;
                for (int i = 0; i < count; i++)
                {
                    ulong key = (ulong)i + 1;
                    positions[i] = w.Read<Position>(key).Value;
                    var intent = w.Read<MoveIntent>(key);
                    if (intent.Active && !seen.Add((intent.Target.X.Raw, intent.Target.Z.Raw))) duplicate = true;
                    bool newOrder = intent.Active && (!lastActive[i] || !lastTarget[i].Equals(intent.Target));
                    if (newOrder) { if (arrivedTick[i] == 0) ordersBeforeArrival[i]++; else ordersAfterArrival++; }
                    if (lastActive[i] && !intent.Active && arrivedTick[i] == 0) arrivedTick[i] = tick;
                    lastActive[i] = intent.Active; lastTarget[i] = intent.Target;
                }
                if (duplicate) duplicateTargetTicks++;
                for (int s = 0; s < lattice.Length; s++)
                {
                    int occupied = 0;
                    for (int i = 0; i < count; i++) if (Squared(positions[i], lattice[s]) <= oneSquared) occupied++;
                    maxOccupancy[s] = Math.Max(maxOccupancy[s], occupied);
                }
                bool enemy = false;
                if (count == 200) for (int i = 0; i < 100 && !enemy; i++) for (int j = 100; j < 200; j++)
                    if (Squared(positions[i], positions[j]) <= 1) { enemy = true; break; }
                if (enemy) enemySightTicks++;
            }
            int overlapPairs = 0; const long halfSquared = SimFix.Scale * SimFix.Scale / 4;
            for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++) if (Squared(positions[i], positions[j]) < halfSquared) { overlapPairs++; }
            int valid = 0;
            for (int i = 0; i < count; i++) if (w.Has<Position>((ulong)i + 1) && !w.Dead((ulong)i + 1)) valid++;
            int[] inCircle = new int[3];
            for (int i = 0; i < count; i++)
                if (CombatSystem.Horizontal(positions[i] - point.Center).LengthSquared <= new SimVec3(point.Radius, default, default).LengthSquared)
                    inCircle[i < 100 ? 1 : 2]++;
            var cp = w.Capture.Points[0];
            TestContext.Out.WriteLine($"CROWD n={count} valid={valid} slots={lattice.Length} maxInSlot={maxOccupancy.Max()} enemySightTicks={enemySightTicks} duplicateTargetTicks={duplicateTargetTicks} maxOrdersBeforeArrival={ordersBeforeArrival.Max()} ordersAfterArrival={ordersAfterArrival} finalOverlapPairs={overlapPairs}");
            TestContext.Out.WriteLine($"CROWD2 n={count} activeOrders={meter.ActiveCount} currentStuck={meter.CurrentStuckCount} cumulativeStuck={meter.StuckCount} inCircleTeam1={inCircle[1]} inCircleTeam2={inCircle[2]} captureOwner={cp.Owner} capturing={cp.Capturing} progress={cp.Progress}");
            Assert.That(valid, Is.EqualTo(count)); Assert.That(enemySightTicks, Is.Zero);
            Assert.That(cp.Owner, Is.Zero, "No premature capture.");
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(1200));
            if (count == 100)
            {
                // Engineering gates from WP-21 acceptance 2 and 4; a miss means report the numbers, never retune constants.
                Assert.That(meter.ActiveCount, Is.LessThanOrEqualTo(15));
                Assert.That(meter.CurrentStuckCount, Is.LessThanOrEqualTo(15));
                Assert.That(inCircle[1], Is.GreaterThanOrEqualTo(90));
                Assert.That(duplicateTargetTicks, Is.Zero, "Orders target pairwise distinct slots.");
                Assert.That(ordersBeforeArrival.Max(), Is.LessThanOrEqualTo(2));
                Assert.That(ordersAfterArrival, Is.Zero, "No order after arrival.");
            }
        }
    }

    // Copied from M6RunTests.StuckMeter; same eligibility, 301 samples and <1 m displacement.
    internal sealed class M7StuckMeter
    {
        private readonly M6World world;
        private readonly GuardSlots guardSlots;
        private readonly UnitSpawn[] units;
        private readonly SimVec3[,] history;
        private readonly SimVec3[] target;
        private readonly int[] continuous, lastProgress;
        private readonly SimWide[] best;
        private readonly bool[] active, stuck;
        internal readonly int[] Longest;
        internal int StuckCount => stuck.Count(v => v);
        private readonly bool[] now;
        internal int CurrentStuckCount => now.Count(v => v);
        internal int ActiveCount => active.Count(v => v);
        internal M7StuckMeter(M6World w)
        {
            world = w; units = w.Recipe.Units.ToArray(); int n = units.Length; guardSlots = new GuardSlots(w.Recipe.Match.Points);
            history = new SimVec3[n, 301]; target = new SimVec3[n]; continuous = new int[n];
            lastProgress = new int[n]; best = new SimWide[n]; active = new bool[n]; stuck = new bool[n]; now = new bool[n]; Longest = new int[n];
        }
        internal void Sample()
        {
            int tick = (int)world.Sim.Clock.Tick;
            for (int i = 0; i < units.Length; i++)
            {
                ulong key = units[i].Key;
                bool valid = world.Has<Position>(key) && !world.Dead(key) && world.Has<MoveIntent>(key) && world.Read<MoveIntent>(key).Active;
                now[i] = false;
                if (!valid) { active[i] = false; continuous[i] = 0; continue; }
                var p = world.Read<Position>(key).Value; var goal = world.Read<MoveIntent>(key).Target;
                // The progress metric retains its original point-center definition.
                // Only the command target changed to a guard position; stuck windows still compare that exact target.
                var center = goal;
                for (int pi = 0; pi < world.Recipe.Match.Points.Length; pi++)
                    if (guardSlots.TryFind(goal, pi, out _)) { center = world.Recipe.Match.Points[pi].Center; break; }
                var distance = CombatSystem.Horizontal(p - center).LengthSquared;
                if (!active[i] || !target[i].Equals(goal))
                { best[i] = distance; lastProgress[i] = tick; continuous[i] = 0; }
                else if (distance < best[i]) { best[i] = distance; lastProgress[i] = tick; }
                Longest[i] = Math.Max(Longest[i], tick - lastProgress[i]); active[i] = true; target[i] = goal;
                bool enemy = false;
                for (int j = 0; j < units.Length && !enemy; j++)
                    if (units[j].Team != 0 && units[j].Team != units[i].Team && world.Has<Position>(units[j].Key) && !world.Dead(units[j].Key))
                        enemy = CombatSystem.Horizontal(p - world.Read<Position>(units[j].Key).Value).LengthSquared <=
                            new SimVec3(world.Recipe.Match.Sight, default, default).LengthSquared;
                if (enemy) continuous[i] = 0; else continuous[i]++;
                history[i, tick % 301] = p;
                if (continuous[i] >= 301 && CombatSystem.Horizontal(p - history[i, (tick - 300) % 301]).LengthSquared <
                    new SimVec3(SimFix.FromInt(1), default, default).LengthSquared)
                {
                    if (!stuck[i] && world.Recipe.Seed == 41)
                    {
                        var start = history[i, (tick - 300) % 301];
                        TestContext.Out.WriteLine($"STUCK seed=41 key={key} window={tick - 300}..{tick} fromRaw={start.X.Raw},{start.Z.Raw} toRaw={p.X.Raw},{p.Z.Raw} goalRaw={goal.X.Raw},{goal.Z.Raw} displacement={(p - start).Length.Raw / 65536.0:F6}m continuous={continuous[i]}");
                    }
                    stuck[i] = true; now[i] = true;
                }
            }
        }
    }
}
