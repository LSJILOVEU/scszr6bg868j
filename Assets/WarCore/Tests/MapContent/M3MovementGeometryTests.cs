using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M3MovementGeometryTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void HundredStaggeredSquads_ReachExactTargetsWithinSegmentBound_WholeSweepsAreSafe(bool uShape)
        {
            WcMap map = uShape ? M3TestWorld.UMap() : M2DemoMap.Build();
            NavGrid grid = WcMapNavigation.Create(map);
            var random = new Random(uShape ? 17031 : 17030);
            var units = new UnitSpawn[100];
            var commands = new BridgeCommand[100];
            var targets = new SimVec3[100];
            var deadlines = new int[100];
            var previous = new SimVec3[100];
            int maxTick = 0;
            var search = new GridPathfinder(grid);
            for (int i = 0; i < units.Length; i++)
            {
                var start = LegalPoint(random, map);
                var end = LegalPoint(random, map);
                if (i == 0)
                {
                    start = uShape ? (6500L, 3500L) : (4000L, 12000L);
                    end = uShape ? (6500L, 500L) : (60000L, 12000L);
                }
                while (start == end) end = LegalPoint(random, map);
                SimVec3 a = M3TestWorld.Point(start.x, start.z), b = M3TestWorld.Point(end.x, end.z);
                targets[i] = b; previous[i] = a;
                units[i] = new UnitSpawn((ulong)i + 1, 0, i + 1, start.x, start.z, 6000);
                commands[i] = BridgeCommand.MoveTo(i + 1, i + 1, end.x, end.z);
                deadlines[i] = i + 1 + SegmentTicks(grid, search, a, b) + 2;
                maxTick = Math.Max(maxTick, deadlines[i]);
            }
            bool directBlocked = false;
            foreach (WcMapWall wall in map.Walls)
                directBlocked |= M3TestWorld.WallDistance(previous[0], targets[0], map, wall) < 0.25;
            Assert.That(directBlocked, Is.True, "The first sample must require a detour.");
            var session = SimSession.Create(new WorldRecipe(41, MoverKind.InterimPath, units, commands), WcMapWriter.Write(map));
            double minimum = double.PositiveInfinity;
            for (int tick = 1; tick <= maxTick; tick++)
            {
                session.StepOnce();
                for (int i = 0; i < units.Length; i++)
                {
                    SimVec3 current = M3TestWorld.Position(session, (ulong)i + 1);
                    if (!grid.TryCell(current, out int cell) || !grid.IsPassable(cell % grid.Width, cell / grid.Width))
                        Assert.Fail($"Blocked route cell tick={tick} unit={i + 1}");
                    foreach (WcMapWall wall in map.Walls)
                    {
                        double distance = M3TestWorld.WallDistance(previous[i], current, map, wall);
                        minimum = Math.Min(minimum, distance);
                        if (distance < 0.25 - 1e-10) Assert.Fail($"Sweep penetration tick={tick} unit={i + 1} distance={distance:R}");
                    }
                    if (tick >= deadlines[i] && !current.Equals(targets[i]))
                        Assert.Fail($"Late unit={i + 1} tick={tick}, expected=({targets[i].X.Raw},{targets[i].Z.Raw}) actual=({current.X.Raw},{current.Z.Raw})");
                    if (tick == deadlines[i])
                    {
                        Assert.That(M3TestWorld.Component<MoveIntent>(session, (ulong)i + 1).Active, Is.False);
                        Assert.That(M3TestWorld.Component<MoveWaypoint>(session, (ulong)i + 1).Has, Is.False);
                    }
                    previous[i] = current;
                }
            }
            TestContext.Out.WriteLine($"M3 geometry map={(uShape ? "U" : "demo")} units=100 ticks={maxTick} minimum sweep clearance={minimum:R}m");
        }

        private static (long x, long z) LegalPoint(Random random, WcMap map)
        {
            while (true)
            {
                long x = random.Next((int)map.Width * 1000), z = random.Next((int)map.Depth * 1000);
                SimVec3 point = M3TestWorld.Point(x, z);
                if (!map.TryCell(point.X, point.Z, out int cx, out int cz) || !map.IsPassable(cx, cz)) continue;
                bool legal = true;
                foreach (WcMapWall wall in map.Walls) legal &= M3TestWorld.WallDistance(point, point, map, wall) > 0.25;
                if (legal) return (x, z);
            }
        }

        // Build the contract's vertices independently, replanning at each vertex as the runtime must.
        private static int SegmentTicks(NavGrid grid, GridPathfinder search, SimVec3 start, SimVec3 target)
        {
            int ticks = 0;
            SimVec3 point = start;
            for (int guard = 0; guard < grid.Count + 2; guard++)
            {
                Assert.That(search.Find(point, target), Is.EqualTo(GridPathStatus.Found));
                int[] route = search.Path.ToArray();
                foreach (int key in route) Assert.That(grid.IsPassable(key % grid.Width, key / grid.Width), Is.True);
                SimVec3 center = grid.Center(route[0], point.Y), next;
                if (!center.Equals(point)) next = center;
                else if (route.Length == 1) next = target;
                else
                {
                    var vertices = new List<int> { route[0] };
                    for (int n = 1; n < route.Length - 1; n++)
                        if (route[n] - route[n - 1] != route[n + 1] - route[n]) vertices.Add(route[n]);
                    vertices.Add(route[route.Length - 1]);
                    next = grid.Center(vertices[1], point.Y);
                }
                double dx = next.X.Raw - point.X.Raw, dz = next.Z.Raw - point.Z.Raw;
                ticks += (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / SimFix.Ratio(6000, 1000 * SimClock.TickRate).Raw);
                if (next.Equals(target)) return ticks;
                point = next;
            }
            Assert.Fail("Reference vertex plan failed to terminate.");
            return 0;
        }

        [Test]
        public void PointToCenter_CornerDistanceCanDip_ButRadiusSafetyStillHolds()
        {
            WcMap map = M3TestWorld.Boxes(3, 3, (1, 1, 1, 1));
            SimVec3 point = M3TestWorld.Point(990, 400), center = M3TestWorld.Point(500, 500);
            double first = M3TestWorld.WallDistance(point, point, map, map.Walls[0]);
            double last = M3TestWorld.WallDistance(center, center, map, map.Walls[0]);
            double sweep = M3TestWorld.WallDistance(point, center, map, map.Walls[0]);
            Assert.That(sweep, Is.LessThan(Math.Min(first, last)));
            Assert.That(sweep, Is.GreaterThanOrEqualTo(Math.Min(first, 0.5)));
            Assert.That(sweep, Is.GreaterThan(0.25));
            TestContext.Out.WriteLine($"M3 geometric counterexample endpoints={first:R}/{last:R}m segment minimum={sweep:R}m");
        }

        [Test]
        public void SegmentDistanceOracle_DetectsInteriorHitWithSafeEndpoints()
        {
            WcMap map = M3TestWorld.Boxes(8, 8, (3, 3, 1, 1));
            SimVec3 a = M3TestWorld.Point(1000, 3500), b = M3TestWorld.Point(6000, 3500);
            Assert.That(M3TestWorld.WallDistance(a, a, map, map.Walls[0]), Is.GreaterThan(0.25));
            Assert.That(M3TestWorld.WallDistance(b, b, map, map.Walls[0]), Is.GreaterThan(0.25));
            Assert.That(M3TestWorld.WallDistance(a, b, map, map.Walls[0]), Is.Zero);
            Assert.That(M3TestWorld.WallDistance(M3TestWorld.Point(1000, 2500), M3TestWorld.Point(6000, 2500), map, map.Walls[0]), Is.EqualTo(0.5));
        }
    }
}
