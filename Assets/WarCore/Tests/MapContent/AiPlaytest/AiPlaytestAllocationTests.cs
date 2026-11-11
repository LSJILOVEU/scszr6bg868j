#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class AiPlaytestAllocationTests
    {
        private static readonly SimVec3 Home = A2.Cell(10, 20), Hidden = A2.Cell(30, 20), Seen = A2.Cell(15, 20);

        private static M6World World(bool target = false, int deployed = 0)
        {
            var units = new UnitSpawn[deployed > 0 ? deployed + 1 : target ? 2 : 1];
            units[0] = A2.Soldier(1, 1, Home, target ? 0 : -1, 3000);
            if (target) units[1] = A2.Soldier(2, 2, Hidden, -1, 3000);
            if (deployed > 0)
            {
                for (int i = 0; i < deployed; i++) units[i] = A2.Soldier((ulong)i + 1, 1, A2.Cell(10 + i % 4, 10 + i / 4), -1, 3000);
                units[deployed] = new UnitSpawn((ulong)deployed + 1, 0, 2, 5000, 20000, 3000, 1, -1, true);
            }
            var match = M6World.Pack(new[] { new ControlPointDef(1, Home, SimFix.Ratio(1, 2), initialOwner: 1) },
                ticks: 100000, think: 10, bases: new[] { new SpawnAnchor(1, A2.Cell(5, 20)) }, sight: A2.Sight);
            WorldRecipe recipe = M6World.RecipeOf(units, match, A2.Combat(0))
                .WithTerrainRules(RwrPack.Load("rwr-ai-playtest")).WithStandingOrders().WithRuleSet(MatchMode.TestNeverEndsId);
            return new M6World(recipe, A1.Map(new[] { (20, 5, 1, 30) }));
        }

        private static long Window(M6World w, int ticks, string label, InputFrame first = default)
        {
            long start = w.Sim.Clock.Tick + 1, total = 0, peak = 0, firstTick = 0, firstBytes = 0;
            for (int i = 0; i < ticks; i++)
            {
                InputFrame input = i == 0 ? first : default;
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(input);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                total += bytes;
                if (bytes > peak) peak = bytes;
                if (bytes > 0 && firstTick == 0) { firstTick = start + i; firstBytes = bytes; }
            }
            TestContext.Out.WriteLine($"{label}: ticks={start}..{w.Sim.Clock.Tick}, bytes={total}, peak={peak}, first={firstTick}:{firstBytes}");
            return total;
        }

        private static void Quiet(M6World w, int squad)
        {
            w.Sim.Step(A3.Command(SquadCommandKind.Hold, squad));
            w.Step(29);
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

        private static long Contacts(M6World w)
        {
            Quiet(w, 2);
            TargetMemory memory = w.System<TargetMemory>();
            StableId observer = A3.Id(w, 1);
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(2)), Is.False);
            Assert.That(memory.TryRecall(observer, 30, out _, out _, out _), Is.False);
            A3b.Place(w, 2, Seen);
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(2)), Is.True);
            long bytes = Window(w, 6, "first sight");
            Assert.That(memory.TryRecall(observer, 36, out StableId target, out _, out long seen), Is.True);
            Assert.That(target, Is.EqualTo(A3.Id(w, 2)));
            Assert.That(seen, Is.EqualTo(36));
            bytes += Window(w, 4, "retained contact");
            A3b.Place(w, 2, Hidden);
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(2)), Is.False);
            bytes += Window(w, 100, "lost sight and search");
            Assert.That(memory.TryRecall(observer, 140, out _, out _, out seen), Is.True);
            Assert.That(seen, Is.EqualTo(36));
            Assert.That(A3a.SearchCount(w), Is.EqualTo(1));
            bytes += Window(w, 210, "memory expiry");
            Assert.That(memory.TryRecall(observer, 350, out _, out _, out _), Is.False);
            A3b.Place(w, 2, w.Read<Position>(1).Value + SimVec3.Metres(0, 0, 5));
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(2)), Is.True);
            bytes += Window(w, 6, "reacquisition");
            Assert.That(memory.TryRecall(observer, 356, out target, out _, out seen), Is.True);
            Assert.That(target, Is.EqualTo(A3.Id(w, 2)));
            Assert.That(seen, Is.EqualTo(356));
            return bytes;
        }

        [Test]
        public void FullStep_FirstContactLossAndReacquisition_AllocateZero()
        {
            Contacts(World(target: true));
            var w = World(target: true);
            Counter();
            long bytes = Contacts(w);
            Assert.That(bytes, Is.EqualTo(0L), "complete Sim.Step contact transitions");
        }

        private static long ComplexPath(M6World w)
        {
            Quiet(w, 1);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
            long bytes = Window(w, 1, "first complex path", A3.Command(SquadCommandKind.MoveTo, 1, Hidden));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(Hidden));
            Assert.That(w.Read<MoveWaypoint>(1).Point, Is.Not.EqualTo(Hidden));
            return bytes + Window(w, 29, "path continuation");
        }

        [Test]
        public void FullStep_FirstComplexPath_AllocatesZero()
        {
            ComplexPath(World());
            var w = World();
            Counter();
            long bytes = ComplexPath(w);
            Assert.That(bytes, Is.EqualTo(0L), "complete Sim.Step first path and squad command");
        }

        private static long Reinforcement(M6World w, int deployed)
        {
            Quiet(w, 1);
            w.Step(59);
            Assert.That(w.Has<Position>((ulong)deployed + 1), Is.False);
            long bytes = Window(w, 1, "first reinforcement");
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed[0].Unit, Is.EqualTo(A3.Id(w, (ulong)deployed + 1)));
            Assert.That(w.Has<Position>((ulong)deployed + 1), Is.True);
            return bytes;
        }

        [TestCase(1)]
        [TestCase(16, Reason = "Full Sim.Step allocates when reinforcement grows the crowd buffer.")]
        public void FullStep_FirstReinforcement_AllocatesZero(int deployed)
        {
            Reinforcement(World(deployed: deployed), deployed);
            var w = World(deployed: deployed);
            Counter();
            long bytes = Reinforcement(w, deployed);
            Assert.That(bytes, Is.EqualTo(0L), "complete Sim.Step first reinforcement");
        }
    }
}
#endif
