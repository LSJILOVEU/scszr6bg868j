using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp13StressTests
    {
        [Test]
        public void PLACEHOLDER_TwoHundredAimedLethalHits_Exactly600Events_NoDrops()
        {
            var w = Paired(new LethalResolver()); w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(200));
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(200));
            Assert.That(w.EventCount(GameplayEvents.FinalLife), Is.EqualTo(200));
            Assert.That(w.EventCount(GameplayEvents.Life), Is.EqualTo(200));
            int deaths = 0; for (int i = 0; i < 200; i++) if (w.Dead(i)) deaths++;
            Assert.That(deaths, Is.EqualTo(200)); Assert.That(w.Sim.Events.Read(1).Length, Is.EqualTo(600));
            Assert.That(w.Sim.Events.EventsDroppedThisTick, Is.Zero); Assert.That(w.Sim.Events.EventsDroppedTotal, Is.Zero);
            foreach (var f in w.Combat.Fires) Assert.That(f.DamageRaw, Is.EqualTo(w.Pack.MaxHealthRaw));
        }

        [TestCase("Aimed")]
        [TestCase("AtTarget")]
        [TestCase("Miss")]
        public void ContinuousSubmit_600TicksAllocateZeroBytes(string path)
        {
            var w = Paired(new ZeroResolver(), path);
            for (int tick = 0; tick < 100; tick++) w.Step();
            int shots = 0, misses = 0, damageEvents = 0;
            ulong rngBefore = Wp13TestWorld.Hash(w.Sim.Rng);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 600; tick++)
            {
                w.Step(); shots += w.Combat.Fires.Length;
                foreach (var fire in w.Combat.Fires) if (fire.Target.Equals(default)) misses++;
                damageEvents += w.EventCount(GameplayEvents.Damage);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero); Assert.That(shots, Is.GreaterThan(200 * 90));
            Assert.That(w.Sim.Events.EventsDroppedTotal, Is.Zero);
            Assert.That(w.Combat.QueriesThisStep, Is.Zero);
            Assert.That(misses, Is.EqualTo(path == "Miss" ? shots : 0));
            Assert.That(damageEvents, Is.EqualTo(path == "Miss" ? 0 : shots));
            if (path == "Miss") Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rngBefore));
            TestContext.Out.WriteLine("WP13 Submit+" + path + " n=200 warmup=100 ticks=600 allocation=" + allocated + "B shots=" + shots);
        }

        private static Wp13TestWorld Paired(IHitResolver resolver, string path = "Aimed")
        {
            var units = new UnitSpawn[200];
            for (int i = 0; i < 200; i++) units[i] = M5TestWorld.Spawn(i + 1, i / 2 * 3000 + i % 2 * 1000, 0, team: i % 2 + 1);
            var w = new Wp13TestWorld(units, M5TestWorld.Pack(resolver));
            w.Produce = c =>
            {
                for (int i = 0; i < 200; i++)
                {
                    if (w.Dead(i)) continue;
                    if (path == "AtTarget") w.Target(c, i, i ^ 1);
                    else w.Aim(c, i, path == "Miss" ? w.Point(i) + SimVec3.Metres(0, 0, 1) : w.Point(i ^ 1));
                }
            };
            return w;
        }
    }
}
