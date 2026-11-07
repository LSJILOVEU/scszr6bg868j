using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp23MovementTests
    {
        private const long S = Wp23.S;

        // ---- every unit stands on the ground, every tick ----

        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void DemoTerrain_UnitsStayOnTheGround_EveryTick(MoverKind mover)
        {
            WcMap map = TerrainDemoMap.Build();
            TerrainTables tables = TerrainDerivation.Derive(map, Wp23.Pack());
            HeightField ground = tables.Ground;
            var units = new[] { Wp23.Unit(1, 1, 3500, 16500), Wp23.Unit(2, 2, 44500, 16500), Wp23.Unit(3, 1, 3500, 15500) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 44500, 16500), BridgeCommand.MoveTo(2, 2, 3500, 16500) };
            SimSession s = Wp23.Session(map, mover, units, script, Wp23.Pack());
            bool climbed = false;
            var previous = new Dictionary<ulong, SimVec3>();
            for (int tick = 0; tick <= 600; tick++)
            {
                if (tick != 0) s.StepOnce();
                foreach (ulong key in new ulong[] { 1, 2, 3 })
                {
                    SimVec3 p = Wp23.At(s, key);
                    Assert.That(ground.TryHeightAt(p.X, p.Z, out SimFix y), Is.True, $"tick {tick} unit {key} left the map");
                    Assert.That(p.Y.Raw, Is.EqualTo(y.Raw), $"tick {tick} unit {key} is not on the ground");
                    climbed |= y.Raw > 3 * S;
                    if (previous.TryGetValue(key, out SimVec3 last))
                        Assert.That(tables.Nav.SegmentClear(last, p, WarCore.Interim.InterimLocomotion.UnitFootprint), Is.True, $"tick {tick} unit {key} overlaps a blocked cell");
                    previous[key] = p;
                }
            }
            Assert.That(climbed, Is.True, "the walkers must actually cross the ridge");
            Assert.That(Wp23.At(s, 1).X.Raw, Is.GreaterThan(30 * S));
        }

        [Test]
        public void OrderOutsideTheMap_StopsTheUnit_AndATargetInsideIsSnappedToTheGround()
        {
            WcMap map = TerrainDemoMap.Build();
            HeightField ground = TerrainDerivation.Derive(map, Wp23.Pack()).Ground;
            var script = new[] { BridgeCommand.MoveTo(1, 1, 60000, 5000), BridgeCommand.MoveTo(40, 2, 10500, 16500) };
            SimSession s = Wp23.Session(map, MoverKind.InterimCrowd, new[] { Wp23.Unit(1, 1, 3500, 5500), Wp23.Unit(2, 2, 3500, 16500) }, script, Wp23.Pack());
            SimVec3 start = Wp23.At(s, 1);
            for (int i = 0; i < 30; i++) s.StepOnce();
            Assert.That(Wp23.At(s, 1), Is.EqualTo(start));
            Assert.That(Wp23.Intent(s, 1).Active, Is.False);
            for (int i = 0; i < 40; i++) s.StepOnce();
            MoveIntent intent = Wp23.Intent(s, 2);
            Assert.That(intent.Active, Is.True);
            ground.TryHeightAt(intent.Target.X, intent.Target.Z, out SimFix y);
            Assert.That(intent.Target.Y.Raw, Is.EqualTo(y.Raw), "an order's target Y is the ground under it");
            Assert.That(y.Raw, Is.GreaterThan(0));
        }

        // ---- surface speed ----

        private static WcMap Stripes(int width = 24) => Wp23.Make(width, 6, 0, 6 * S, null, 4 * S, null, Wp23.Names,
            (x, z) => (byte)(x < 8 ? 0 : x < 16 ? 1 : 2));

        private static long Milli(int cell) => cell < 8 ? 1000 : cell < 16 ? 500 : 1250;

        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void StepLength_FollowsTheSurfaceUnderTheUnitAtTheStartOfTheTick(MoverKind mover)
        {
            SimSession s = Wp23.Session(Stripes(), mover, new[] { Wp23.Unit(1, 1, 500, 2500, 4000) },
                new[] { BridgeCommand.MoveTo(1, 1, 22500, 2500) }, Wp23.Pack());
            long baseStep = Wp23.Step(4000), targetX = Wp23.Mm(22500).Raw;
            bool started = false; int moved = 0; var seen = new HashSet<long>();
            for (int tick = 1; tick <= 400 && Wp23.Intent(s).Active | !started; tick++)
            {
                SimVec3 before = Wp23.At(s);
                s.StepOnce();
                SimVec3 after = Wp23.At(s);
                long dx = after.X.Raw - before.X.Raw;
                Assert.That(after.Z.Raw, Is.EqualTo(before.Z.Raw)); Assert.That(after.Y.Raw, Is.EqualTo(0));
                if (dx == 0 && !started) continue;
                started = true;
                long expected = Math.Min(Math.Max(1, baseStep * Milli((int)(before.X.Raw / S)) / 1000), targetX - before.X.Raw);
                Assert.That(dx, Is.EqualTo(expected), $"tick {tick} from x={before.X.Raw}");
                seen.Add(expected); moved++;
            }
            Assert.That(Wp23.At(s).X.Raw, Is.EqualTo(targetX));
            Assert.That(Wp23.Intent(s).Active, Is.False);
            Assert.That(seen, Contains.Item(baseStep * 500 / 1000), "all three surfaces were crossed");
            Assert.That(seen, Contains.Item(baseStep), "all three surfaces were crossed");
            Assert.That(seen, Contains.Item(baseStep * 1250 / 1000), "all three surfaces were crossed");
            Assert.That(moved, Is.GreaterThan(100));
        }

        [Test]
        public void StepLength_NeverRoundsDownToZero()
        {
            string slow = TerrainDemoMap.PackJson.Replace("\"speedMilli\": 500", "\"speedMilli\": 1");
            Assert.That(slow, Is.Not.EqualTo(TerrainDemoMap.PackJson));
            WcMap allMud = Wp23.Make(6, 3, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => 1);
            SimSession s = Wp23.Session(allMud, MoverKind.InterimPath, new[] { Wp23.Unit(1, 1, 500, 1500, 100) },
                new[] { BridgeCommand.MoveTo(1, 1, 4500, 1500) }, Wp23.Pack(slow));
            Assert.That(Wp23.Step(100) * 1 / 1000, Is.Zero, "the product alone would be zero");
            s.StepOnce();
            for (int i = 0; i < 5; i++)
            {
                long before = Wp23.At(s).X.Raw;
                s.StepOnce();
                Assert.That(Wp23.At(s).X.Raw - before, Is.EqualTo(1));
            }
        }

        [TestCase(MoverKind.InterimPath)]
        [TestCase(MoverKind.InterimCrowd)]
        public void LowSpeedDiagonal_ArrivesWithin130Steps_AndNeverStandsStill(MoverKind mover)
        {
            WcMap allMud = Wp23.Make(6, 3, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => 1);
            SimSession s = Wp23.Session(allMud, mover, new[] { Wp23.Unit(1, 1, 500, 500, 1) },
                new[] { BridgeCommand.MoveTo(1, 1, 501, 501) }, Wp23.Pack());
            s.StepOnce();
            SimVec3 goal = new SimVec3(Wp23.Mm(501), default, Wp23.Mm(501));
            int arrived = -1;
            for (int step = 1; step <= 130 && arrived < 0; step++)
            {
                SimVec3 before = Wp23.At(s);
                s.StepOnce();
                SimVec3 after = Wp23.At(s);
                if (!Wp23.Intent(s).Active) arrived = step;
                else Assert.That(after, Is.Not.EqualTo(before), $"step {step} made no progress");
            }
            Assert.That(arrived, Is.GreaterThan(0), "arrived within 130 steps");
            Assert.That(Wp23.At(s).X.Raw, Is.EqualTo(goal.X.Raw));
            Assert.That(Wp23.At(s).Z.Raw, Is.EqualTo(goal.Z.Raw));
        }

        // ---- a terrain world with nothing to say behaves like the flat world ----

        private static IEnumerable<object[]> EquivalenceCases()
        {
            foreach (MoverKind mover in new[] { MoverKind.InterimPath, MoverKind.InterimCrowd })
            {
                yield return new object[] { mover, 0L, 0L, "constant" };
                yield return new object[] { mover, 0L, 5L, "constant" };
                yield return new object[] { mover, 0L, 3 * S / 2 + 17, "constant" };
                yield return new object[] { mover, -2 * S, -S + 3, "constant" };
                yield return new object[] { mover, -2 * S, -2 * S, "constant" };
                yield return new object[] { mover, 0L, 0L, "slope" };
                yield return new object[] { mover, -S, -S, "slope" };
            }
        }

        [TestCaseSource(nameof(EquivalenceCases))]
        public void ConstantAndGentleTerrain_MoveLikeTheFlatMap(MoverKind mover, long minY, long c, string shape)
        {
            var boxes = new[] { (10, 4, 2, 12), (20, 0, 2, 14), (20, 18, 2, 6), (30, 6, 2, 16) };
            WcMap flat = Wp23.Make(40, 24, minY, minY + 6 * S, boxes, 4 * S);
            Func<int, int, int> heights = shape == "constant" ? (Func<int, int, int>)((i, j) => (int)c) : (i, j) => (int)(minY + i * (S / 16));
            WcMap terrain = Wp23.Make(40, 24, minY, minY + 6 * S, boxes, 4 * S, heights);
            var units = new[]
            {
                Wp23.Unit(1, 1, 1500, 3500, 4000), Wp23.Unit(2, 1, 1500, 5500, 3000), Wp23.Unit(3, 2, 2500, 12500, 5000),
                Wp23.Unit(4, 2, 2500, 14500, 4000), Wp23.Unit(5, 3, 1500, 20500, 3500), Wp23.Unit(6, 3, 3500, 21500, 4500),
            };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 37500, 3500), BridgeCommand.MoveTo(2, 2, 37500, 13500), BridgeCommand.MoveTo(3, 3, 37500, 21500) };
            SimSession a = Wp23.Session(flat, mover, units, script);
            SimSession b = Wp23.Session(terrain, mover, units, script, Wp23.Pack());
            HeightField ground = TerrainDerivation.Derive(terrain, Wp23.Pack()).Ground;
            Assert.That(b.StateHash, Is.Not.EqualTo(a.StateHash), "the manifest and the locomotion digest differ");
            for (int tick = 0; tick <= 600; tick++)
            {
                if (tick != 0) { a.StepOnce(); b.StepOnce(); }
                for (ulong key = 1; key <= 6; key++)
                {
                    SimVec3 pa = Wp23.At(a, key), pb = Wp23.At(b, key);
                    Assert.That((pb.X.Raw, pb.Z.Raw), Is.EqualTo((pa.X.Raw, pa.Z.Raw)), $"tick {tick} unit {key}");
                    Assert.That(Wp23.Intent(b, key).Active, Is.EqualTo(Wp23.Intent(a, key).Active), $"tick {tick} unit {key} active");
                    Assert.That(pa.Y.Raw, Is.EqualTo(minY));
                    ground.TryHeightAt(pb.X, pb.Z, out SimFix y);
                    Assert.That(pb.Y.Raw, Is.EqualTo(y.Raw));
                    if (shape == "constant") Assert.That(pb.Y.Raw, Is.EqualTo(c));
                }
            }
            Assert.That(Wp23.Intent(a, 1).Active, Is.False, "the walkers arrived, so the comparison covered arrival");
        }

        // Cells blocked by a surface (no wall box) are kept clear by the footprint check, even when a crowd shoves sideways.
        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void BlockedSurfaceCells_HaveNoCollisionBoxButUnitsStayOutOfThem(MoverKind mover)
        {
            string closed = TerrainDemoMap.PackJson.Replace("{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }", "{ \"id\": \"mud\", \"passable\": false }");
            Assert.That(closed, Is.Not.EqualTo(TerrainDemoMap.PackJson));
            // 30 x 9: a two-cell corridor (rows 3-4) between blocked mud rows, entered from the west by a crowd.
            WcMap map = Wp23.Make(30, 9, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => (byte)(z == 3 || z == 4 ? 0 : 1));
            TerrainTables tables = TerrainDerivation.Derive(map, Wp23.Pack(closed));
            var units = new List<UnitSpawn>();
            var script = new List<BridgeCommand>();
            // One unit per squad, so no formation slot lands in a blocked row.
            for (ulong k = 1; k <= 10; k++)
            {
                units.Add(Wp23.Unit(k, (int)k, 1500 + (long)(k % 3) * 600, 3600 + (long)(k % 2) * 800, 5000));
                script.Add(BridgeCommand.MoveTo((int)k, (int)k, 27500, 3500 + (long)(k % 2) * 1000));
            }
            SimSession s = Wp23.Session(map, mover, units.ToArray(), script.ToArray(), Wp23.Pack(closed));
            var last = new SimVec3[11];
            for (ulong k = 1; k <= 10; k++) last[k] = Wp23.At(s, k);
            for (int tick = 1; tick <= 500; tick++)
            {
                s.StepOnce();
                for (ulong k = 1; k <= 10; k++)
                {
                    SimVec3 p = Wp23.At(s, k);
                    Assert.That(tables.Nav.SegmentClear(last[k], p, WarCore.Interim.InterimLocomotion.UnitFootprint), Is.True, $"tick {tick} unit {k}");
                    last[k] = p;
                }
            }
            // Getting through is not asserted: crowd liveness in a corridor is the T1a-2b yield rule's job (see Wp23FailureTests).
        }

        // The same jam on a slope: sidesteps and the ease back to the cell centre are moves too, and every one ends on the ground.
        [Test]
        public void CrowdJamOnASlope_EveryUnitStaysOnTheGround()
        {
            string closed = TerrainDemoMap.PackJson.Replace("{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }", "{ \"id\": \"mud\", \"passable\": false }");
            WcMap map = Wp23.Make(30, 9, 0, 6 * S, null, 4 * S, (i, j) => (int)(i * (S / 8) + j * (S / 16)), Wp23.Names,
                (x, z) => (byte)(z == 3 || z == 4 ? 0 : 1));
            HeightField ground = TerrainDerivation.Derive(map, Wp23.Pack(closed)).Ground;
            var units = new List<UnitSpawn>();
            var script = new List<BridgeCommand>();
            for (ulong k = 1; k <= 12; k++)
            {
                units.Add(Wp23.Unit(k, (int)k, 1500 + (long)(k % 3) * 600, 3600 + (long)(k % 2) * 800, 5000));
                script.Add(BridgeCommand.MoveTo((int)k, (int)k, 27500, 3500 + (long)(k % 2) * 1000));
            }
            SimSession s = Wp23.Session(map, MoverKind.InterimCrowd, units.ToArray(), script.ToArray(), Wp23.Pack(closed));
            for (int tick = 1; tick <= 400; tick++)
            {
                s.StepOnce();
                for (ulong k = 1; k <= 12; k++)
                {
                    SimVec3 p = Wp23.At(s, k);
                    Assert.That(ground.TryHeightAt(p.X, p.Z, out SimFix y), Is.True);
                    Assert.That(p.Y.Raw, Is.EqualTo(y.Raw), $"tick {tick} unit {k}");
                }
            }
        }

        // A wall across the whole map with high ground on both sides: nobody gets across, from either side, however the
        // orders point (the A* finds no route, and the swept step cannot hop a 1 m wall).
        [TestCase(MoverKind.InterimPath), TestCase(MoverKind.InterimCrowd)]
        public void ClosedWallOnRisingGround_IsNeverCrossed_InEitherDirection(MoverKind mover)
        {
            WcMap map = Wp23.Make(16, 8, 0, 6 * S, new[] { (8, 0, 1, 8) }, 5 * S, (i, j) => (int)(Math.Abs(i - 8) * (S / 4)));
            var units = new[] { Wp23.Unit(1, 1, 2500, 2500, 7000), Wp23.Unit(2, 2, 13500, 5500, 7000), Wp23.Unit(3, 3, 7400, 4500, 7000), Wp23.Unit(4, 4, 9600, 3500, 7000) };
            var script = new[]
            {
                BridgeCommand.MoveTo(1, 1, 13500, 2500), BridgeCommand.MoveTo(2, 2, 2500, 5500),
                BridgeCommand.MoveTo(3, 3, 9500, 4500), BridgeCommand.MoveTo(4, 4, 7500, 3500),
            };
            SimSession s = Wp23.Session(map, mover, units, script, Wp23.Pack());
            for (int tick = 0; tick < 300; tick++)
            {
                s.StepOnce();
                Assert.That(Wp23.At(s, 1).X.Raw, Is.LessThan(8 * S), $"tick {tick}");
                Assert.That(Wp23.At(s, 3).X.Raw, Is.LessThan(8 * S), $"tick {tick}");
                Assert.That(Wp23.At(s, 2).X.Raw, Is.GreaterThanOrEqualTo(9 * S), $"tick {tick}");
                Assert.That(Wp23.At(s, 4).X.Raw, Is.GreaterThanOrEqualTo(9 * S), $"tick {tick}");
            }
        }

        [Test]
        public void SurfaceOnlyMap_AppliesSurfaceRules_AndLeavesYUntouched()
        {
            SimSession s = Wp23.Session(Stripes(), MoverKind.InterimCrowd, new[] { Wp23.Unit(1, 1, 500, 2500, 4000) },
                new[] { BridgeCommand.MoveTo(1, 1, 22500, 2500) }, Wp23.Pack());
            for (int i = 0; i < 200; i++) { s.StepOnce(); Assert.That(Wp23.At(s).Y.Raw, Is.EqualTo(0)); }
            Assert.That(Wp23.At(s).X.Raw, Is.GreaterThan(8 * S));
        }
    }
}
