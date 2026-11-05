using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M5StressTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void C4_C6_PLACEHOLDER_TwoHundredSimultaneousShots_NoLostEvents(bool lethal)
        {
            var units = new UnitSpawn[200];
            for (int i = 0; i < 200; i++) units[i] = M5TestWorld.Spawn(i + 1, i / 2 * 3000 + i % 2 * 1000, 0, team: i % 2 + 1);
            var pack = M5TestWorld.Pack(lethal ? (IHitResolver)new LethalResolver() : new ZeroResolver());
            var world = new M5TestWorld(units, pack);
            world.Step(); Assert.That(world.Combat.Fires.Length, Is.EqualTo(200));
            int damage = 0, final = 0, life = 0, dead = 0;
            foreach (var e in world.Sim.Events.Read(1))
            { if (e.Type.Equals(GameplayEvents.Damage)) damage++; if (e.Type.Equals(GameplayEvents.FinalLife)) final++; if (e.Type.Equals(GameplayEvents.Life)) life++; }
            for (int i = 0; i < 200; i++) if (world.Dead(i)) dead++;
            Assert.That(damage, Is.EqualTo(200)); Assert.That(life, Is.EqualTo(dead));
            Assert.That(final, Is.EqualTo(200)); Assert.That(dead, Is.EqualTo(lethal ? 200 : 0));
            Assert.That(world.Sim.Events.EventsDroppedTotal, Is.Zero);
            Assert.That(world.Sim.Events.Read(1).Length, Is.EqualTo(damage + final + life));
            Assert.That(pack.BuildSchema(200).NoticeStagingCapacity, Is.EqualTo(801));
            TestContext.Out.WriteLine($"C4/C6 volley lethal={lethal} damage={damage} final={final} life={life} dropped={world.Sim.Events.EventsDroppedTotal}");
        }

        [Test]
        public void C6_PLACEHOLDER_NextTickDeadRejectsCommandAndDoesNotTakeFormationRank()
        {
            var units = new[] { M5TestWorld.Spawn(1, 2500, 2500, weapon: 2), M5TestWorld.Spawn(2, 4500, 3500, weapon: -1),
                M5TestWorld.Spawn(3, 6500, 2500, team: 2, weapon: -1) };
            var world = new M5TestWorld(units, map: M3TestWorld.Boxes(32, 24));
            world.Step(); Assert.That(world.Dead(2), Is.True); var corpse = world.Point(2);
            world.Sim.Step(M4TestWorld.Command(1, 16000, 12000));
            Assert.That(world.Sim.Entities.TryGet(world.Intents, world.Unit(2), out _), Is.False);
            Assert.That(world.Point(2), Is.EqualTo(corpse));
            Assert.That(world.Sim.Entities.Read(world.Intents, world.Unit(0)).Target, Is.EqualTo(M3TestWorld.Point(15000, 12000)));
            Assert.That(world.Sim.Entities.Read(world.Intents, world.Unit(1)).Target, Is.EqualTo(M3TestWorld.Point(17000, 12000)));
            for (int i = 0; i < 100; i++) world.Step(); Assert.That(world.Point(2), Is.EqualTo(corpse));
        }

        [Test]
        public void C6_PLACEHOLDER_CorpseDoesNotBlockNarrowPass_SurvivorMovementMatchesM4()
        {
            var map = M3TestWorld.Boxes(32, 10, (0, 0, 32, 4), (0, 5, 32, 5));
            var survivors = new[] { M5TestWorld.Spawn(1, 1500, 4500, weapon: -1, squad: 1),
                M5TestWorld.Spawn(2, 2500, 4500, weapon: -1, squad: 2), M5TestWorld.Spawn(3, 3500, 4500, weapon: 2, squad: 3) };
            var units = new[] { survivors[0], survivors[1], survivors[2], M5TestWorld.Spawn(4, 5500, 4500, team: 2, weapon: -1, squad: 4) };
            var battle = new M5TestWorld(units, map: map); var control = new M4TestWorld(map, survivors);
            for (int i = 0; i < 3; i++) { battle.Order(i, 22500 + i * 1000, 4500); control.Order(i, 22500 + i * 1000, 4500); }
            for (int tick = 1; tick <= 250; tick++)
            {
                battle.Step(); control.Sim.Step(default);
                for (int i = 0; i < 3; i++) Assert.That(battle.Point(i), Is.EqualTo(control.Point(i)), $"tick={tick} unit={i}");
                Assert.That(battle.Point(0).X, Is.LessThan(battle.Point(1).X)); Assert.That(battle.Point(1).X, Is.LessThan(battle.Point(2).X));
                if (tick == 1) Assert.That(battle.Dead(3), Is.True);
            }
            for (int i = 0; i < 3; i++) Assert.That(battle.Point(i).X.Raw, Is.GreaterThan(5500 * SimFix.Scale / 1000));
        }

        [TestCase(20)]
        [TestCase(200)]
        public void C0_PLACEHOLDER_MovingCombat600Ticks_ZeroAllocation_M4TimingControl(int count)
        {
            var units = new UnitSpawn[count];
            for (int i = 0; i < count; i++) units[i] = M5TestWorld.Spawn(i + 1, 2500 + i % 20 * 1000,
                2500 + i / 20 * 2000, team: i % 2 + 1, squad: i + 1);
            var map = M3TestWorld.Boxes(256, 32);
            var battle = new M5TestWorld(units, M5TestWorld.Pack(new ZeroResolver()), map: map);
            var control = new M4TestWorld(map, units);
            for (int i = 0; i < count; i++) { battle.Order(i, units[i].XMm + 220000, units[i].ZMm); control.Order(i, units[i].XMm + 220000, units[i].ZMm); }
            for (int i = 0; i < 100; i++) { battle.Step(); control.Sim.Step(default); }
            var points = new SimVec3[count]; for (int i = 0; i < count; i++) points[i] = battle.Point(i);
            var combatTimes = new double[600]; var controlTimes = new double[600];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 600; tick++)
            {
                long start = Stopwatch.GetTimestamp(); battle.Step();
                combatTimes[tick] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 600; tick++)
            {
                long start = Stopwatch.GetTimestamp(); control.Sim.Step(default);
                controlTimes[tick] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            long controlAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocation, Is.Zero); Assert.That(controlAllocation, Is.Zero);
            // Independent untimed replay checks active movement and shots throughout the entire measurement window.
            var verify = new M5TestWorld(units, M5TestWorld.Pack(new ZeroResolver()), map: map);
            for (int i = 0; i < count; i++) verify.Order(i, units[i].XMm + 220000, units[i].ZMm);
            for (int i = 0; i < 100; i++) verify.Step();
            int totalShots = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                verify.Step(); totalShots += verify.Combat.Fires.Length;
                for (int i = 0; i < count; i++)
                {
                    Assert.That(verify.Point(i), Is.Not.EqualTo(points[i]), $"tick={tick + 101} unit={i}");
                    Assert.That(verify.Sim.Entities.Read(verify.Intents, verify.Unit(i)).Active, Is.True);
                    points[i] = verify.Point(i);
                }
            }
            Assert.That(totalShots, Is.GreaterThan(count * 90));
            Assert.That(verify.Sim.StateHashRetained(), Is.EqualTo(battle.Sim.StateHashRetained()));
            for (int i = 0; i < count; i++) Assert.That(battle.Point(i), Is.EqualTo(control.Point(i)));
            double Mean(double[] times) { double sum = 0; foreach (double t in times) sum += t; return sum / times.Length; }
            double combatMean = Mean(combatTimes), controlMean = Mean(controlTimes); Array.Sort(combatTimes); Array.Sort(controlTimes);
            TestContext.Out.WriteLine($"M5 n={count} warmup=100 ticks=600 mean={combatMean:F6}ms p95={combatTimes[569]:F6}ms max={combatTimes[599]:F6}ms allocation={allocation}B shots={totalShots}");
            TestContext.Out.WriteLine($"M4 control n={count} mean={controlMean:F6}ms p95={controlTimes[569]:F6}ms max={controlTimes[599]:F6}ms allocation={controlAllocation}B ratio={combatMean / controlMean:F3}");
        }
    }
}
