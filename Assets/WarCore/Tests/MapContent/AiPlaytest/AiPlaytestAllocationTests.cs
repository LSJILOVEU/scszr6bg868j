#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class AiPlaytestAllocationTests
    {
        private static readonly SimVec3 Start = A2.Cell(10, 10), Visible = A2.Cell(14, 10), Hidden = A2.Cell(30, 10);

        private static M6World World(bool reserve = false)
        {
            var units = reserve ? new[] { Unit(1, 1, Start, 0), Unit(2, 2, Hidden, -1), Unit(3, 1, Start, 0, true) }
                : new[] { Unit(1, 1, Start, 0), Unit(2, 2, Hidden, -1) };
            var match = M6World.Pack(new[] { new ControlPointDef(1, Start, SimFix.FromInt(1), initialOwner: 1) },
                ticks: 100000, think: 10, bases: new[] { new SpawnAnchor(1, A2.Cell(30, 14)), new SpawnAnchor(2, A2.Cell(35, 14)) });
            var recipe = M6World.RecipeOf(units, match, A2.Combat(0)).WithRuleSet(MatchMode.TestNeverEndsId)
                .WithTerrainRules(RwrPack.Load("rwr-ai-playtest")).WithStandingOrders();
            byte[] map = WcMapWriter.Write(Wp23.Make(40, 32, 0, 4 * SimFix.Scale,
                new[] { (20, 0, 1, 25) }, 2 * SimFix.Scale, (x, z) => 0, new[] { "grass" }, (x, z) => 0));
            var w = new M6World(recipe, map);
            w.Sim.Step(A3.Command(SquadCommandKind.Hold, 2));
            w.Step(59);
            Assert.That(A3.Count(w), Is.EqualTo(0), "warm-up has no first contact");
            return w;
        }

        private static UnitSpawn Unit(ulong key, int team, SimVec3 at, int weapon, bool reserve = false) =>
            new UnitSpawn(key, 0, team, at.X.Raw * 1000 / SimFix.Scale, at.Z.Raw * 1000 / SimFix.Scale, 3000, team, weapon, reserve);

        private static long Step(M6World w, InputFrame input = default)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            w.Sim.Step(input);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static void Counter()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            Assert.That(bytes, Is.GreaterThanOrEqualTo(32768));
            AllocationMeter.Stabilize();
        }

        private static long[] ContactWindow(bool report)
        {
            M6World w = World();
            var bytes = new long[110];
            Counter();
            for (int tick = 61; tick <= 170; tick++)
            {
                if (tick == 61) A3b.Place(w, 2, Visible);
                if (tick == 91) A3b.Place(w, 2, Hidden);
                if (tick == 126) A3b.Place(w, 1, Visible);
                if (tick == 151) A3b.Place(w, 2, A2.Cell(16, 10));
                bytes[tick - 61] = Step(w);
                if (tick == 66)
                {
                    Assert.That(A3.Recall(w, 1, tick, out StableId target, out _, out long seen), Is.True);
                    Assert.That(target, Is.EqualTo(A3.Id(w, 2)));
                    Assert.That(seen, Is.EqualTo(66));
                }
                if (tick == 96)
                {
                    Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(2)), Is.False);
                    Assert.That(A3.Recall(w, 1, tick, out _, out _, out long seen), Is.True);
                    Assert.That(seen, Is.EqualTo(86));
                    Assert.That(A3b.Count(w), Is.EqualTo(1));
                }
                if (tick == 126) Assert.That(A3a.SearchCount(w), Is.EqualTo(1));
                if (tick == 156)
                {
                    Assert.That(A3.Recall(w, 1, tick, out _, out _, out long seen), Is.True);
                    Assert.That(seen, Is.EqualTo(156));
                    Assert.That(A3a.SearchCount(w), Is.EqualTo(0));
                }
            }
            if (report) Report("contact-search-reacquisition", 61, bytes);
            return bytes;
        }

        [Category("Manual"), Test, Explicit("Full Sim.Step allocates on first contact, search and reacquisition.")]
        public void FullStep_FirstContactSearchAndReacquisition_AllocatesZero()
        {
            ContactWindow(false);
            Assert.That(ContactWindow(true), Is.All.EqualTo(0L));
        }

        private static GridPathfinder Pathfinder(M6World w) => (GridPathfinder)typeof(InterimLocomotion)
            .GetField("pathfinder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w.System<InterimLocomotion>());

        private static long PathWindow(bool report)
        {
            M6World w = World();
            GridPathfinder path = Pathfinder(w);
            Assert.That(path.Path.Length, Is.EqualTo(0), "warm-up has not searched a path");
            InputFrame input = A3.Command(SquadCommandKind.MoveTo, 1, A2.Cell(30, 14));
            Counter();
            long bytes = Step(w, input);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveWaypoint>(1).Has, Is.True);
            bool detours = false;
            foreach (int cell in path.Path) if (cell / 40 >= 25) detours = true;
            Assert.That(detours, Is.True, "the route goes around the wall");
            if (report) TestContext.WriteLine("first-complex-path tick=61 bytes=" + bytes + " routeCells=" + path.Path.Length);
            return bytes;
        }

        [Category("Manual"), Test, Explicit("Full Sim.Step expands the first path and squad buffers.")]
        public void FullStep_FirstComplexSquadPath_AllocatesZero()
        {
            PathWindow(false);
            Assert.That(PathWindow(true), Is.EqualTo(0L));
        }

        private static long[] ReinforceWindow(bool report)
        {
            M6World w = World(true);
            w.Step(24);
            Assert.That(w.Has<Position>(3), Is.False);
            Assert.That(w.Reinforce.Successes(1), Is.EqualTo(0));
            var bytes = new long[26];
            Counter();
            for (int tick = 85; tick <= 110; tick++)
            {
                bytes[tick - 85] = Step(w);
                if (tick == 90)
                {
                    Assert.That(w.Has<Position>(3), Is.True);
                    Assert.That(w.Reinforce.Successes(1), Is.EqualTo(1));
                    Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
                }
            }
            Assert.That(A3.Recall(w, 3, 110, out StableId target, out _, out _), Is.True);
            Assert.That(target, Is.EqualTo(A3.Id(w, 2)));
            if (report) Report("reinforcement", 85, bytes);
            return bytes;
        }

        [Category("Manual"), Test, Explicit("Full Sim.Step allocates when the first reinforcement discovers an enemy.")]
        public void FullStep_FirstReinforcement_AllocatesZero()
        {
            ReinforceWindow(false);
            Assert.That(ReinforceWindow(true), Is.All.EqualTo(0L));
        }

        private static void Report(string scenario, int first, long[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                TestContext.WriteLine(scenario + " tick=" + (first + i) + " bytes=" + bytes[i]);
        }
    }
}
#endif
