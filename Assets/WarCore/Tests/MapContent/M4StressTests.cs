using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M4StressTests
    {
        // A fixed PRNG avoids System.Random implementation changes across Unity and CoreCLR.
        private sealed class Random32
        {
            private uint state;
            internal Random32(uint seed) { state = seed; }
            internal int Next(int min, int max)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                return min + (int)(state % (uint)(max - min));
            }
        }
        private readonly struct Footprint
        {
            internal readonly int X, Z, Count, Spacing, HalfX, HalfZ;
            internal Footprint(int x, int z, int count, int spacing)
            {
                X = x; Z = z; Count = count; Spacing = spacing;
                int cols = (int)Math.Ceiling(Math.Sqrt(count)), rows = (count + cols - 1) / cols;
                HalfX = (cols - 1) * spacing / 2; HalfZ = (rows - 1) * spacing / 2;
            }
            internal SimVec3 Point(int rank)
            {
                int cols = (int)Math.Ceiling(Math.Sqrt(Count));
                return M3TestWorld.Point(X - HalfX + rank % cols * Spacing, Z - HalfZ + rank / cols * Spacing);
            }
            internal bool Separated(Footprint b, int gap)
            {
                long dx = Math.Max(0, Math.Abs(X - b.X) - HalfX - b.HalfX);
                long dz = Math.Max(0, Math.Abs(Z - b.Z) - HalfZ - b.HalfZ);
                return dx * dx + dz * dz >= (long)gap * gap;
            }
            internal bool Fits(WcMap map)
            {
                if (X - HalfX < 1000 || Z - HalfZ < 1000 || X + HalfX > map.Width * 1000 - 1000 || Z + HalfZ > map.Depth * 1000 - 1000) return false;
                // Check the full rectangle, including missing final-row slots, and capsule clearance.
                for (int z = (Z - HalfZ - 250) / 1000; z <= (Z + HalfZ + 250) / 1000; z++)
                    for (int x = (X - HalfX - 250) / 1000; x <= (X + HalfX + 250) / 1000; x++)
                        if (!map.IsPassable(x, z)) return false;
                return true;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AB_200FixedScenarios_ZeroOverlap_ReportArrival(bool demo)
        {
            WcMap map = demo ? M2DemoMap.Build() : M3TestWorld.Boxes(64, 32);
            var report = new StringBuilder();
            string name = demo ? "B" : "A";
            report.AppendLine($"# {name} incomplete scenarios (deadline tick 600)");
            report.AppendLine("Coordinates are raw (65536 raw = 1 m). Progress means a new minimum squared distance to the target, not oscillation. No cause is inferred.");
            int total = 0, arrived = 0, complete = 0, incomplete = 0;
            long pairChecks = 0;
            double minimum = double.PositiveInfinity;
            for (int scenario = 0; scenario < 200; scenario++)
            {
                uint seed = (uint)((demo ? 820000 : 810000) + scenario);
                var rng = new Random32(seed);
                int squads = rng.Next(1, 4);
                var counts = new int[squads]; var births = new Footprint[squads]; var goals = new Footprint[squads];
                for (int s = 0; s < squads; s++) counts[s] = rng.Next(1, 11);
                Footprint Draw(int n, int spacing, List<Footprint> avoid)
                {
                    for (int attempt = 0; attempt < 100000; attempt++)
                    {
                        var f = new Footprint(rng.Next(1, 63) * 1000 + 500, rng.Next(1, 31) * 1000 + 500, n, spacing);
                        if (!f.Fits(map)) continue;
                        bool valid = true;
                        foreach (var other in avoid) if (!f.Separated(other, 1000)) { valid = false; break; }
                        if (valid) return f;
                    }
                    throw new InvalidOperationException("Cannot construct valid scenario seed=" + seed);
                }
                var occupied = new List<Footprint>();
                for (int s = 0; s < squads; s++) { births[s] = Draw(counts[s], 1000, occupied); occupied.Add(births[s]); }
                for (int s = 0; s < squads; s++) { goals[s] = Draw(counts[s], 2000, occupied); occupied.Add(goals[s]); }
                var units = new List<UnitSpawn>(); var expected = new List<SimVec3>();
                for (int s = 0; s < squads; s++)
                    for (int rank = 0; rank < counts[s]; rank++)
                    {
                        var p = births[s].Point(rank);
                        units.Add(new UnitSpawn((ulong)units.Count + 1, 0, s + 1, p.X.Raw * 1000 / 65536, p.Z.Raw * 1000 / 65536, 6000));
                        expected.Add(goals[s].Point(rank));
                    }
                var world = new M4TestWorld(map, units.ToArray());
                var best = new BigInteger[units.Count]; var last = new int[units.Count];
                for (int i = 0; i < units.Count; i++) best[i] = M4CrowdTests.Squared(world.Point(i), expected[i]);
                for (int tick = 0; tick <= 600; tick++)
                {
                    if (tick > 0) world.Sim.Step(tick <= squads ? M4TestWorld.Command(tick, goals[tick - 1].X, goals[tick - 1].Z) : default);
                    var points = world.Points();
                    try { minimum = Math.Min(minimum, M4CrowdTests.CheckPairs(points, tick)); }
                    catch (AssertionException) { TestContext.Out.WriteLine($"{name} failed seed={seed}"); throw; }
                    pairChecks += units.Count * (units.Count - 1) / 2;
                    for (int i = 0; i < units.Count; i++)
                    {
                        BigInteger d = M4CrowdTests.Squared(points[i], expected[i]);
                        if (d < best[i]) { best[i] = d; last[i] = tick; }
                    }
                }
                int count = 0;
                for (int i = 0; i < units.Count; i++) if (world.Point(i).Equals(expected[i])) count++;
                total += units.Count; arrived += count;
                if (count == units.Count) { complete++; continue; }
                incomplete++;
                report.AppendLine($"\n## seed {seed}: {count}/{units.Count} arrived");
                for (int s = 0; s < squads; s++) report.AppendLine($"- squad {s + 1}: n={counts[s]}, spawn centre=({births[s].X},{births[s].Z}) mm, target centre=({goals[s].X},{goals[s].Z}) mm");
                report.AppendLine("\n| key | target X/Z raw | final X/Z raw | last progress tick | arrived |\n|---:|---|---|---:|---|");
                for (int i = 0; i < units.Count; i++)
                {
                    var p = world.Point(i); var target = expected[i];
                    report.AppendLine($"| {i + 1} | {target.X.Raw},{target.Z.Raw} | {p.X.Raw},{p.Z.Raw} | {last[i]} | {(p.Equals(target) ? "yes" : "no")} |");
                }
            }
            string summary = $"{name}: scenes={complete}/200 units={arrived}/{total} rate={100.0 * arrived / total:F4}% incomplete={incomplete} minimum={minimum:R}m pairChecks={pairChecks} violations=0 deadline=600";
            TestContext.Out.WriteLine(summary);
            report.Insert(0, summary + "\n\n");
            string directory = Environment.GetEnvironmentVariable("WP08_REPORT_DIR");
            if (!string.IsNullOrEmpty(directory)) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + "-nonarrivals.md"), report.ToString()); }
            else TestContext.Out.WriteLine(report.ToString());
        }

        [TestCase(200, false)]
        [TestCase(0, false)]
        [TestCase(200, true)]
        public void C_Overlap_ShrinksNeverBelowMinimum_ResolvedBy100(int gapMm, bool third)
        {
            var units = new List<UnitSpawn> { new UnitSpawn(1, 0, 1, 15000, 15000, 6000), new UnitSpawn(2, 0, 2, 15000 + gapMm, 15000, 6000) };
            if (third) units.Add(new UnitSpawn(3, 0, 3, 15000, 15800, 6000));
            var world = new M4TestWorld(M3TestWorld.Boxes(32, 32), units.ToArray());
            world.Order(0, 5000, 15000); world.Order(1, 25000 + gapMm, 15000);
            var previous = M4CrowdTests.Squared(world.Point(0), world.Point(1));
            int separated = 0, decreases = 0, firstDecrease = 0;
            string firstDrop = "none";
            for (int tick = 1; tick <= 100; tick++)
            {
                world.Sim.Step(default);
                var distance = M4CrowdTests.Squared(world.Point(0), world.Point(1));
                // Below the minimum gap the rule demands a strictly growing gap; at or above it the gap may shrink but not under the minimum.
                BigInteger minimum = (BigInteger)32768 * 32768;
                if (previous < minimum ? distance < previous : distance < minimum)
                {
                    decreases++;
                    if (firstDecrease == 0) { firstDecrease = tick; firstDrop = $"squaredRaw {previous} -> {distance}; positions {world.Point(0).X.Raw},{world.Point(0).Z.Raw} / {world.Point(1).X.Raw},{world.Point(1).Z.Raw}"; }
                }
                if (separated == 0 && distance >= (BigInteger)32768 * 32768) separated = tick;
                if (third)
                    for (int i = 0; i < 2; i++) Assert.That(M4CrowdTests.Squared(world.Point(i), world.Point(2)) >= (BigInteger)32768 * 32768, Is.True);
                previous = distance;
            }
            TestContext.Out.WriteLine($"C gap={gapMm}mm third={third} separatedTick={separated} final={Math.Sqrt((double)previous) / 65536:R}m decreases={decreases} firstDecrease={firstDecrease} {firstDrop}");
            Assert.That(separated, Is.InRange(1, 100));
            Assert.That(decreases, Is.Zero, "Below the minimum gap the pair must never close in, and above it must never go under it; first=" + firstDecrease);
        }

        [Test]
        public void InactiveBlocker_RemainsSolid_WhenMovingUnitApproaches()
        {
            var world = new M4TestWorld(M3TestWorld.Boxes(16, 12), new[] {
                new UnitSpawn(1, 0, 1, 4500, 4500, 6000), new UnitSpawn(2, 0, 2, 8500, 4500, 6000) });
            world.Order(0, 12500, 4500);
            for (int tick = 1; tick <= 100; tick++)
            {
                world.Sim.Step(default);
                M4CrowdTests.CheckPairs(world.Points(), tick);
                Assert.That(world.Point(1), Is.EqualTo(M3TestWorld.Point(8500, 4500)));
            }
        }
    }
}
