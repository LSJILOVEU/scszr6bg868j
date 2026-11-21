using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Blast1
{
    // BLAST-1 B11 and B12: the scripted blast command, the frame's blast list, and the "blast-wall" replay setup.
    public sealed class Blast1SessionTests
    {
        private const long M = SimFix.Scale;

        private static SimSession Demo() => SimSession.Create(BlastDemo.Recipe(), BlastDemo.MapBytes());

        private static long Health(SimSession s, ulong key)
        {
            var sim = M3TestWorld.Sim(s);
            return sim.GameplayResource(sim.Entities.StableOf(M3TestWorld.Unit(s, key)), 1);
        }

        // B11: the command goes off on its tick at the ground point, shows in that frame only, and is no squad command.
        [Test]
        public void TheCommandGoesOffOnItsTick()
        {
            SimSession s = Demo();
            for (int t = 1; t < BlastDemo.BlastTick; t++)
            {
                s.StepOnce();
                Assert.That(s.Current.Blasts.Length, Is.Zero, "tick " + t);
            }
            Assert.DoesNotThrow(() => s.StepOnce(), "a blast command never reaches the squad switch");
            Assert.That(s.Tick, Is.EqualTo(BlastDemo.BlastTick));
            ViewBlast blast = s.Current.Blasts.ToArray().Single();
            Assert.That((blast.X, blast.Y, blast.Z, blast.Radius, blast.Team), Is.EqualTo((14f, 0f, 12f, 6f, 0)));
            Assert.That(Ml7Worlds.Input(s).Squad.HasValue, Is.False, "the blast tick carries no squad command");
            Assert.That(s.CommandLog.Single().Kind, Is.EqualTo(BridgeCommandKind.Blast));
            Assert.That(s.CommandLog.Single().BlastId, Is.EqualTo(BlastDemo.Blast));
            // Interpolated frames carry the current blasts.
            Assert.That(FrameView.Interpolate(s.Previous, s.Current, 0.5f).Blasts.Length, Is.EqualTo(1));
            s.StepOnce();
            Assert.That(s.Current.Blasts.Length, Is.Zero);
        }

        // B12: the west group is hurt by distance (1 m and 1.5 m die, 3 m and 4.5 m keep 28.13 and 64.61 health), the east
        // group behind the 3 m wall is untouched; nobody fired.
        [Test]
        public void TheWallKeepsTheEastGroupWhole()
        {
            SimSession s = Demo();
            var health = new long[8];
            for (int t = 0; t < BlastDemo.BlastTick + 1; t++)
            {
                s.StepOnce();
                Assert.That(s.Current.Fires.Length, Is.Zero, "tick " + s.Tick);
            }
            for (int i = 0; i < 8; i++) health[i] = Health(s, (ulong)i + 1);
            long Hurt(long hx, long hz)
            {
                long d = new SimVec3(SimFix.FromRaw(hx), SimFix.FromRaw(Blast1World.Centre), SimFix.FromRaw(hz)).Length.Raw;
                return Blast1World.Expected(d);
            }
            long third = Hurt(3 * M, 0), fourth = Hurt(0, 9 * M / 2);
            Assert.That((third, fourth), Is.EqualTo((4710400L, 2319525L)), "3.125 m and 4.584 m from the blast");
            Assert.That(health, Is.EqualTo(new[] { 0L, 0L, 100 * M - third, 100 * M - fourth, 100 * M, 100 * M, 100 * M, 100 * M }));
            Assert.That(Enumerable.Range(0, 8).Select(i => s.Current.TryGet((ulong)i + 1, out ViewEntity e) && e.Alive),
                Is.EqualTo(new[] { false, false, true, true, true, true, true, true }));
        }

        // B11: a replay of the command log gives the same run.
        [Test]
        public void TheCommandLogReplays()
        {
            SimSession a = Demo();
            for (int t = 0; t < 70; t++) a.StepOnce();
            WorldRecipe recipe = BlastDemo.Recipe();
            var again = new WorldRecipe(recipe.Seed, recipe.Mover, recipe.Units.ToArray(), a.CommandLog.ToArray()).WithCombat(recipe.Combat);
            SimSession b = SimSession.Create(again, BlastDemo.MapBytes()), c = Demo();
            for (int t = 0; t < 70; t++) { b.StepOnce(); c.StepOnce(); }
            Assert.That((b.StateHash, c.StateHash), Is.EqualTo((a.StateHash, a.StateHash)));
        }

        // B11: the command is checked when it is queued.
        [Test]
        public void TheCommandIsChecked()
        {
            SimSession s = Demo();
            StringAssert.Contains("no blast 'rwr.none'", Assert.Throws<ArgumentException>(() => s.Enqueue(BridgeCommand.Blast(5, 1000, 1000, "rwr.none"))).Message);
            StringAssert.StartsWith("Blast point names layer 1 on a map without floor plates.",
                Assert.Throws<ArgumentException>(() => s.Enqueue(BridgeCommand.Blast(5, 1000, 1000, BlastDemo.Blast, 1))).Message);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Enqueue(BridgeCommand.Blast(5, 10_000_000_000, 1000, BlastDemo.Blast)));
            Assert.Throws<ArgumentException>(() => BridgeCommand.Blast(5, 0, 0, ""));
            Assert.Throws<ArgumentException>(() => BridgeCommand.Blast(5, 0, 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.Blast(0, 0, 0, BlastDemo.Blast));
            Assert.Throws<ArgumentOutOfRangeException>(() => BridgeCommand.Blast(1, 0, 0, BlastDemo.Blast, -1));
            SimSession plain = SimSession.Create(DemoRecipes.M6(), DemoRecipes.M6MapBytes());
            StringAssert.Contains("has no blasts", Assert.Throws<InvalidOperationException>(() => plain.Enqueue(BridgeCommand.Blast(5, 1000, 1000, BlastDemo.Blast))).Message);
        }

        // B11: on a terrain map the blast lies on the terrain's surface there (0.25 m per metre along x: 2 m at x = 8, 2.125 m at 8.5).
        [TestCase(8000, 2 * M)]
        [TestCase(8500, 2 * M + M / 8)]
        public void OnTerrainTheBlastLiesOnTheGround(long xMm, long yRaw)
        {
            ContentPack pack = RwrPack.Load(BlastDemo.Layer);
            WcMap map = Wp23.Make(20, 10, 0, 8 * M, null, 2 * M, (i, j) => (int)(i * M / 4), new[] { "grass" }, (x, z) => 0);
            var units = new[] { new UnitSpawn(1, 0, 1, 15500, 5500, 4000, 1) };
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, new[] { BridgeCommand.Blast(3, xMm, 5000, BlastDemo.Blast) })
                .WithTerrainRules(pack).WithCombat(pack.BuildCombat());
            SimSession s = SimSession.Create(recipe, WcMapWriter.Write(map));
            s.StepOnce(); s.StepOnce(); s.StepOnce();
            ViewBlast blast = s.Current.Blasts.ToArray().Single();
            Assert.That((blast.X, blast.Y, blast.Z), Is.EqualTo(((float)xMm / 1000, (float)yRaw / M, 5f)));
            StringAssert.StartsWith("Blast point is outside the terrain.",
                Assert.Throws<ArgumentException>(() => s.Enqueue(BridgeCommand.Blast(9, 25000, 5000, BlastDemo.Blast))).Message);
        }

        // B11: the layer form puts the blast on the plate's surface (layer 1 of the ML-7 map: 3 m), and it stops at the plate:
        // the unit on the ground right below is not hit, the one on the plate is.
        [Test]
        public void OnAPlateTheBlastLiesOnThePlate()
        {
            ContentPack pack = RwrPack.Load(BlastDemo.Layer);
            var units = new[] { new UnitSpawn(1, 0, 1, 6500, 1500, 6000, 1, layer: 1), new UnitSpawn(2, 0, 2, 7500, 2500, 6000, 1) };
            var recipe = Ml7Worlds.Recipe(units, new[] { BridgeCommand.Blast(2, 7500, 1500, BlastDemo.Blast, 1) }).WithCombat(pack.BuildCombat());
            SimSession s = SimSession.Create(recipe, Ml7Worlds.Plates);
            s.StepOnce(); s.StepOnce();
            ViewBlast blast = s.Current.Blasts.ToArray().Single();
            Assert.That((blast.X, blast.Y, blast.Z), Is.EqualTo((7.5f, 3f, 1.5f)));
            s.StepOnce();
            Assert.That(Health(s, 1), Is.LessThan(100 * M), "on the plate");
            Assert.That(Health(s, 2), Is.EqualTo(100 * M), "under the plate");
            StringAssert.StartsWith("Blast point is not on layer 1.",
                Assert.Throws<ArgumentException>(() => s.Enqueue(BridgeCommand.Blast(9, 8500, 2500, BlastDemo.Blast, 1))).Message);
        }
    }
}
