using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // SURF-1: walking over the new surfaces. Flat 1 m cells, one unit at 4000 mm/s (seed 41 comes from Wp23.Session).
    public sealed class Surf1MovementTests
    {
        private const long S = SimFix.Scale;
        private static ContentPack Pack() => RwrPack.Load(Surf1RulesTests.Layer);

        private static readonly MoverKind[] Movers = { MoverKind.InterimPath, MoverKind.InterimCrowd };

        // A map whose cells all use surface `name` (map surface table: that one name).
        private static WcMap Uniform(int width, int depth, string name) =>
            Wp23.Make(width, depth, 0, 6 * S, null, 4 * S, null, new[] { name }, (x, z) => 0);

        // S3. Hand-computed, not read from the layer. The base step is 4000 mm/s over 30 ticks per second:
        // 4000 * 65536 / (1000 * 30) = 8738.13..., truncated to 8738 raw per tick. A step is base * milli / 1000 truncated
        // (the same formula Wp23MovementTests uses). Thirty steps:
        //   grass    1000: 8738                  -> 262140
        //   water-shallow 500: 4369              -> 131070 (exactly half of grass)
        //   asphalt 1250: 10922 (10922.5)        -> 327660
        //   snow     700: 6116 (6116.6)          -> 183480
        //   sand     800: 6990 (6990.4)          -> 209700
        //   bush     800: 6990                   -> 209700
        //   rock     900: 7864 (7864.2)          -> 235920
        [TestCase("grass", 8738L, 262140L)]
        [TestCase("water-shallow", 4369L, 131070L)]
        [TestCase("asphalt", 10922L, 327660L)]
        [TestCase("snow", 6116L, 183480L)]
        [TestCase("sand", 6990L, 209700L)]
        [TestCase("bush", 6990L, 209700L)]
        [TestCase("rock", 7864L, 235920L)]
        public void WalkingSpeed_FollowsTheSurface(string surface, long stepRaw, long thirtyStepsRaw)
        {
            foreach (MoverKind mover in Movers)
            {
                SimSession s = Wp23.Session(Uniform(18, 3, surface), mover, new[] { Wp23.Unit(1, 1, 1500, 1500, 4000) },
                    new[] { BridgeCommand.MoveTo(1, 1, 16500, 1500) }, Pack());
                bool started = false; int counted = 0; long total = 0;
                for (int tick = 1; tick <= 200 && counted < 30; tick++)
                {
                    long before = Wp23.At(s).X.Raw;
                    s.StepOnce();
                    long dx = Wp23.At(s).X.Raw - before;
                    if (dx == 0 && !started) continue;
                    started = true;
                    Assert.That(dx, Is.EqualTo(stepRaw), surface + " " + mover + " tick " + tick);
                    total += dx; counted++;
                }
                Assert.That(counted, Is.EqualTo(30), surface + " " + mover);
                Assert.That(total, Is.EqualTo(thirtyStepsRaw), surface + " " + mover);
            }
        }

        // The tables a map derives carry each surface's walkability, speed and path cost (hand-written, in the layer's ordinal order).
        // A blocked surface is stored as speed 0 and cost 0. Path cost is what makes a route over shallow water dearer than one over grass.
        [Test]
        public void DerivedTables_CarryTheSurfaceNumbers()
        {
            string[] names = { "asphalt", "bush", "grass", "mud", "road", "rock", "sand", "snow", "water-deep", "water-shallow" };
            int[] cost = { 1, 2, 1, 3, 1, 2, 2, 3, 0, 4 };
            int[] speed = { 1250, 800, 1000, 500, 1250, 900, 800, 700, 0, 500 };
            WcMap map = Wp23.Make(10, 2, 0, 6 * S, null, 4 * S, null, names, (x, z) => (byte)x);
            TerrainTables tables = TerrainDerivation.Derive(map, Pack());
            for (int x = 0; x < names.Length; x++)
                for (int z = 0; z < 2; z++)
                {
                    int key = z * 10 + x;
                    Assert.That(tables.Nav.IsPassable(x, z), Is.EqualTo(names[x] != "water-deep"), names[x]);
                    Assert.That((int)tables.CostAt(key), Is.EqualTo(cost[x]), names[x] + " cost");
                    Assert.That((int)tables.SpeedMilliAt(key), Is.EqualTo(speed[x]), names[x] + " speed");
                }
        }

        // ---- S4: deep water cannot be entered ----

        // 24 x 9 cells; a band of deep water across x 10..12 on every row, except the rows listed in `shallowRows`.
        private const int BandFrom = 10, BandTo = 13, Rows = 9;
        private static readonly string[] BandNames = { "grass", "water-deep", "water-shallow" };

        private static WcMap Band(params int[] shallowRows) => Wp23.Make(24, Rows, 0, 6 * S, null, 4 * S, null, BandNames,
            (x, z) => (byte)(x >= BandFrom && x < BandTo ? (Array.IndexOf(shallowRows, z) >= 0 ? 2 : 1) : 0));

        private static bool InBand(SimVec3 p) => p.X.Raw >= BandFrom * S && p.X.Raw < BandTo * S;

        // (a) The band spans the whole map: the order to the far bank is unreachable; over 600 ticks the unit is never in the band.
        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void DeepBand_NobodyCrossesOrEnters(MoverKind mover)
        {
            SimSession s = Wp23.Session(Band(), mover, new[] { Wp23.Unit(1, 1, 2500, 4500, 4000) },
                new[] { BridgeCommand.MoveTo(1, 1, 20500, 4500) }, Pack());
            long furthest = 0;
            for (int tick = 1; tick <= 600; tick++)
            {
                s.StepOnce();
                SimVec3 p = Wp23.At(s);
                Assert.That(InBand(p) || p.X.Raw > BandTo * S, Is.False, mover + " tick " + tick + " x raw " + p.X.Raw);
                furthest = Math.Max(furthest, p.X.Raw);
            }
            Assert.That(furthest, Is.LessThan(BandFrom * S));
            Assert.That(M3TestWorld.Locomotion(s).HasFailed(M3TestWorld.Unit(s)), Is.True, "the order to the far bank is reported as failed");
        }

        // (b) One shallow row opens in the band: the unit takes it, reaches the far bank, and never stands in deep water.
        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void DeepBand_WithAShallowGap_IsCrossedThroughTheGap(MoverKind mover)
        {
            const int gapRow = 6;
            SimSession s = Wp23.Session(Band(gapRow), mover, new[] { Wp23.Unit(1, 1, 2500, 4500, 4000) },
                new[] { BridgeCommand.MoveTo(1, 1, 20500, 4500) }, Pack());
            bool usedGap = false;
            for (int tick = 1; tick <= 600; tick++)
            {
                s.StepOnce();
                SimVec3 p = Wp23.At(s);
                if (InBand(p))
                {
                    long row = p.Z.Raw / S;
                    Assert.That(row, Is.EqualTo(gapRow), mover + " tick " + tick + " is inside the band outside the gap row");
                    usedGap = true;
                }
            }
            Assert.That(usedGap, Is.True, mover.ToString());
            Assert.That(Wp23.At(s).X.Raw, Is.EqualTo(Wp23.Mm(20500).Raw), mover.ToString());
        }

        // (c) Spawn and reinforcement checks: anchors on deep water are refused, deployment cannot stand there, shallow is fine.
        [Test]
        public void DeepWater_IsRefusedAsSpawnAndAsDeployGround()
        {
            WcMap map = Band(6);
            TerrainTables tables = TerrainDerivation.Derive(map, Pack());
            SimVec3 bank = new SimVec3(Wp23.Mm(2500), default, Wp23.Mm(4500));
            SimVec3 far = new SimVec3(Wp23.Mm(20500), default, Wp23.Mm(4500));
            SimVec3 deep = new SimVec3(Wp23.Mm(11500), default, Wp23.Mm(2500));
            SimVec3 shallow = new SimVec3(Wp23.Mm(11500), default, Wp23.Mm(6500));

            string message = Assert.Throws<InvalidDataException>(() => TerrainAnchors.Validate(tables, new[] { deep }, new[] { far })).Message;
            Assert.That(message, Does.StartWith("TERRAIN_ANCHOR_BLOCKED"));
            Assert.DoesNotThrow(() => TerrainAnchors.Validate(tables, new[] { shallow }, new[] { far }));
            Assert.DoesNotThrow(() => TerrainAnchors.Validate(tables, new[] { bank }, new[] { far }), "the shallow row connects the banks");

            var ground = new DeployGround(tables.Nav, tables, 0);
            Assert.That(ground.CanStand(deep), Is.False);
            Assert.That(ground.CanStand(shallow), Is.True);
            Assert.That(ground.CanStand(bank), Is.True);

            TerrainTables closed = TerrainDerivation.Derive(Band(), Pack());
            Assert.That(Assert.Throws<InvalidDataException>(() => TerrainAnchors.Validate(closed, new[] { bank }, new[] { far })).Message,
                Does.StartWith("TERRAIN_UNREACHABLE"));
        }

        // ---- S8: determinism over every walkable surface ----

        // All nine walkable surfaces, four cells wide each, side by side on a 36 x 10 map; ten soldiers in two squads run east and
        // back west. Surface names are listed in the layer's own (ordinal) order, one cell column group per surface.
        private static readonly string[] Walkable =
            { "asphalt", "bush", "grass", "mud", "road", "rock", "sand", "snow", "water-shallow" };

        private static SimSession Field()
        {
            WcMap map = Wp23.Make(36, 10, 0, 6 * S, null, 4 * S, null, Walkable, (x, z) => (byte)(x / 4));
            var units = new UnitSpawn[10];
            for (int i = 0; i < units.Length; i++)
                units[i] = Wp23.Unit((ulong)(i + 1), i % 2 + 1, 500 + 700 * (i / 2), 1500 + 700 * i, 4000 + 250 * i);
            var script = new[]
            {
                BridgeCommand.MoveTo(1, 1, 33500, 4500), BridgeCommand.MoveTo(1, 2, 33500, 7500),
                BridgeCommand.MoveTo(150, 1, 1500, 4500), BridgeCommand.MoveTo(150, 2, 1500, 7500),
            };
            return Wp23.Session(map, MoverKind.InterimCrowd, units, script, Pack());
        }

        [Test]
        public void MixedField_SameSeedGivesTheSameHashEveryTick()
        {
            SimSession a = Field(), b = Field();
            var seen = new HashSet<ulong>();
            Assert.That(a.StateHash, Is.EqualTo(b.StateHash), "tick 0");
            for (int tick = 1; tick <= 300; tick++)
            {
                a.StepOnce(); b.StepOnce();
                Assert.That(b.StateHash, Is.EqualTo(a.StateHash), "tick " + tick);
                seen.Add(a.StateHash);
            }
            Assert.That(seen.Count, Is.GreaterThan(100), "the soldiers actually move");
        }

        [Test]
        public void MixedField_SnapshotAtTick150ResumesToTheSameRun()
        {
            SimSession whole = Field(), a = Field();
            for (int i = 0; i < 150; i++) { whole.StepOnce(); a.StepOnce(); }
            byte[] snapshot = M3TestWorld.Sim(a).SaveRetainedSnapshot();
            SimSession b = Field();
            M3TestWorld.Sim(b).RestoreRetainedSnapshot(snapshot);
            Assert.That(b.StateHash, Is.EqualTo(whole.StateHash), "tick 150");
            for (int tick = 151; tick <= 300; tick++)
            {
                whole.StepOnce(); b.StepOnce();
                Assert.That(b.StateHash, Is.EqualTo(whole.StateHash), "tick " + tick);
            }
        }
    }
}
