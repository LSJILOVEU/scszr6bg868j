using System;
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
    public sealed class M6SnapshotTests
    {
        [Test]
        public void FullSnapshotReverseAllocation_DeathDeployedReserveProgressAndEventContinuation()
        {
            var points = new[] { new ControlPointDef(1, M6World.P(20), SimFix.FromInt(6)), new ControlPointDef(2, M6World.P(60), SimFix.FromInt(6)) };
            var units = new[] { M6World.U(1, 1), M6World.U(2, 2, 60), M6World.U(3, 1, 5),
                M6World.U(4, 1, reserve: true, speed: 6000), M6World.U(5, 1, reserve: true, speed: 6000),
                M6World.U(6, 2, reserve: true, speed: 6000), M6World.U(7, 2, reserve: true, speed: 6000) };
            var recipe = M6World.Endless(M6World.RecipeOf(units, M6World.Pack(points, ticks: 10, interval: 5, think: 1)));
            M6World Create()
            {
                var w = new M6World(recipe);
                w.Sim.RegisterGameplay("test.death", new M6Damage(w, 2, 3), SimSystemPhase.Queries); return w;
            }
            var a = Create(); var b = Create(); a.Step(6);
            Assert.That(a.Dead(3), Is.True); Assert.That(a.Reinforce.Successes(1), Is.EqualTo(1));
            Assert.That(a.Capture.Points[0].Progress, Is.EqualTo(6));
            var deployed = a.Reinforce.Successes(1) + a.Reinforce.Successes(2);
            var snapshot = a.Sim.SaveRetainedSnapshot(); b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true);
            Assert.That(b.Unit(1), Is.Not.EqualTo(a.Unit(1)), "Physical allocation must actually change.");
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot));
            Assert.That(b.Capture.Changes.Length, Is.Zero); Assert.That(b.Reinforce.Deployed.Length, Is.Zero);
            int captures = 0, spawns = 0, orders = 0, stops = 0;
            var calls = M6AiTests.Observe(b);
            for (int tick = 7; tick <= 600; tick++)
            {
                a.Step(); b.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick=" + tick);
                Assert.That(b.Reinforce.Deployed.ToArray(), Is.EqualTo(a.Reinforce.Deployed.ToArray()), "deploy tick=" + tick);
                Assert.That(b.Capture.Changes.ToArray(), Is.EqualTo(a.Capture.Changes.ToArray()), "capture tick=" + tick);
                captures += a.Capture.Changes.Length; spawns += a.Reinforce.Deployed.Length;
                if (a.Capture.Changes.Length != 0)
                {
                    // Restore on an event tick too: transient events must not replay.
                    var c = Create(); c.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot(), reverseAllocation: true);
                    Assert.That(c.Capture.Changes.Length, Is.Zero); Assert.That(c.Reinforce.Deployed.Length, Is.Zero);
                    Assert.That(c.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
                    c.Step(); var d = Create(); d.Sim.RestoreRetainedSnapshot(b.Sim.SaveRetainedSnapshot()); d.Step();
                    Assert.That(c.Hash, Is.EqualTo(d.Hash));
                    Assert.That(c.Capture.Changes.ToArray(), Is.EqualTo(d.Capture.Changes.ToArray()));
                    Assert.That(c.Reinforce.Deployed.ToArray(), Is.EqualTo(d.Reinforce.Deployed.ToArray()));
                }
            }
            orders = calls.Orders; stops = calls.Stops;
            Assert.That(captures, Is.GreaterThanOrEqualTo(2)); Assert.That(spawns, Is.EqualTo(2));
            Assert.That(a.Reinforce.Successes(1) + a.Reinforce.Successes(2), Is.EqualTo(deployed + 2));
            Assert.That(orders, Is.GreaterThan(0)); Assert.That(stops, Is.GreaterThan(0));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
            TestContext.Out.WriteLine($"M6 restore saved=6 continued=594 captures={captures} deployments={spawns} orders={orders} stops={stops}");
        }

        private static readonly string[] Fields = { "pointId", "centerX", "centerY", "centerZ", "radius", "owner", "hasSpawn", "spawnX", "spawnY", "spawnZ", "pointOrder", "pointCount",
            "baseTeam", "baseX", "baseY", "baseZ", "baseOrder", "baseCount", "num", "den", "strict", "captureTicks", "interrupt", "decay", "interval", "think", "max", "sight", "columns", "rows", "spacing", "speed", "squad", "map", "selectorSight", "innerSelector" };
        private sealed class ChangedSelector : ITargetSelector
        {
            public ulong PolicyKey => 87654321;
            public bool TrySelect(CombatSystem w, EntityId shooter, SimFix range, out EntityId target) => new NearestVisibleEnemy().TrySelect(w, shooter, range, out target);
        }
        [TestCaseSource(nameof(Fields))]
        public void ConfigurationEachFieldRejectsSnapshot(string field)
        {
            WorldRecipe Recipe(bool change)
            {
                bool Is(string name) => change && field == name;
                var center = M6World.P(20) + new SimVec3(SimFix.FromInt(Is("centerX") ? 1 : 0), SimFix.FromInt(Is("centerY") ? 1 : 0), SimFix.FromInt(Is("centerZ") ? 1 : 0));
                var spawn = M6World.P(21) + new SimVec3(SimFix.FromInt(Is("spawnX") ? 1 : 0), SimFix.FromInt(Is("spawnY") ? 1 : 0), SimFix.FromInt(Is("spawnZ") ? 1 : 0));
                var points = new[] { new ControlPointDef(Is("pointId") ? 9 : 1, center, SimFix.FromInt(Is("radius") ? 7 : 6), Is("owner") ? 1 : 0, !Is("hasSpawn"), spawn),
                    new ControlPointDef(2, M6World.P(60), SimFix.FromInt(6)) };
                if (Is("pointOrder")) Array.Reverse(points); if (Is("pointCount")) points = points.Take(1).ToArray();
                var bases = new[] { new SpawnAnchor(Is("baseTeam") ? 2 : 1, M6World.P(6) + new SimVec3(SimFix.FromInt(Is("baseX") ? 1 : 0), SimFix.FromInt(Is("baseY") ? 1 : 0), SimFix.FromInt(Is("baseZ") ? 1 : 0))), new SpawnAnchor(2, M6World.P(74)) };
                if (Is("baseOrder")) Array.Reverse(bases); if (Is("baseCount")) bases = bases.Take(1).ToArray();
                var pack = M6World.Pack(points, num: Is("num") ? 3 : 2, den: Is("den") ? 2 : 1, strict: Is("strict"), ticks: Is("captureTicks") ? 5 : 3,
                    mode: Is("interrupt") ? InterruptMode.Pause : InterruptMode.Decay, decay: Is("decay") ? 2 : 1, interval: Is("interval") ? 7 : 5,
                    think: Is("think") ? 2 : 1, max: Is("max") ? 901 : 900, bases: bases, columns: Is("columns") ? 5 : 4, rows: Is("rows") ? 5 : 4,
                    spacing: SimFix.Ratio(Is("spacing") ? 4 : 3, 2), sight: SimFix.FromInt(Is("sight") ? 36 : 35));
                var guns = M6World.Guns(new ZeroResolver(), new SightLimited(Is("innerSelector") ? (ITargetSelector)new ChangedSelector() : new NearestVisibleEnemy(), SimFix.FromInt(Is("selectorSight") ? 36 : 35)));
                return M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 2, 60), new UnitSpawn(3, 0, Is("squad") ? 7 : 1, 6000, 20000, Is("speed") ? 5999 : 6000, 1, -1, true) }, pack, guns);
            }
            var a = new M6World(Recipe(false), WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (40, 1, 1, 1))));
            var b = new M6World(Recipe(true), WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (field == "map" ? 41 : 40, 1, 1, 1))));
            Assert.That(b.Hash, Is.Not.EqualTo(a.Hash), field);
            a.Step(); var saved = a.Sim.SaveRetainedSnapshot();
            var error = Assert.Catch<InvalidOperationException>(() => b.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: true));
            TestContext.Out.WriteLine($"M6 reject {field}: {error.Message}");
        }
    }
}
