using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M6PerformanceTests
    {
        internal static M6World CreateRealLoad(bool endless = false)
        {
            var units = new UnitSpawn[200];
            for (int i = 0; i < units.Length; i++)
            {
                int slot = i % 100, team = i < 100 ? 1 : 2;
                // Mirrored 10x10 formations west/east, well outside sight and capture range.
                long x = 1000 + slot % 10 * 750;
                if (team == 2) x = 80000 - x;
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, 80250 + slot / 10 * 1500,
                    6000, team, team - 1);
            }
            var recipe = M6World.RecipeOf(units, RwrMatch.CreateM6(), RwrPack.CreateM6(), seed: 41);
            return new M6World(endless ? M6World.Endless(recipe) : recipe, WcMapWriter.Write(M3TestWorld.Boxes(80, 112)));
        }

        [Test]
        public void Real100Vs100ThreePointsReportsStepCostAndPerTickWork()
        {
            const int warmup = 100, ticks = 600;
            var measured = CreateRealLoad(); measured.Step(warmup);
            var times = new double[ticks];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < ticks; i++)
            {
                long start = Stopwatch.GetTimestamp(); measured.Sim.Step(default);
                times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            double mean = times.Average(); Array.Sort(times);
            TestContext.Out.WriteLine($"REAL_PERF seed=41 n=200 warmup={warmup} ticks={ticks} mean={mean:F6} p95={times[(int)Math.Ceiling(ticks * 0.95) - 1]:F6} max={times[ticks - 1]:F6} gc={allocated}");

            // All observation and assertions run in a separate, untimed replay.
            var replay = CreateRealLoad(); var orders = M6AiTests.Observe(replay); replay.Step(warmup);
            var last = new SimVec3[200];
            for (int i = 0; i < last.Length; i++) last[i] = replay.Read<Position>((ulong)i + 1).Value;
            var csv = new string[ticks + 1];
            csv[0] = "tick,aliveOne,aliveTwo,moving,orders,fires,changes,approach,engagement,capture,contested,afterEnd";
            int approachTicks = 0, engagementTicks = 0, captureTicks = 0, contestedTicks = 0;
            int movingTicks = 0, maxMoving = 0, totalOrders = 0, totalFires = 0, totalChanges = 0;
            int aliveOne = 0, aliveTwo = 0, afterEndTicks = 0;
            for (int i = 0; i < ticks; i++)
            {
                int beforeOrders = orders.Orders;
                replay.Step();
                aliveOne = aliveTwo = 0; int moving = 0;
                var pointTeams = new int[3];
                for (int j = 0; j < last.Length; j++)
                {
                    ulong key = (ulong)j + 1; var position = replay.Read<Position>(key).Value;
                    if (!replay.Dead(key))
                    {
                        if (j < 100) aliveOne++; else aliveTwo++;
                        if (replay.Has<MoveIntent>(key) && replay.Read<MoveIntent>(key).Active && !position.Equals(last[j])) moving++;
                        for (int p = 0; p < pointTeams.Length; p++)
                            if (CaptureSystem.Inside(position, replay.Recipe.Match.Points[p])) pointTeams[p] |= j < 100 ? 1 : 2;
                    }
                    last[j] = position;
                }
                int calls = orders.Orders - beforeOrders, fires = replay.Combat.Fires.Length, changes = replay.Capture.Changes.Length;
                bool approach = moving > 0 && fires == 0, engagement = fires > 0;
                bool capture = changes > 0;
                foreach (var point in replay.Capture.Points) capture |= point.Progress > 0;
                bool contested = Array.Exists(pointTeams, teams => teams == 3);
                bool afterEnd = replay.Match.Winner != 0 && replay.Sim.Clock.Tick > replay.Match.EndTick;
                if (afterEnd) afterEndTicks++;
                if (approach) approachTicks++; if (engagement) engagementTicks++;
                if (capture) captureTicks++; if (contested) contestedTicks++;
                if (moving > 0) movingTicks++; maxMoving = Math.Max(maxMoving, moving);
                totalOrders += calls; totalFires += fires; totalChanges += changes;
                csv[i + 1] = $"{warmup + i + 1},{aliveOne},{aliveTwo},{moving},{calls},{fires},{changes},{(approach ? 1 : 0)},{(engagement ? 1 : 0)},{(capture ? 1 : 0)},{(contested ? 1 : 0)},{(afterEnd ? 1 : 0)}";
            }
            string trace = Path.Combine(Path.GetTempPath(), "m6-real-load-" + Process.GetCurrentProcess().Id + ".csv");
            File.WriteAllLines(trace, csv);
            TestContext.Out.WriteLine($"REAL_WORK ticks={ticks} approach={approachTicks} engagement={engagementTicks} capture={captureTicks} contested={contestedTicks} movingTicks={movingTicks} maxMoving={maxMoving} orders={totalOrders} fires={totalFires} changes={totalChanges} aliveOne={aliveOne} aliveTwo={aliveTwo} endTick={replay.Match.EndTick} afterEnd={afterEndTicks} trace={trace}");
            Assert.That(replay.Hash, Is.EqualTo(measured.Hash));
            Assert.That(approachTicks, Is.GreaterThan(0));
            Assert.That(engagementTicks, Is.GreaterThan(0));
            Assert.That(captureTicks, Is.GreaterThan(0));
            Assert.That(totalOrders, Is.GreaterThan(0));
            Assert.That(totalChanges, Is.GreaterThan(0));
        }

        private static readonly ControlPointDef Destination = new ControlPointDef(2, M6World.P(240, 160), SimFix.FromInt(6));
        private static M6World Create(int count, bool battle)
        {
            var units = new UnitSpawn[count];
            for (int i = 0; i < count; i++)
                units[i] = i < count - 2 ? new UnitSpawn((ulong)i + 1, 0, i + 1, 5000 + i % 10 * 2000, 2000 + i / 10 * 2000, 6000, i % 2 + 1, 0) :
                    new UnitSpawn((ulong)i + 1, 0, i + 1, 100000, 120000 + i % 2 * 80000, 3000, i % 2 + 1, -1);
            var points = new[] { new ControlPointDef(1, M6World.P(5, 2), SimFix.Ratio(2, 5)), Destination };
            var guns = RwrPack.Create();
            var combat = new CombatPack(guns.Weapons.ToArray(), new ZeroResolver(), battle ? (ITargetSelector)new SightLimited(new NearestVisibleEnemy(), SimFix.FromInt(35)) : new NearestVisibleEnemy(), guns.MaxHealthRaw);
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(combat);
            if (battle) recipe = recipe.WithMatch(M6World.Pack(points, ticks: 10000, think: 30));
            var w = new M6World(recipe, WcMapWriter.Write(M3TestWorld.Boxes(256, 256)));
            if (!battle)
                for (int i = count - 2; i < count; i++) w.System<InterimLocomotion>().Order(w.Unit((ulong)i + 1), GuardSlots.Build(Destination.Center, Destination.Radius)[i - (count - 2) + 1]);
            return w;
        }
        [TestCase(20)]
        [TestCase(200)]
        public void MovingCombatAi600TicksZeroGcAndSameLayoutM5Control(int count)
        {
            var m6 = Create(count, true); var m5 = Create(count, false);
            m6.Step(100); m5.Step(100);
            var a = new double[600]; var b = new double[600];
            long Measure(M6World w, double[] times)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < times.Length; i++)
                {
                    long start = Stopwatch.GetTimestamp(); w.Sim.Step(default);
                    times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            long gc6 = Measure(m6, a), gc5 = Measure(m5, b);
            double mean6 = a.Average(), mean5 = b.Average(); Array.Sort(a); Array.Sort(b);
            TestContext.Out.WriteLine($"PERF n={count} M6 mean={mean6:F6} p95={a[569]:F6} max={a[599]:F6} gc={gc6} M5 mean={mean5:F6} p95={b[569]:F6} max={b[599]:F6} gc={gc5} ratio={mean6 / mean5:F3}");
            Assert.That(gc6, Is.Zero); Assert.That(gc5, Is.Zero);
            // Untimed replay proves work throughout the measured window, without measurement assertions or bridge copies.
            foreach (bool battle in new[] { true, false })
            {
                var check = Create(count, battle); check.Step(100);
                var positions = new SimVec3[count]; for (int i = count - 2; i < count; i++) positions[i] = check.Read<Position>((ulong)i + 1).Value;
                int shots = 0;
                for (int tick = 0; tick < 600; tick++)
                {
                    check.Step(); shots += check.Combat.Fires.Length;
                    for (int i = count - 2; i < count; i++)
                    {
                        ulong key = (ulong)i + 1; var next = check.Read<Position>(key).Value;
                        Assert.That(check.Read<MoveIntent>(key).Active, Is.True);
                        Assert.That(next, Is.Not.EqualTo(positions[i]), $"battle={battle} tick={tick} key={key}"); positions[i] = next;
                    }
                    if (battle) Assert.That(check.Capture.Points[0].Progress, Is.EqualTo(tick + 101));
                }
                Assert.That(shots, Is.GreaterThan(count * 10)); Assert.That(check.Hash, Is.EqualTo(battle ? m6.Hash : m5.Hash));
                TestContext.Out.WriteLine($"WORK n={count} battle={battle} shots={shots} moving=2 ticks=600");
            }
        }
        [Test]
        public void ReinforcementGrowthAllocationReportedSeparately()
        {
            var units = Enumerable.Range(1, 80).Select(i => M6World.U((ulong)i, i <= 40 ? 1 : 2, i <= 40 ? 6 : 74, reserve: i % 40 != 1, speed: 6000)).ToArray();
            var w = new M6World(M6World.RecipeOf(units, M6World.Pack(interval: 120, ticks: 10000, think: 1)));
            w.Step(100);
            var bytes = new long[4800]; var deployed = new int[4800];
            for (int i = 0; i < bytes.Length; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(); w.Sim.Step(default);
                bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before; deployed[i] = w.Reinforce.Deployed.Length;
            }
            long spawnBytes = 0, otherBytes = 0; int count = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (deployed[i] != 0) { count += deployed[i]; spawnBytes += bytes[i]; }
                else otherBytes += bytes[i];
                if (bytes[i] != 0) TestContext.Out.WriteLine($"GROWTH tick={101 + i} deployed={deployed[i]} bytes={bytes[i]}");
            }
            TestContext.Out.WriteLine($"GROWTH_SUM deployed={count} deploymentTickBytes={spawnBytes} otherTickBytes={otherBytes}");
            Assert.That(count, Is.EqualTo(78)); Assert.That(otherBytes, Is.Zero);
        }
    }
}
