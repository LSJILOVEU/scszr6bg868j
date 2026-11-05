using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M5BallisticsTests
    {
        [TestCase(0, 3390, 99, 159)]
        [TestCase(1, 3690, 108, 168)]
        public void C1_PLACEHOLDER_AccumulatedCadence_Reload(int weapon, long interval, int last, int reload)
        {
            var world = new M5TestWorld(M5TestWorld.Pair(weapon), M5TestWorld.Pack(new ZeroResolver()));
            var ticks = new List<long>();
            while (ticks.Count < 31 && world.Sim.Clock.Tick < 200)
            { world.Step(); if (world.Combat.Fires.Length != 0) ticks.Add(world.Sim.Clock.Tick); }
            Assert.That(ticks.Count, Is.EqualTo(31));
            for (int n = 1; n <= 30; n++)
            {
                long offset = ticks[n - 1] - ticks[0];
                Assert.That(offset, Is.EqualTo(((n - 1) * interval + 999) / 1000), "shot=" + n);
                Assert.That(Math.Abs(offset - (n - 1) * interval / 1000.0), Is.LessThanOrEqualTo(1), "parity table shot=" + n);
                if (n > 1) Assert.That(ticks[n - 1] - ticks[n - 2], Is.InRange(3, 4));
            }
            Assert.That(ticks[29] - ticks[0], Is.EqualTo(last));
            Assert.That(ticks[30] - ticks[0], Is.EqualTo(reload));
            Assert.That(ticks[30] - ticks[29], Is.EqualTo(60));
            TestContext.Out.WriteLine($"C1 weapon={weapon} first={ticks[0]} shot30-offset={last} shot31-offset={reload}");
        }

        private sealed class Gate : ITargetSelector
        {
            internal bool Open = true;
            public ulong PolicyKey => 100;
            public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
            { target = default; return Open && new NearestVisibleEnemy().TrySelect(world, shooter, range, out target); }
        }
        [Test]
        public void C1_PLACEHOLDER_IdleResetsZero_NoTargetDoesNotConsumeStateOrRandom()
        {
            var gate = new Gate(); var resolver = new RecordingResolver();
            var world = new M5TestWorld(M5TestWorld.Pair(), M5TestWorld.Pack(resolver, gate));
            world.Step(); gate.Open = false;
            Combatant before = world.State(0); var hash = new StateHash(); world.Sim.Rng.WriteState(hash);
            for (int i = 0; i < 40; i++) world.Step();
            Assert.That(world.State(0), Is.EqualTo(before));
            var after = new StateHash(); world.Sim.Rng.WriteState(after); Assert.That(after.Value, Is.EqualTo(hash.Value));
            gate.Open = true; world.Step(); Assert.That(world.Combat.Fires.Length, Is.EqualTo(1));
            long zero = world.Sim.Clock.Tick;
            Assert.That(world.State(0).DueMilli, Is.EqualTo(zero * 1000 + 3390));
            for (int i = 1; i <= 4; i++) { world.Step(); Assert.That(world.Combat.Fires.Length, Is.EqualTo(i == 4 ? 1 : 0)); }
        }

        internal static RecordingResolver Sample(int weapon, long distance, int count = 1000)
        {
            var resolver = new RecordingResolver();
            var world = new M5TestWorld(M5TestWorld.Pair(weapon, distance), M5TestWorld.Pack(resolver));
            for (int tick = 0; resolver.Samples.Count < count && tick < 50000; tick++) world.Step();
            Assert.That(resolver.Samples.Count, Is.EqualTo(count));
            return resolver;
        }
        [TestCase(0, 10000, 0.5)]
        [TestCase(1, 10000, 0.55)]
        [TestCase(2, 10000, 1.0)]
        [TestCase(0, 50500, 0.25)]
        [TestCase(2, 121500, 0.5)]
        public void C2_C3_PLACEHOLDER_ThousandShots_ThreeSigma_Reproducible(int weapon, long distance, double p)
        {
            var a = Sample(weapon, distance); var b = Sample(weapon, distance);
            Assert.That(b.Samples, Is.EqualTo(a.Samples));
            int lethal = 0; foreach (var sample in a.Samples) if (sample.damage != 0) lethal++;
            double sigma = Math.Sqrt(p * (1 - p) / 1000);
            Assert.That(Math.Abs(lethal / 1000.0 - p), Is.LessThanOrEqualTo(3 * sigma));
            if (p == 1) Assert.That(lethal, Is.EqualTo(1000));
            TestContext.Out.WriteLine($"C2/C3 weapon={weapon} distanceMm={distance} lethal={lethal}/1000 p={p} 3sigma={3 * sigma:F9} falloffRaw={a.Samples[0].falloff}");
        }

        [TestCase(0, 34, 67)]
        [TestCase(2, 99, 144)]
        public void C3_PLACEHOLDER_FalloffBoundary_InsideAtOutside(int weapon, int start, int end)
        {
            WeaponDef w = M5TestWorld.NoSpread(RwrPack.Create().Weapons[weapon]);
            foreach (long raw in new[] { 0L, start * SimFix.Scale - 1, start * SimFix.Scale })
                Assert.That(w.FalloffRaw(SimFix.FromRaw(raw)), Is.EqualTo(65536));
            Assert.That(w.FalloffRaw(SimFix.FromRaw(start * SimFix.Scale + 1)), Is.EqualTo((end * SimFix.Scale - start * SimFix.Scale - 1) * 65536 / ((end - start) * SimFix.Scale)));
            Assert.That(w.FalloffRaw(SimFix.Ratio(start + end, 2)), Is.EqualTo(32768));
            Assert.That(w.FalloffRaw(SimFix.FromRaw(end * SimFix.Scale)), Is.Zero);
            Assert.That(w.FalloffRaw(SimFix.FromRaw(end * SimFix.Scale + 1)), Is.Zero);
            foreach (long mm in new[] { start * 1000L - 1, start * 1000L, start * 1000L + 1, end * 1000L - 1, end * 1000L, end * 1000L + 1 })
            {
                var resolver = new RecordingResolver(); var world = new M5TestWorld(M5TestWorld.Pair(weapon, mm), M5TestWorld.Pack(resolver));
                world.Step();
                Assert.That(resolver.Samples.Count, Is.EqualTo(mm > end * 1000L ? 0 : 1), "distanceMm=" + mm);
                if (resolver.Samples.Count == 0) continue;
                Assert.That(resolver.Samples[0].falloff, Is.EqualTo(w.FalloffRaw(SimFix.Ratio(mm, 1000))));
                if (mm == end * 1000L) Assert.That(resolver.Samples[0].damage, Is.Zero);
            }
        }

        [Test]
        public void C3_PLACEHOLDER_ZeroFalloffStillDraws_RandomReplayStableShooterOrder()
        {
            var resolver = new RecordingResolver();
            var units = new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 67000, 67000), M5TestWorld.Spawn(3, 67000, team: 2, weapon: -1) };
            var world = new M5TestWorld(units, M5TestWorld.Pack(resolver), reverse: true);
            var rng = new SimRandom(41); var stream = rng.Stream("combat.hit");
            int shots = 0;
            for (int tick = 0; tick < 180; tick++)
            {
                world.Step(); StableId previous = default; bool first = true;
                foreach (var shot in world.Combat.Fires)
                {
                    if (!first) Assert.That(shot.Shooter.CompareTo(previous), Is.GreaterThan(0));
                    previous = shot.Shooter; first = false;
                    Assert.That(resolver.Samples[shots].falloff, Is.Zero);
                    Assert.That(resolver.Samples[shots].roll, Is.EqualTo(rng.NextInt(stream, 65536)), "shot=" + shots);
                    Assert.That(shot.DamageRaw, Is.Zero); shots++;
                }
            }
            Assert.That(shots, Is.GreaterThanOrEqualTo(62));
        }

        [Test]
        public void C1_C3_WeaponValidation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDef(999, 1, 1000, default, SimFix.FromInt(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDef(1000, 0, 1000, default, SimFix.FromInt(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDef(1000, 1, 999, default, SimFix.FromInt(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDef(1000, 1, 1000, SimFix.FromInt(-1), SimFix.FromInt(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDef(1000, 1, 1000, SimFix.FromInt(1), SimFix.FromInt(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDef(1000, 1, 1000, SimFix.FromInt(2), SimFix.FromInt(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDef(1000, 1, 1000, default, SimFix.FromInt(1), -1));
            Assert.DoesNotThrow(() => new WeaponDef(1000, 1, 1000, default, SimFix.FromInt(1), 65537)); // effect semantics belong to the resolver.
        }
    }
}
