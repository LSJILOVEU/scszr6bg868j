using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Slope1;

namespace WarCore.Tests
{
    // SLOPE-1: off by default (S1), saves and determinism (S10), the slope-race replay (S11), recipes carrying the pack (S28)
    // and where the pack comes from (S29).
    public sealed class Slope1WorldTests
    {
        private static readonly UnitSpawn[] Walkers = { Unit(1, 1500, 1500), Unit(2, 16500, 4500) };
        private static readonly BridgeCommand[] WalkerScript = { BridgeCommand.MoveTo(1, 1, 16500, 1500), BridgeCommand.MoveTo(2, 2, 1500, 4500) };

        private static List<ulong> Hashes(SimSession s, int ticks)
        {
            var hashes = new List<ulong>();
            for (int t = 0; t < ticks; t++) { s.StepOnce(); hashes.Add(Hash(s)); }
            return hashes;
        }

        private static StableContentManifest Content(SimSession s) => M3TestWorld.Sim(s).StableContent;

        // S1: a terrain world and a plate world, each with WithPack(a pack without the new sections) and without: the same
        // hashes tick by tick for 300 ticks, the same content manifest, the same locomotion state version.
        [Test]
        public void S1_APackWithoutTheSectionsChangesNothing()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 6, Rise600, "mud");
                SimSession with = Ai(ramp, Plain(), Walkers, WalkerScript);
                SimSession without = Ai(ramp, Plain(), Walkers, WalkerScript, withPack: false);
                Assert.That(Content(with).Digest, Is.EqualTo(Content(without).Digest), "terrain manifest");
                Assert.That(Loco(with).StateVersion, Is.EqualTo(Loco(without).StateVersion));
                Assert.That(Hashes(with, 300), Is.EqualTo(Hashes(without, 300)), "terrain");

                SimSession plateWith = SimSession.Create(Ml7Worlds.Recipe(Ml8DemoTests.Squad, Ml8DemoTests.Script).WithPack(Plain()), Ml8Worlds.Demo);
                SimSession plateWithout = SimSession.Create(Ml7Worlds.Recipe(Ml8DemoTests.Squad, Ml8DemoTests.Script), Ml8Worlds.Demo);
                Assert.That(Content(plateWith).Digest, Is.EqualTo(Content(plateWithout).Digest), "plate manifest");
                Assert.That(Hashes(plateWith, 300), Is.EqualTo(Hashes(plateWithout, 300)), "plates");
                Assert.Throws<SimBoundaryException>(() => Content(with).Key(SlopeFeature.ContentKeyName));
                // A stats row of another line (stats.spread) does not turn the step line on.
                ContentPack spread = Layer("slope-test", "\"stats\": { \"spread\": { \"dragFloorPermille\": 900 } }");
                SimSession spreadOnly = Ai(ramp, spread, Walkers, WalkerScript), plainAgain = Ai(ramp, Plain(), Walkers, WalkerScript);
                Assert.Throws<SimBoundaryException>(() => Content(spreadOnly).Key(SlopeFeature.ContentKeyName));
                Assert.That(Hashes(spreadOnly, 60), Is.EqualTo(Hashes(plainAgain, 60)), "stats.spread only");
            });
        }

        // S10: no new component and the same state version, so a sloped world's snapshot is as long as a plain one's; saved
        // at tick 150 and restored, the next 150 hashes equal the uninterrupted run; the same seed twice is the same; the
        // manifest carries movement.step = SlopeFeature.Digest; another slope table, another floor, or the feature off refuses
        // the snapshot.
        [Test]
        public void S10_SavesAndDeterminism()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 6, Rise600, "mud");
                SimSession sloped = Ai(ramp, Sloped(), Walkers, WalkerScript), plain = Ai(ramp, Plain(), Walkers, WalkerScript);
                Assert.That(AssemblyWorlds.Components(M3TestWorld.Sim(sloped)), Is.EqualTo(AssemblyWorlds.Components(M3TestWorld.Sim(plain))));
                Assert.That(Loco(sloped).StateVersion, Is.EqualTo(Loco(plain).StateVersion));
                Assert.That(M3TestWorld.Sim(sloped).SaveRetainedSnapshot().Length, Is.EqualTo(M3TestWorld.Sim(plain).SaveRetainedSnapshot().Length));
                Assert.That(Content(sloped).Key(SlopeFeature.ContentKeyName), Is.EqualTo(SlopeFeature.Digest(Sloped())));

                SimSession whole = SimSession.Create(SlopeDemo.Recipe(), SlopeDemo.MapBytes());
                SimSession again = SimSession.Create(SlopeDemo.Recipe(), SlopeDemo.MapBytes());
                List<ulong> first = Hashes(whole, 300);
                Assert.That(Hashes(again, 300), Is.EqualTo(first), "the same seed twice");

                SimSession saved = SimSession.Create(SlopeDemo.Recipe(), SlopeDemo.MapBytes());
                Hashes(saved, 150);
                byte[] snapshot = M3TestWorld.Sim(saved).SaveRetainedSnapshot();
                SimSession restored = SimSession.Create(SlopeDemo.Recipe(), SlopeDemo.MapBytes());
                Hashes(restored, 10);
                M3TestWorld.Sim(restored).RestoreRetainedSnapshot(snapshot);
                Assert.That(restored.Tick, Is.EqualTo(150));
                Assert.That(Hashes(restored, 150), Is.EqualTo(first.GetRange(150, 150)), "resumed after the restore");

                sloped.StepOnce();
                byte[] onRamp = M3TestWorld.Sim(sloped).SaveRetainedSnapshot();
                ContentPack otherTable = Layer("slope-test", "\"movement\": { \"slope\": { \"up\": [ { \"abovePermille\": 100, \"speedPermille\": 901 } ] } }");
                ContentPack otherFloor = Layer("slope-test", MovementJson + ", " + Stats(floor: 400));
                foreach (ContentPack other in new[] { otherTable, otherFloor, Plain() })
                    Assert.Catch<SimSnapshotException>(() => M3TestWorld.Sim(Ai(ramp, other, Walkers, WalkerScript)).RestoreRetainedSnapshot(onRamp));
                Assert.DoesNotThrow(() => M3TestWorld.Sim(Ai(ramp, Sloped(), Walkers, WalkerScript)).RestoreRetainedSnapshot(onRamp));
            });
        }

        // S10: the content key's byte layout: the prefix, the up band count and bands, the down band count and bands, then the
        // speed row's floor and ceiling (0 when not written); -1 in place of the bands when there is no slope table.
        [Test]
        public void S10_TheDigestLayout()
        {
            Assert.DoesNotThrow(() =>
            {
                var expected = new StateHash();
                expected.Add("movement.step.v1");
                foreach (int v in new[] { 3, 100, 900, 300, 700, 500, 500, 1, 400, 800, 400, 0 }) expected.Add(v);
                Assert.That(SlopeFeature.Digest(RwrPack.Load("rwr-slope")), Is.EqualTo(expected.Value));

                var statsOnly = new StateHash();
                statsOnly.Add("movement.step.v1");
                foreach (int v in new[] { -1, 300, 1100 }) statsOnly.Add(v);
                Assert.That(SlopeFeature.Digest(Layer("slope-test", Stats(300, 1100))), Is.EqualTo(statsOnly.Value));
            });
        }

        // S11: slope-race. 18 m = 1179648 raw a lane at 3000 mm/s (6553 raw a tick). Flat 6553; uphill 26214 raw per metre ->
        // 13107 over the probe -> 399 permille -> 700: 4587; downhill 39322 -> 19661 -> 600 -> 800: 5242. A unit ordered at
        // tick k arrives on the tick k - 1 + ceil(1179648 / step): 1 + 180 = 181, 2 + 257 = 259, 3 + 225 = 228.
        [Test]
        public void S11_TheSlopeRaceArrivals()
        {
            Assert.DoesNotThrow(() =>
            {
                long[] steps = { 6553, 6553 * 700 / 1000, 6553 * 800 / 1000 };
                var expected = new long[3];
                for (int lane = 0; lane < 3; lane++)
                    expected[lane] = SlopeDemo.OrderTick(lane) - 1 + (1179648 + steps[lane] - 1) / steps[lane];
                Assert.That(expected, Is.EqualTo(new long[] { 181, 259, 228 }));

                SimSession s = SimSession.Create(SlopeDemo.Recipe(), SlopeDemo.MapBytes());
                var arrived = new long[3];
                for (int t = 0; t < 400; t++)
                {
                    s.StepOnce();
                    for (int lane = 0; lane < 3; lane++)
                        if (arrived[lane] == 0 && s.Tick > SlopeDemo.OrderTick(lane) && Loco(s).IsArrived(Id(s, (ulong)lane + 1))) arrived[lane] = s.Tick;
                }
                Assert.That(arrived[0], Is.EqualTo(expected[0]), "flat");
                Assert.That(arrived[1], Is.EqualTo(expected[1]), "uphill");
                Assert.That(arrived[2], Is.EqualTo(expected[2]), "downhill");
                for (int lane = 0; lane < 3; lane++)
                    Assert.That(At(s, (ulong)lane + 1).X.Raw, Is.EqualTo(SimFix.Ratio(SlopeDemo.GoalXMm(lane), 1000).Raw), "lane " + lane);
                Assert.That(arrived[0] < arrived[2] && arrived[2] < arrived[1], Is.True, "flat, then downhill, then uphill");
            });
        }

        // S28: MapRecipe.From keeps the pack it was given; WithPlayerBlock and WithPlayerTeamDash copy it, and the worlds they
        // build carry movement.step.
        [Test]
        public void S28_RecipesCarryThePack()
        {
            Assert.DoesNotThrow(() =>
            {
                ContentPack m6 = RwrPack.Load("rwr-m6");
                Assert.That(MapRecipe.From(MapBake.Run(Trv1aDemo.DescBytes()), m6).Pack, Is.SameAs(m6));

                ContentPack slope = RwrPack.Load("rwr-slope");
                WcMap map = RampX(20, 6, Rise600, "grass");
                WorldRecipe source = PlayerRecipe(map, slope, 1500, 1500);
                WorldRecipe blocked = DemoRecipes.WithPlayerBlock(source, new BlockSpec(500, 1000));
                MethodInfo teamDash = typeof(DemoRecipes).GetMethod("WithPlayerTeamDash", BindingFlags.NonPublic | BindingFlags.Static);
                var dashed = (WorldRecipe)teamDash.Invoke(null, new object[] { source, RwrPack.Load(DemoRecipes.PlaytestLayer) });
                foreach (WorldRecipe r in new[] { blocked, dashed })
                {
                    Assert.That(r.Pack, Is.SameAs(slope));
                    SimSession s = SimSession.Create(r, WcMapWriter.Write(map));
                    Assert.That(Content(s).Key(SlopeFeature.ContentKeyName), Is.EqualTo(SlopeFeature.Digest(slope)));
                }
            });
        }

        // S29: with the feature on, a pack and a terrain pack of different ids refuse the world; with it off the same pair
        // builds; a recipe that only calls WithTerrainRules(sloped pack) still takes the slope (4369 up a 600 permille ramp).
        [Test]
        public void S29_WhereThePackComesFrom()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 3, Rise600);
                byte[] bytes = WcMapWriter.Write(ramp);
                WorldRecipe Pair(ContentPack terrain, ContentPack pack) =>
                    new WorldRecipe(41, MoverKind.InterimCrowd, new[] { Unit(1, 1500, 1500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) })
                        .WithTerrainRules(terrain).WithPack(pack);
                Assert.Throws<ArgumentException>(() => SimSession.Create(Pair(Sloped("slope-a"), Sloped("slope-b")), bytes));
                Assert.Throws<ArgumentException>(() => SimSession.Create(Pair(Plain("slope-a"), Sloped("slope-b")), bytes));
                Assert.DoesNotThrow(() =>
                {
                    SimSession.Create(Pair(Plain("slope-a"), Plain("slope-b")), bytes);
                    SimSession terrainOnly = Ai(ramp, Sloped(), new[] { Unit(1, 1500, 1500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) }, withPack: false);
                    Assert.That(FirstMove(terrainOnly).X.Raw, Is.EqualTo(4369));
                });
            });
        }
    }
}
