using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M7DemoTests
    {
        private static WorldRecipe Recipe(ulong seed = 41)
        {
            var d = DemoRecipes.M7();
            return new WorldRecipe(seed, d.Mover, d.Units.ToArray(), d.Script.ToArray()).WithCombat(d.Combat).WithMatch(d.Match).WithCrowdPass(d.CrowdPass);
        }
        private static M6World World(ulong seed = 41, bool reverse = false) => new M6World(Recipe(seed), DemoRecipes.M7MapBytes(), reverse);

        [TestCase(41UL)]
        [TestCase(42UL)]
        [TestCase(43UL)]
        [TestCase(44UL)]
        [TestCase(45UL)]
        public void FiveSeedsOutcomeCapacityReinforcementAndStuck(ulong seed)
        {
            var w = World(seed); var meter = new M7StuckMeter(w); meter.Sample();
            var index = w.Sim.GetType().GetField("spatialIndex", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.Sim);
            var members = index.GetType().GetProperty("MembershipCount");
            int peak = (int)members.GetValue(index), deployed = 0, pointSpawns = 0;
            ulong hash600 = 0;
            while (w.Match.Winner == 0 && w.Sim.Clock.Tick < 9000)
            {
                // Reinforce runs before capture; use ownership from the preceding tick.
                var owners = w.Capture.Points.ToArray();
                w.Step(); meter.Sample(); peak = Math.Max(peak, (int)members.GetValue(index));
                foreach (var record in w.Reinforce.Deployed)
                {
                    deployed++;
                    bool allowed = false, fromPoint = false;
                    foreach (var anchor in w.Recipe.Match.Bases)
                        if (anchor.Team == record.Team && OnSpawnGrid(record.Point, anchor.Point, w.Recipe.Match)) allowed = true;
                    for (int p = 0; p < owners.Length; p++)
                        if (owners[p].Owner == record.Team && w.Recipe.Match.Points[p].HasSpawn &&
                            OnSpawnGrid(record.Point, w.Recipe.Match.Points[p].Spawn, w.Recipe.Match)) allowed = fromPoint = true;
                    Assert.That(allowed, Is.True, "Deployment must use an owned anchor's candidate grid.");
                    Assert.That(w.Recipe.Units.ToArray().Any(u => u.Reserve && StableEntityKey.Placed(1, u.Key).Id.Equals(record.Unit)), Is.True);
                    if (fromPoint) pointSpawns++;
                }
                if (w.Sim.Clock.Tick == 600) hash600 = w.Hash;
            }
            int entities = 0; foreach (var entity in w.Sim.Entities) entities++;
            TestContext.Out.WriteLine($"M7_DEMO seed={seed} end={w.Match.EndTick} winner={w.Match.Winner} reason={w.Match.EndReason} hash600={hash600:X16} endHash={w.Hash:X16} stuck={meter.StuckCount} longest={meter.Longest.Max()} N={w.Recipe.Units.Length} walls={M7DemoMap.Build().Walls.Count} entities={entities} capacity={w.Sim.Entities.Capacity} peakMemberships={peak} deployed={deployed} pointSpawns={pointSpawns}");
            Assert.That(w.Match.Winner, Is.InRange(1, 2)); Assert.That(w.Match.EndTick, Is.InRange(1, 9000));
            Assert.That(w.Recipe.Units.Length, Is.EqualTo(300)); Assert.That(entities, Is.EqualTo(306));
            Assert.That(w.Sim.Entities.Capacity, Is.EqualTo(306));
            Assert.That(deployed, Is.GreaterThan(0)); Assert.That(pointSpawns, Is.GreaterThan(0));
            Assert.That(meter.StuckCount, Is.Zero, "Report nonzero; do not tune the recipe.");
        }

        private static bool OnSpawnGrid(SimVec3 at, SimVec3 anchor, MatchPack pack)
        {
            for (int z = 0; z < pack.GridRows; z++)
                for (int x = 0; x < pack.GridColumns; x++)
                    if (at.Equals(anchor + new SimVec3(SimFix.Ratio(2 * x - pack.GridColumns + 1, 2) * pack.GridSpacing,
                        default, SimFix.Ratio(2 * z - pack.GridRows + 1, 2) * pack.GridSpacing))) return true;
            return false;
        }

        [Test]
        public void Seed41RepeatReverseAndRecordedConstants()
        {
            var a = World(); var b = World(); var reverse = World(reverse: true);
            Assert.That(a.Unit(1), Is.Not.EqualTo(reverse.Unit(1)));
            Assert.That(a.Hash, Is.EqualTo(b.Hash)); Assert.That(a.Hash, Is.EqualTo(reverse.Hash));
            ulong hash600 = 0;
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < 9000)
            {
                a.Step(); b.Step(); reverse.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "repeat tick=" + a.Sim.Clock.Tick);
                Assert.That(reverse.Hash, Is.EqualTo(a.Hash), "reverse tick=" + a.Sim.Clock.Tick);
                if (a.Sim.Clock.Tick == 600) hash600 = a.Hash;
            }
            TestContext.Out.WriteLine($"M7_LOCK tick600={hash600:X16} end={a.Match.EndTick} winner={a.Match.Winner} reason={a.Match.EndReason} endHash={a.Hash:X16}");
            // Re-recorded by WP-21 (H1); the earlier values were recorded on fa5c23c + phase 1 + M7 recipe.
            // export PATH="/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/Scripting/DotNetSdk:$PATH"
            // dotnet test tools/headless/WarCore.Headless.Tests.csproj -c Release --disable-build-servers
            //   --filter 'FullyQualifiedName~M7DemoTests.Seed41' --logger 'console;verbosity=normal'
            Assert.That(hash600, Is.EqualTo(0x8D7460197CEA0D74UL));
            Assert.That(a.Match.EndTick, Is.EqualTo(2030)); Assert.That(a.Match.Winner, Is.EqualTo(2));
            Assert.That(a.Match.EndReason, Is.EqualTo(MatchEndReason.AllPoints));
            Assert.That(a.Hash, Is.EqualTo(0xD7C576E0318B4FE5UL));
        }

        [Test]
        public void FullSnapshotDuringDeathsReinforcementAndProgressContinuesInReverseWorld()
        {
            var a = World(); bool ready = false;
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < 9000)
            {
                a.Step();
                ready = a.Recipe.Units.ToArray().Any(u => a.Dead(u.Key)) &&
                    a.Recipe.Units.ToArray().Any(u => u.Reserve && a.Has<Position>(u.Key)) &&
                    a.Capture.Points.ToArray().Any(p => p.Progress > 0);
                if (ready) break;
            }
            Assert.That(ready, Is.True, "Natural demo must reach all three snapshot preconditions.");
            long savedTick = a.Sim.Clock.Tick;
            Assert.That(a.Match.Winner, Is.Zero);
            var snapshot = a.Sim.SaveRetainedSnapshot(); var b = World(reverse: true);
            b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true);
            Assert.That(b.Unit(1), Is.Not.EqualTo(a.Unit(1)));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            Assert.That(b.Capture.Changes.Length, Is.Zero); Assert.That(b.Reinforce.Deployed.Length, Is.Zero);
            int steps = 0;
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < 9000)
            {
                a.Step(); b.Step(); steps++;
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "restored tick=" + a.Sim.Clock.Tick);
                Assert.That(b.Reinforce.Deployed.ToArray(), Is.EqualTo(a.Reinforce.Deployed.ToArray()));
                Assert.That(b.Capture.Changes.ToArray(), Is.EqualTo(a.Capture.Changes.ToArray()));
            }
            Assert.That(steps, Is.GreaterThan(0));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
            TestContext.Out.WriteLine($"M7_SNAPSHOT saved={savedTick} continued={steps} end={a.Match.EndTick} hash={a.Hash:X16}");
        }

        [Test]
        public void TimingProducesSamplesWithoutChangingHashesOrAddingSteadyAllocations()
        {
            var a = SimSession.Create(Recipe(), DemoRecipes.M7MapBytes());
            var b = SimSession.Create(Recipe(), DemoRecipes.M7MapBytes());
            Assert.That(a.StepTimingEnabled, Is.False); Assert.That(a.StepTiming.Count, Is.Zero);
            b.EnableStepTiming();
            for (int i = 0; i < 100; i++) { a.StepOnce(); b.StepOnce(); }
            long bytesA = 0, bytesB = 0;
            for (int i = 0; i < 600; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(); a.StepOnce(); bytesA += GC.GetAllocatedBytesForCurrentThread() - before;
                before = GC.GetAllocatedBytesForCurrentThread(); b.StepOnce(); bytesB += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            }
            var stats = b.StepTiming;
            Assert.That(stats.Count, Is.EqualTo(700)); Assert.That(stats.MeanMs, Is.GreaterThan(0));
            Assert.That(stats.P95Ms, Is.GreaterThan(0)); Assert.That(stats.MaxMs, Is.GreaterThanOrEqualTo(stats.P95Ms));
            Assert.That(bytesB, Is.EqualTo(bytesA), "Timing adds no allocation to existing view capture.");
            TestContext.Out.WriteLine($"M7_TIMING CoreCLR count={stats.Count} mean={stats.MeanMs:F6} p95={stats.P95Ms:F6} max={stats.MaxMs:F6} allocationDelta={bytesB - bytesA}");
            b.DisableStepTiming(); a.StepOnce(); b.StepOnce();
            Assert.That(b.StepTiming.Count, Is.EqualTo(700)); Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            b.EnableStepTiming(2);
            for (int i = 0; i < 3; i++) { a.StepOnce(); b.StepOnce(); }
            Assert.That(b.StepTiming.Count, Is.EqualTo(2)); Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.EnableStepTiming(0));
        }

        [Test]
        public void MapRecipeAndCheckedInBytesAgree()
        {
            var map = M7DemoMap.Build(); var recipe = Recipe();
            Assert.That(map.Width, Is.EqualTo(160)); Assert.That(map.Depth, Is.EqualTo(100));
            var grid = WcMapNavigation.Create(map);
            Assert.That(grid.Width, Is.LessThanOrEqualTo(256)); Assert.That(grid.Depth, Is.LessThanOrEqualTo(256));
            var session = SimSession.Create(recipe, DemoRecipes.M7MapBytes());
            for (int z = 0; z < map.Depth; z++) for (int x = 0; x < map.Width; x++)
                Assert.That(session.BlockedCell(x, z), Is.EqualTo(!map.IsPassable(x, z)), $"cell {x},{z}");
            // Every cover is at least 4 m from every other cover and from the map edge.
            foreach (var wall in map.Walls)
            {
                Assert.That(wall.LocalCenter.X - wall.HalfX, Is.GreaterThanOrEqualTo(SimFix.FromInt(4)));
                Assert.That(wall.LocalCenter.Z - wall.HalfZ, Is.GreaterThanOrEqualTo(SimFix.FromInt(4)));
                Assert.That(map.Max.X - wall.LocalCenter.X - wall.HalfX, Is.GreaterThanOrEqualTo(SimFix.FromInt(4)));
                Assert.That(map.Max.Z - wall.LocalCenter.Z - wall.HalfZ, Is.GreaterThanOrEqualTo(SimFix.FromInt(4)));
                foreach (var other in map.Walls)
                    if (wall.Placement != other.Placement)
                    {
                        long dx = Math.Abs(wall.LocalCenter.X.Raw - other.LocalCenter.X.Raw) - wall.HalfX.Raw - other.HalfX.Raw;
                        long dz = Math.Abs(wall.LocalCenter.Z.Raw - other.LocalCenter.Z.Raw) - wall.HalfZ.Raw - other.HalfZ.Raw;
                        Assert.That(Math.Max(dx, dz), Is.GreaterThanOrEqualTo(4 * SimFix.Scale));
                    }
            }
            var units = recipe.Units.ToArray();
            for (int team = 1; team <= 2; team++)
            {
                Assert.That(units.Count(u => u.Team == team && !u.Reserve), Is.EqualTo(100));
                Assert.That(units.Count(u => u.Team == team && u.Reserve), Is.EqualTo(50));
                Assert.That(units.Where(u => u.Team == team).All(u => u.Weapon == team - 1 && u.SpeedMmPerSec == 6000), Is.True);
            }
            var initial = units.Where(u => !u.Reserve).ToArray();
            for (int i = 0; i < initial.Length; i++) for (int j = i + 1; j < initial.Length; j++)
            {
                long dx = initial[i].XMm - initial[j].XMm, dz = initial[i].ZMm - initial[j].ZMm;
                Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(500L * 500));
                if (initial[i].Team != initial[j].Team) Assert.That(dx * dx + dz * dz, Is.GreaterThan(35000L * 35000));
            }
            Assert.That(recipe.Match.Points.Length, Is.EqualTo(5));
            for (int i = 0; i < 5; i++)
            {
                var p = recipe.Match.Points[i];
                Assert.That(p.Id, Is.EqualTo(i + 1)); Assert.That(p.InitialOwner, Is.Zero);
                Assert.That(p.HasSpawn, Is.True); Assert.That(p.Radius, Is.EqualTo(SimFix.FromInt(6)));
            }
            Assert.That(File.ReadAllBytes(Path.Combine(ArchitectureTests.AssetRoot(), "StreamingAssets/maps/M7Demo.wcmap")), Is.EqualTo(DemoRecipes.M7MapBytes()));
        }
    }
}
