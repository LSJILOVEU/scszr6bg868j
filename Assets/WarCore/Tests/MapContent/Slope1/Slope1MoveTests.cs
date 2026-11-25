using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Slope1;

namespace WarCore.Tests
{
    // SLOPE-1: AI steps on slopes (S3-S7, S9, S19, S20, S24-S27). Base 8738 raw (4000 mm/s) unless a test says otherwise.
    // A ramp rising 39322 raw per metre along +X: the probe 0.5 m ahead rises 19661 raw -> floor(19661000 / 32768) = 600 permille:
    // uphill band (> 500) 500, downhill band (> 400) 800. A ramp of 26214 per metre: 13107 -> 399 permille: uphill (> 300) 700.
    public sealed class Slope1MoveTests
    {
        private static readonly MoverKind[] Movers = { MoverKind.InterimPath, MoverKind.InterimCrowd };

        private static SimSession Walker(WcMap map, ContentPack pack, long fromXMm, long toXMm, long zMm = 1500, long speed = 4000,
            MoverKind mover = MoverKind.InterimCrowd) =>
            Ai(map, pack, new[] { Unit(1, fromXMm, zMm, speed) }, new[] { BridgeCommand.MoveTo(1, 1, toXMm, zMm) }, mover);

        // S3: up 8738 * 500 / 1000 = 4369; down 8738 * 800 / 1000 = 6990.4 -> 6990.
        [TestCaseSource(nameof(Movers))]
        public void S3_UphillAndDownhillDiffer(MoverKind mover)
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 3, Rise600);
                Assert.That(FirstMove(Walker(ramp, Sloped(), 1500, 16500, mover: mover)).X.Raw, Is.EqualTo(4369));
                Assert.That(FirstMove(Walker(ramp, Sloped(), 16500, 1500, mover: mover)).X.Raw, Is.EqualTo(-6990));
                // The same ramp without the section: the old surface-only step both ways.
                Assert.That(FirstMove(Walker(ramp, Plain(), 1500, 16500, mover: mover)).X.Raw, Is.EqualTo(Base4000));
                Assert.That(FirstMove(Walker(ramp, Plain(), 16500, 1500, mover: mover)).X.Raw, Is.EqualTo(-Base4000));
            });
        }

        // S4: along the contour of a ramp rising along X the step is the flat one; on flat mud the sloped pack equals rwr-field;
        // 0.3 m before the foot of a 600 permille ramp the 0.5 m probe already climbs 0.2 m * 39322 = 7864 raw -> 239 -> 900
        // (a probe one step long, 8738 raw, would still be on the flat: 1000): 8738 * 900 / 1000 = 7864.
        [Test]
        public void S4_ContourFlatMudAndTheFoot()
        {
            Assert.DoesNotThrow(() =>
            {
                SimSession contour = Ai(Map(5, 20, (i, j) => Rise600 * i), Sloped(), new[] { Unit(1, 2500, 1500) },
                    new[] { BridgeCommand.MoveTo(1, 1, 2500, 16500) });
                Assert.That(FirstMove(contour).Z.Raw, Is.EqualTo(Base4000));

                WcMap mud = Map(18, 3, (i, j) => 0, "mud");
                long sloped = FirstMove(Walker(mud, Sloped(), 1500, 16500)).X.Raw;
                long field = FirstMove(Walker(mud, WarCore.Content.Rwr.RwrPack.Load("rwr-field"), 1500, 16500)).X.Raw;
                Assert.That(sloped, Is.EqualTo(4369));
                Assert.That(field, Is.EqualTo(4369));

                WcMap foot = Map(20, 3, (i, j) => i <= 10 ? 0 : Rise600 * (i - 10));
                SimSession s = Walker(foot, Sloped(), 9700, 16500);
                StepReceipt r = Loco(s).ExplainStep(Id(s), Dir(1, 0), SimFix.FromRaw(Base4000));
                Assert.That(r.Receipt.Drags, Is.EqualTo(new[] { 1000, 900 }));
                Assert.That(FirstMove(s).X.Raw, Is.EqualTo(7864));
            });
        }

        // S5: 1000 mm/s = 2184 raw on sand (800) up a 399 permille ramp (700): 2184 * 800 * 700 / 10^6 = 1223.04 -> 1223;
        // dividing twice would give floor(1747 * 700 / 1000) = 1222.
        [Test]
        public void S5_DividesOnce()
        {
            Assert.DoesNotThrow(() =>
            {
                SimSession s = Walker(RampX(20, 3, Rise400, "sand"), Sloped(), 1500, 16500, speed: 1000);
                Assert.That(FirstMove(s).X.Raw, Is.EqualTo(1223));
            });
        }

        // S6: mud 500 x up 500 = 250 permille of the base. Floor 400 lifts it (8738 * 400 / 1000 = 3495.2 -> 3495); floor 250 is
        // not below it (no clamp: 2184.5 -> 2184); floor 200 neither.
        [TestCase(400, 3495L, StatClamp.Floor)]
        [TestCase(250, 2184L, StatClamp.None)]
        [TestCase(200, 2184L, StatClamp.None)]
        public void S6_TheFloorLiftsOnlyBelowIt(int floor, long expected, StatClamp clamp)
        {
            Assert.DoesNotThrow(() =>
            {
                ContentPack pack = Layer("slope-floor", MovementJson + ", " + Stats(floor: floor));
                SimSession s = Walker(RampX(20, 3, Rise600, "mud"), pack, 1500, 16500);
                StepReceipt r = Loco(s).ExplainStep(Id(s), Dir(1, 0), SimFix.FromRaw(Base4000));
                Assert.That(r.Receipt.Drags, Is.EqualTo(new[] { 500, 500 }));
                Assert.That(r.Receipt.Clamp, Is.EqualTo(clamp));
                Assert.That(r.Receipt.Result, Is.EqualTo(expected));
                Assert.That(FirstMove(s).X.Raw, Is.EqualTo(expected));
            });
        }

        // S7: crouched (the stance gear, 500, acts before inertia and before the foot step) on flat grass under a floor of 800:
        // the step is 8738 * 500 / 1000 = 4369; clamping the gear would give 8738 * 800 / 1000 = 6990.
        [Test]
        public void S7_TheFloorDoesNotLiftTheStanceGear()
        {
            Assert.DoesNotThrow(() =>
            {
                ContentPack pack = Layer("slope-stance", Stats(floor: 800), "rwr-stance");
                var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { Unit(1, 1500, 1500) },
                        new[] { BridgeCommand.Stance(1, 1, 1), BridgeCommand.MoveTo(20, 1, 16500, 1500) })
                    .WithCombat(pack.BuildCombat()).WithPack(pack);
                SimSession s = SimSession.Create(recipe, WcMapWriter.Write(Map(18, 3, (i, j) => 0, "grass")));
                for (int t = 0; t < 19; t++) s.StepOnce();
                FirstMove(s);
                for (int t = 0; t < 5; t++)
                {
                    long before = At(s).X.Raw;
                    s.StepOnce();
                    Assert.That(At(s).X.Raw - before, Is.EqualTo(4369), "tick " + s.Tick);
                }
            });
        }

        // S9: the probe cannot read a height: past the map edge, on a map without a height field. Both are 1000.
        [Test]
        public void S9_NoHeightIsNoSlope()
        {
            Assert.DoesNotThrow(() =>
            {
                SimSession edge = Walker(RampX(8, 3, Rise600), Sloped(), 7700, 1500);
                Assert.That(Loco(edge).ExplainStep(Id(edge), Dir(1, 0), SimFix.FromRaw(Base4000)).Receipt.Drags, Is.EqualTo(new[] { 1000, 1000 }));
                Assert.That(Loco(edge).ExplainStep(Id(edge), Dir(-1, 0), SimFix.FromRaw(Base4000)).Receipt.Drags, Is.EqualTo(new[] { 1000, 800 }));

                WcMap surfaces = Map(18, 3, null, "mud");
                SimSession s = Walker(surfaces, Sloped(), 1500, 16500);
                Assert.That(Loco(s).ExplainStep(Id(s), Dir(1, 0), SimFix.FromRaw(Base4000)).Receipt.Drags, Is.EqualTo(new[] { 500, 1000 }));
                Assert.That(FirstMove(s).X.Raw, Is.EqualTo(4369));
            });
        }

        // S19: a unit put on a blocked cell (deep water, surface factor 0) steps exactly as without the slope section: the AI's
        // one raw minimum, the player's nothing. Nothing throws (the stat line refuses a factor of 0), and the receipt says Blocked.
        [Test]
        public void S19_BlockedGroundKeepsTheOldBranch()
        {
            Assert.DoesNotThrow(() =>
            {
                string[] names = { "grass", "water-deep" };
                WcMap map = Wp23.Make(18, 5, 0, 20 * S, null, 4 * S, (i, j) => 0, names, (x, z) => (byte)(x == 8 && z == 2 ? 1 : 0));
                foreach (MoverKind mover in Movers)
                {
                    SimSession on = Ai(map, Sloped(), new[] { Unit(1, 3500, 2500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 2500) }, mover);
                    SimSession off = Ai(map, Plain(), new[] { Unit(1, 3500, 2500) }, new[] { BridgeCommand.MoveTo(1, 1, 16500, 2500) }, mover);
                    foreach (SimSession s in new[] { on, off }) { s.StepOnce(); Teleport(s, Wp23.V(8500, 0, 2500)); }
                    Assert.That(Loco(on).ExplainStep(Id(on), Dir(1, 0), SimFix.FromRaw(Base4000)).Blocked, Is.True);
                    for (int t = 0; t < 3; t++)
                    {
                        Assert.DoesNotThrow(() => on.StepOnce());
                        off.StepOnce();
                        Assert.That(At(on), Is.EqualTo(At(off)), mover + " tick " + on.Tick);
                    }
                }
                SimSession player = Player(map, Sloped(), 3500, 2500);
                Teleport(player, Wp23.V(8500, 0, 2500));
                SimVec3 before = At(player);
                Assert.DoesNotThrow(() => PlayerStep(player, 1000, 0));
                Assert.That(At(player), Is.EqualTo(before));
            });
        }

        // S20: base 0.25 m (7500 mm/s = 16384 raw), surface 2000, flat ground, floor 1000: exactly 0.5 m a tick, no throw.
        [Test]
        public void S20_LargestStepIsHalfAMetre()
        {
            Assert.DoesNotThrow(() =>
            {
                ContentPack fast = Layer("slope-fast", "\"surfaces\": [ { \"id\": \"grass\", \"speedMilli\": 2000 } ], " + MovementJson + ", " + Stats(floor: 1000));
                SimSession s = Walker(Map(30, 3, (i, j) => 0, "grass"), fast, 1500, 26500, speed: 7500);
                Assert.That(FirstMove(s).X.Raw, Is.EqualTo(32768));
                for (int t = 0; t < 5; t++)
                {
                    long before = At(s).X.Raw;
                    Assert.DoesNotThrow(() => s.StepOnce());
                    Assert.That(At(s).X.Raw - before, Is.EqualTo(32768));
                }
            });
        }

        // S24: stats.speed alone (no slope, no drag) turns the foot step on: mud 500 lifted by a floor of 800 -> 6990; road 1250
        // pressed by a ceiling of 1100 -> 8738 * 1100 / 1000 = 9611.8 -> 9611. The AI and the player alike.
        [TestCase("mud", 800, 0, 6990L)]
        [TestCase("road", 0, 1100, 9611L)]
        public void S24_StatsSpeedAloneClampsTheSurface(string surface, int floor, int ceil, long expected)
        {
            Assert.DoesNotThrow(() =>
            {
                ContentPack pack = Layer("slope-stats", Stats(floor, ceil));
                WcMap map = Map(18, 3, (i, j) => 0, surface);
                Assert.That(FirstMove(Walker(map, pack, 1500, 16500)).X.Raw, Is.EqualTo(expected));
                Assert.That(PlayerStep(Player(map, pack, 1500, 1500), 1000, 0).X.Raw, Is.EqualTo(expected));
            });
        }

        // S25: only a registered drag on a terrain world: mud 500 x drag 500 -> 8738 * 500 * 500 / 10^6 = 2184.5 -> 2184.
        [Test]
        public void S25_TerrainWorldWithOnlyARegisteredDrag()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap map = Map(18, 3, (i, j) => 0, "mud");
                SimSession ai = Walker(map, Plain(), 1500, 16500);
                Loco(ai).AddStepDrag(new FixedDrag(500));
                Assert.That(FirstMove(ai).X.Raw, Is.EqualTo(2184));
                SimSession player = Player(map, Plain(), 1500, 1500);
                Loco(player).AddStepDrag(new FixedDrag(500));
                Assert.That(PlayerStep(player, 1000, 0).X.Raw, Is.EqualTo(2184));
                Assert.That(PlayerStep(player, -1000, 0).X.Raw, Is.EqualTo(-2184));
            });
        }

        // S26: 1 mm/s = 2 raw x mud 500 x up 500 = 0.5 -> 0, lifted to 1 raw. With the path mover the lift is what makes a unit one
        // raw short of its target arrive (and stop) this tick: a 0 step would still reach the point but leave the order on.
        [Test]
        public void S26_TheFootStepKeepsOneRaw()
        {
            Assert.DoesNotThrow(() =>
            {
                WcMap ramp = RampX(20, 3, Rise600, "mud");
                SimSession s = Walker(ramp, Sloped(), 1500, 16500, speed: 1);
                Assert.That(Loco(s).ExplainStep(Id(s), Dir(1, 0), SimFix.FromRaw(2)).Receipt.Result, Is.EqualTo(0));
                Assert.That(FirstMove(s).X.Raw, Is.EqualTo(1));

                SimSession path = Walker(ramp, Sloped(), 1500, 16500, speed: 1, mover: MoverKind.InterimPath);
                path.StepOnce();
                SimVec3 target = Wp23.Intent(path).Target;
                Assert.That(M3TestWorld.Component<MoveWaypoint>(path).Point, Is.EqualTo(target));
                Teleport(path, new SimVec3(SimFix.FromRaw(target.X.Raw - 1), target.Y, target.Z));
                path.StepOnce();
                Assert.That(At(path).X.Raw, Is.EqualTo(target.X.Raw));
                Assert.That(Wp23.Intent(path).Active, Is.False);
            });
        }

        // S27 (AI): a crowd unit blocked by a standing unit 0.55 m ahead sidesteps with the step of its own heading (+X, up 600
        // permille: 4369), not one probed along the sidestep (across the ramp: 1000, 8738). The side is -Z (radial -X, zero dot).
        [Test]
        public void S27_TheSidestepKeepsTheHeadingsBand()
        {
            Assert.DoesNotThrow(() =>
            {
                SimSession s = Ai(RampX(20, 5, Rise600), Sloped(), new[] { Unit(1, 3000, 2500), Unit(2, 3550, 2500) },
                    new[] { BridgeCommand.MoveTo(1, 1, 16500, 2500) });
                SimVec3 d = FirstMove(s);
                Assert.That(d.X.Raw, Is.EqualTo(0));
                Assert.That(d.Z.Raw, Is.EqualTo(-4369));
            });
        }

        internal static void Teleport(SimSession s, SimVec3 to, ulong key = 1)
        {
            EntityStore store = M3TestWorld.Sim(s).Entities;
            Position position = store.Get<Position>(Id(s, key));
            position.Value = to;
            store.Set(Id(s, key), position);
        }
    }
}
