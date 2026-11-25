using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Slope1;

namespace WarCore.Tests
{
    // SLOPE-1: plates (S12 stair, S13 ramp), the dash (S14), the read-only receipt (S21) and inertia (S22).
    public sealed class Slope1PlateTests
    {
        // A unit of key 1 on a plate layer (6000 mm/s: 6000 * 65536 / 30000 = 13107 raw a tick unless given).
        private static UnitSpawn OnLayer(long xMm, long zMm, int layer, long speed = 6000) =>
            new UnitSpawn(1, 0, 1, xMm, zMm, speed, layer: layer);

        private static SimSession Plates(byte[] map, UnitSpawn unit, BridgeCommand order, ContentPack pack, bool terrain = false)
        {
            WorldRecipe recipe = Ml7Worlds.Recipe(new[] { unit }, new[] { order }).WithPack(pack);
            return SimSession.Create(terrain ? recipe.WithPack(pack) : recipe, map);
        }

        // S12 (and S9 on plates): the demo stair (layer 1, x 10..14 m, z 9..10 m) rises 0.75 m a metre: the probe 0.5 m ahead
        // rises 24576 raw -> 750 permille: up (> 500) 500, down (> 400) 800. Up 13107 * 500 / 1000 = 6553; down
        // 13107 * 800 / 1000 = 10485. At the seams the probe leaves the stair's field: 1000.
        [Test]
        public void S12_TheStair()
        {
            Assert.DoesNotThrow(() =>
            {
                SimSession up = Plates(Ml8Worlds.Demo, OnLayer(10600, 9500, 1), BridgeCommand.MoveTo(1, 1, 13400, 9500, 1), Sloped());
                Assert.That(Loco(up).ExplainStep(Id(up), Dir(1, 0), SimFix.FromRaw(13107)).Receipt.Drags[1], Is.EqualTo(500));
                Assert.That(FirstMove(up).X.Raw, Is.EqualTo(6553));
                SimSession plainUp = Plates(Ml8Worlds.Demo, OnLayer(10600, 9500, 1), BridgeCommand.MoveTo(1, 1, 13400, 9500, 1), Plain());
                Assert.That(FirstMove(plainUp).X.Raw, Is.EqualTo(13107), "without the section");

                SimSession down = Plates(Ml8Worlds.Demo, OnLayer(13400, 9500, 1), BridgeCommand.MoveTo(1, 1, 10600, 9500, 1), Sloped());
                Assert.That(Loco(down).ExplainStep(Id(down), Dir(-1, 0), SimFix.FromRaw(13107)).Receipt.Drags[1], Is.EqualTo(800));
                Assert.That(FirstMove(down).X.Raw, Is.EqualTo(-10485));

                SimSession top = Plates(Ml8Worlds.Demo, OnLayer(13700, 9500, 1), BridgeCommand.MoveTo(1, 1, 13700, 9500, 1), Sloped());
                Assert.That(Loco(top).ExplainStep(Id(top), Dir(1, 0), SimFix.FromRaw(13107)).Receipt.Drags[1], Is.EqualTo(1000), "top seam");
                SimSession foot = Plates(Ml8Worlds.Demo, OnLayer(10300, 9500, 1), BridgeCommand.MoveTo(1, 1, 10300, 9500, 1), Sloped());
                Assert.That(Loco(foot).ExplainStep(Id(foot), Dir(-1, 0), SimFix.FromRaw(13107)).Receipt.Drags[1], Is.EqualTo(1000), "foot seam");
            });
        }

        // S13: the TRV-1a door ramp (layer 2, x 10..12 m, z 15..17 m) rises 29493 raw a metre: 14746 over the probe -> 450
        // permille: up along +X (> 300) 700, down along -X (> 400) 800. At 4000 mm/s: 8738 * 700 / 1000 = 6116,
        // 8738 * 800 / 1000 = 6990. The unit stands in the middle (x 11 m), so both probes stay on the ramp.
        [Test]
        public void S13_TheDoorRamp()
        {
            Assert.DoesNotThrow(() =>
            {
                byte[] map = MapBake.Run(Trv1aDemo.DescBytes()).WcMapBytes;
                SimSession up = Plates(map, OnLayer(11000, 15500, 2, 4000), BridgeCommand.MoveTo(1, 1, 11900, 15500, 2), Sloped(), terrain: true);
                Assert.That(Loco(up).ExplainStep(Id(up), Dir(1, 0), SimFix.FromRaw(Base4000)).Receipt.Drags[1], Is.EqualTo(700));
                Assert.That(FirstMove(up).X.Raw, Is.EqualTo(6116));
                SimSession down = Plates(map, OnLayer(11000, 15500, 2, 4000), BridgeCommand.MoveTo(1, 1, 10100, 15500, 2), Sloped(), terrain: true);
                Assert.That(Loco(down).ExplainStep(Id(down), Dir(-1, 0), SimFix.FromRaw(Base4000)).Receipt.Drags[1], Is.EqualTo(800));
                Assert.That(FirstMove(down).X.Raw, Is.EqualTo(-6990));
            });
        }

        // S14: the same dash up a 600 permille ramp with and without the slope section: the same position every tick.
        [Test]
        public void S14_TheDashTakesNoSlope()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 3, Rise600, "mud");
                UnitSpawn[] dasher = { Unit(1, 1500, 1500).WithDash(4000, 6, 60) };
                SimSession sloped = Ai(ramp, Sloped(), dasher), plain = Ai(ramp, Plain(), dasher);
                SimVec3 start = At(sloped);
                Assert.That(Loco(sloped).TryDash(Id(sloped), Dir(1, 0)), Is.True);
                Assert.That(Loco(plain).TryDash(Id(plain), Dir(1, 0)), Is.True);
                for (int t = 0; t < 8; t++)
                {
                    sloped.StepOnce(); plain.StepOnce();
                    Assert.That(At(sloped), Is.EqualTo(At(plain)), "dash tick " + t);
                }
                Assert.That(At(sloped).X.Raw - start.X.Raw, Is.GreaterThan(0), "the dash moved");
            });
        }

        // S21: the receipt's drags and result equal the real step: the AI on a ramp (max(1, Result)), the player axis by axis
        // (each axis's unscaled displacement through the one probe of the move's heading); a flat world with a drag of 500:
        // [1000, 1000, 500] and 4369; off and nothing registered: the old formula; 1000 calls leave the state hash alone.
        [Test]
        public void S21_TheReceiptMatchesTheStep()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 6, Rise600, "mud");
                SimSession ai = Ai(ramp, Sloped(), new[] { Unit(1, 1500, 1500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) });
                bool compared = false;
                for (int t = 0; t < 20 && !compared; t++)
                {
                    StepReceipt r = Loco(ai).ExplainStep(Id(ai), Dir(1, 0), SimFix.FromRaw(Base4000));
                    SimVec3 before = At(ai);
                    ai.StepOnce();
                    long moved = At(ai).X.Raw - before.X.Raw;
                    if (moved == 0) continue;
                    Assert.That(r.Receipt.Drags, Is.EqualTo(new[] { 500, 500 }));
                    Assert.That(moved, Is.EqualTo(Math.Max(1, r.Receipt.Result)));
                    Assert.That(moved, Is.EqualTo(Scaled(Base4000, 500, 500)));
                    compared = true;
                }
                Assert.That(compared, Is.True);

                // Grass (1000), heading (0.6, 0.8): the probe rises 0.6 * 19661 = 11796 raw -> 359 permille uphill -> 700.
                WcMap grass = RampX(20, 6, Rise600, "grass");
                SimSession player = Player(grass, Sloped(), 5500, 1500), plain = Player(grass, Plain(), 5500, 1500);
                SimVec3 whole = PlayerStep(plain, 600, 800);
                StepReceipt rx = Loco(player).ExplainStep(Id(player), Dir(600, 800), SimFix.FromRaw(whole.X.Raw));
                StepReceipt rz = Loco(player).ExplainStep(Id(player), Dir(600, 800), SimFix.FromRaw(whole.Z.Raw));
                SimVec3 d = PlayerStep(player, 600, 800);
                Assert.That(rx.Receipt.Drags, Is.EqualTo(new[] { 1000, 700 }));
                Assert.That(rz.Receipt.Drags, Is.EqualTo(new[] { 1000, 700 }));
                Assert.That(d.X.Raw, Is.EqualTo(rx.Receipt.Result));
                Assert.That(d.Z.Raw, Is.EqualTo(rz.Receipt.Result));
                Assert.That(d.X.Raw, Is.EqualTo(Scaled(whole.X.Raw, 700)));

                WcMap flat = Wp23.Make(18, 5, 0, 4 * S, null, 4 * S);
                SimSession f = SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, new[] { Unit(1, 1500, 2500) },
                    Array.Empty<BridgeCommand>()), WcMapWriter.Write(flat));
                Assert.That(Loco(f).ExplainStep(Id(f), Dir(1, 0), SimFix.FromRaw(Base4000)).Receipt.Result, Is.EqualTo(Base4000), "nothing registered");
                Loco(f).AddStepDrag(new FixedDrag(500));
                StepReceipt flatReceipt = Loco(f).ExplainStep(Id(f), Dir(1, 0), SimFix.FromRaw(Base4000));
                Assert.That(flatReceipt.Blocked, Is.False);
                Assert.That(flatReceipt.Receipt.Drags, Is.EqualTo(new[] { 1000, 1000, 500 }));
                Assert.That(flatReceipt.Receipt.Result, Is.EqualTo(4369));

                SimSession off = Ai(ramp, Plain(), new[] { Unit(1, 1500, 1500) });
                Assert.That(Loco(off).ExplainStep(Id(off), Dir(1, 0), SimFix.FromRaw(Base4000)).Receipt.Result, Is.EqualTo(Scaled(Base4000, 500)), "surface only");

                ulong hash = Hash(ai);
                for (int k = 0; k < 1000; k++) Loco(ai).ExplainStep(Id(ai), Dir(1, 0), SimFix.FromRaw(Base4000));
                Assert.That(Hash(ai), Is.EqualTo(hash));
            });
        }

        // S22: with inertia (9000 mm/s^2, no braking) an AI unit climbs a 600 permille mud ramp from standing: its motion speed
        // equals the plain world's tick by tick, and the step is that speed * 500 * 500 / 10^6 in one division (at least 1).
        [Test]
        public void S22_InertiaThenTheFootStep()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 3, Rise600, "mud");
                SimSession World(ContentPack pack) => SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, new[] { Unit(1, 1500, 1500) },
                        new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) }).WithPack(pack).WithMotion().With(new InertiaOption(9000, 0)),
                    WcMapWriter.Write(ramp));
                SimSession sloped = World(Sloped()), plain = World(Plain());
                int moving = 0;
                for (int t = 0; t < 20; t++)
                {
                    SimVec3 before = At(sloped), plainBefore = At(plain);
                    sloped.StepOnce(); plain.StepOnce();
                    long speed = WarCore.Tests.Motion.InertiaWorlds.State(sloped).Speed.Raw;
                    Assert.That(speed, Is.EqualTo(WarCore.Tests.Motion.InertiaWorlds.State(plain).Speed.Raw), "tick " + t);
                    long step = At(sloped).X.Raw - before.X.Raw;
                    if (speed == 0) { Assert.That(step, Is.EqualTo(0)); continue; }
                    moving++;
                    Assert.That(step, Is.EqualTo(Math.Max(1, Scaled(speed, 500, 500))), "tick " + t);
                    Assert.That(At(plain).X.Raw - plainBefore.X.Raw, Is.EqualTo(Math.Max(1, Scaled(speed, 500))), "plain tick " + t);
                }
                Assert.That(moving, Is.GreaterThan(10));
            });
        }
    }
}
