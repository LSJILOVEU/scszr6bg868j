using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Slope1;

namespace WarCore.Tests
{
    // SLOPE-1: the player's foot step (S8, S16, S20, S27) and the registration point of drags (S15, S17).
    public sealed class Slope1PlayerTests
    {
        // S8: on one 600 permille ramp, heading +X, the player (key 1) and an AI unit (key 2) read the same slope factor (500);
        // both steps are 8738 * 500 / 1000 = 4369 (the player by axis, the AI along its heading). Heading -X: 800, 6990.
        [Test]
        public void S8_PlayerAndAiTakeTheSameBand()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 6, Rise600);
                SimSession up = Player(ramp, Sloped(), 3500, 1500, extra: new[] { new UnitSpawn(2, 0, 2, 3500, 4500, 4000, 1) });
                up.Enqueue(BridgeCommand.MoveTo(1, 2, 16500, 4500));
                StepReceipt player = Loco(up).ExplainStep(Id(up, 1), Dir(1, 0), SimFix.FromRaw(Base4000));
                StepReceipt ai = Loco(up).ExplainStep(Id(up, 2), Dir(1, 0), SimFix.FromRaw(Base4000));
                Assert.That(player.Receipt.Drags[1], Is.EqualTo(500));
                Assert.That(ai.Receipt.Drags[1], Is.EqualTo(500));
                SimVec3 p0 = At(up, 1), a0 = At(up, 2);
                up.SetPlayerInput(1, new PlayerInput(1000, 0));
                up.StepOnce();
                Assert.That(At(up, 1).X.Raw - p0.X.Raw, Is.EqualTo(4369));
                Assert.That(At(up, 2).X.Raw - a0.X.Raw, Is.EqualTo(4369));

                SimSession down = Player(ramp, Sloped(), 15500, 1500);
                Assert.That(Loco(down).ExplainStep(Id(down), Dir(-1, 0), SimFix.FromRaw(Base4000)).Receipt.Drags[1], Is.EqualTo(800));
                Assert.That(PlayerStep(down, -1000, 0).X.Raw, Is.EqualTo(-6990));
            });
        }

        // S16: drag 500 x sand 800 x up 700 (399 permille), one division: 8738 * 500 * 800 * 700 / 10^9 = 2446.6 -> 2446, for the
        // player (multiplied once: twice would give 1223) and the AI.
        [Test]
        public void S16_TerrainDragIsTakenOnce()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 3, Rise400, "sand");
                SimSession player = Player(ramp, Sloped(), 1500, 1500);
                var drag = new FixedDrag(500);
                Loco(player).AddStepDrag(drag);
                Assert.That(PlayerStep(player, 1000, 0).X.Raw, Is.EqualTo(2446));
                Assert.That(drag.Calls, Is.EqualTo(1), "one step, one read");

                SimSession ai = Ai(ramp, Sloped(), new[] { Unit(1, 1500, 1500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) });
                Loco(ai).AddStepDrag(new FixedDrag(500));
                Assert.That(FirstMove(ai).X.Raw, Is.EqualTo(2446));
            });
        }

        // S20 (player): a gear above 1000 before the foot step (a fake one: today's stance gear is at most 1000) makes a player
        // axis longer than 0.25 m; the foot step refuses it. Without the step line the old branch moves 0.5 m as before.
        [Test]
        public void S20_PlayerAxisOverAQuarterMetreThrowsOnTheStepLine()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap flat = Map(30, 3, (i, j) => 0, "grass");
                SimSession on = SimSession.Create(PlayerRecipe(flat, Sloped(), 1500, 1500, 7500).With(new DoubleGear()), WcMapWriter.Write(flat));
                on.SetPlayerInput(1, new PlayerInput(1000, 0));
                Assert.Throws<InvalidOperationException>(() => on.StepOnce());

                SimSession off = SimSession.Create(PlayerRecipe(flat, Plain(), 1500, 1500, 7500).With(new DoubleGear()), WcMapWriter.Write(flat));
                Assert.That(PlayerStep(off, 1000, 0).X.Raw, Is.EqualTo(32768));

                // At exactly 0.25 m (no fake gear) the step line takes it: grass 1000, flat: 16384.
                Assert.That(PlayerStep(Player(flat, Sloped(), 1500, 1500, speed: 7500), 1000, 0).X.Raw, Is.EqualTo(16384));
            });
        }

        // S27 (player): a ramp rising 39322 per metre along +Z, a wall row at z 6..7. The player at z 5.7 m holds (+X, +Z): the
        // probe along that heading rises 23170 * 39322 / 65536 = 13902 raw -> 424 permille -> 700. The whole step would put the
        // footprint into the wall, so the X-only candidate is taken with the band of the original heading: floor(x * 700 / 1000)
        // of the unscaled X displacement (a probe along +X alone would give 1000, the whole X).
        [Test]
        public void S27_XOnlyKeepsTheOriginalBand()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap map = Wp23.Make(16, 16, 0, 20 * S, new[] { (0, 6, 16, 1) }, 12 * S, (i, j) => Rise600 * j);
                SimSession plain = Player(map, Plain(), 5000, 5700);
                SimVec3 unscaled = PlayerStep(plain, 707, 707);
                Assert.That(unscaled.Z.Raw, Is.EqualTo(0), "the wall leaves X only");
                SimSession sloped = Player(map, Sloped(), 5000, 5700);
                SimVec3 d = PlayerStep(sloped, 707, 707);
                Assert.That(d.Z.Raw, Is.EqualTo(0));
                Assert.That(d.X.Raw, Is.EqualTo(unscaled.X.Raw * 700 / 1000));
            });
        }

        // S15: a flat world (no height field, no surface table) with a drag of 500 registered: the three AI movers step half
        // (8738 -> 4369), the player half per axis, the flat dash not at all; 1 mm/s (2 raw) with a drag of 1 stays 1 raw.
        [Test]
        public void S15_FlatWorldDrags()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap flat = Wp23.Make(18, 5, 0, 4 * S, null, 4 * S);
                foreach (MoverKind mover in new[] { MoverKind.Interim, MoverKind.InterimPath, MoverKind.InterimCrowd })
                {
                    SimSession half = FlatAi(flat, mover, 4000);
                    Loco(half).AddStepDrag(new FixedDrag(500));
                    Assert.That(FirstMove(half).X.Raw, Is.EqualTo(4369), mover.ToString());
                    SimSession slow = FlatAi(flat, mover, 1);
                    Loco(slow).AddStepDrag(new FixedDrag(1));
                    Assert.That(FirstMove(slow).X.Raw, Is.EqualTo(1), mover + " one raw");
                    SimSession none = FlatAi(flat, mover, 4000);
                    Assert.That(FirstMove(none).X.Raw, Is.EqualTo(Base4000), mover + " nothing registered");
                }

                SimSession player = Player(flat, Plain(), 1500, 2500);
                Loco(player).AddStepDrag(new FixedDrag(500));
                Assert.That(PlayerStep(player, 1000, 0).X.Raw, Is.EqualTo(4369));
                SimVec3 diagonal = PlayerStep(player, -600, 800);
                SimSession twin = Player(flat, Plain(), 1500, 2500);
                PlayerStep(twin, 1000, 0);
                SimVec3 whole = PlayerStep(twin, -600, 800);
                Assert.That((diagonal.X.Raw, diagonal.Z.Raw), Is.EqualTo((-((-whole.X.Raw) * 500 / 1000), whole.Z.Raw * 500 / 1000)));

                var dashers = new[] { new UnitSpawn(1, 0, 1, 3500, 2500, 6000, 1).WithDash(4000, 6, 60) };
                SimSession dragged = SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, dashers, Array.Empty<BridgeCommand>()), WcMapWriter.Write(flat));
                SimSession free = SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, dashers, Array.Empty<BridgeCommand>()), WcMapWriter.Write(flat));
                Loco(dragged).AddStepDrag(new FixedDrag(500));
                Assert.That(Loco(dragged).TryDash(Id(dragged), Dir(1, 0)), Is.True);
                Assert.That(Loco(free).TryDash(Id(free), Dir(1, 0)), Is.True);
                for (int t = 0; t < 8; t++)
                {
                    dragged.StepOnce(); free.StepOnce();
                    Assert.That(At(dragged), Is.EqualTo(At(free)), "dash tick " + t);
                }
            });
        }

        // S17: a drag outside 1..1000 throws on that step; five drags fit (flat and terrain), the sixth and null do not; the
        // step line refuses clamps outside its ranges.
        [Test]
        public void S17_DragRangeAndCount()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap flat = Wp23.Make(18, 5, 0, 4 * S, null, 4 * S);
                WcMap ramp = RampX(20, 3, Rise600);
                foreach (int bad in new[] { 0, 1001 })
                {
                    SimSession f = FlatAi(flat, MoverKind.InterimCrowd, 4000);
                    Loco(f).AddStepDrag(new FixedDrag(bad));
                    Assert.Throws<InvalidOperationException>(() => { for (int t = 0; t < 3; t++) f.StepOnce(); }, "flat " + bad);
                    SimSession r = Ai(ramp, Sloped(), new[] { Unit(1, 1500, 1500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) });
                    Loco(r).AddStepDrag(new FixedDrag(bad));
                    Assert.Throws<InvalidOperationException>(() => { for (int t = 0; t < 3; t++) r.StepOnce(); }, "terrain " + bad);
                }
                foreach (SimSession s in new[] { FlatAi(flat, MoverKind.InterimCrowd, 4000),
                             Ai(ramp, Sloped(), new[] { Unit(1, 1500, 1500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) }) })
                {
                    Assert.DoesNotThrow(() => { for (int k = 0; k < InterimLocomotion.MaxStepDrags; k++) Loco(s).AddStepDrag(new FixedDrag(1000)); });
                    Assert.That(InterimLocomotion.MaxStepDrags, Is.EqualTo(5));
                    Assert.Throws<InvalidOperationException>(() => Loco(s).AddStepDrag(new FixedDrag(1000)));
                    Assert.DoesNotThrow(() => FirstMove(s));
                }
                Assert.Throws<ArgumentNullException>(() => Loco(FlatAi(flat, MoverKind.InterimCrowd, 4000)).AddStepDrag(null));
                // The step line's own clamps: floor 0 or 1..1000, ceiling 0 or 1000..4000.
                InterimLocomotion fresh = Loco(FlatAi(flat, MoverKind.InterimCrowd, 4000));
                foreach ((int floor, int ceil) in new[] { (-1, 0), (1001, 0), (0, 999), (0, 4001) })
                    Assert.Throws<ArgumentOutOfRangeException>(() => fresh.SetStepLine(null, floor, ceil), floor + " " + ceil);
                fresh.SetStepLine(null, 1, 1000);
                fresh.SetStepLine(null, 1000, 4000);
                Assert.That(fresh.StepFloorPermille, Is.EqualTo(1000));
                Assert.That(fresh.StepCeilPermille, Is.EqualTo(4000));
            });
        }

        private static SimSession FlatAi(WcMap flat, MoverKind mover, long speed) =>
            SimSession.Create(new WorldRecipe(41, mover, new[] { Unit(1, 1500, 2500, speed) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 2500) }),
                WcMapWriter.Write(flat));

        // A test extension: a speed line with one gear of 2000 in front of the motion model and the player's step.
        private sealed class DoubleGear : WorldStageFeature
        {
            public override string OptionId => "test.slope1-double-gear";
            public override void RegisterRules(WorldAssembly a)
            {
                var line = new SpeedLine(a.Locomotion.Motion);
                line.AddGear(new Gear());
                a.Locomotion.BindMotion(line);
                a.PlayerStep?.BindSpeed(line);
            }

            private sealed class Gear : ISpeedGear
            {
                public int Permille(EntityId unit) => 2000;
            }
        }
    }
}
