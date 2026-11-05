using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M4CrowdTests
    {
        internal static BigInteger Squared(SimVec3 a, SimVec3 b)
        {
            BigInteger x = (BigInteger)a.X.Raw - b.X.Raw, z = (BigInteger)a.Z.Raw - b.Z.Raw;
            return x * x + z * z;
        }

        internal static double CheckPairs(SimVec3[] points, int tick)
        {
            BigInteger min = BigInteger.Pow(10, 30), threshold = BigInteger.Pow(32768, 2);
            for (int i = 0; i < points.Length; i++)
                for (int j = i + 1; j < points.Length; j++)
                {
                    BigInteger distance = Squared(points[i], points[j]);
                    if (distance < threshold) Assert.Fail($"Overlap tick={tick} pair={i + 1}/{j + 1} squared={distance}");
                    if (distance < min) min = distance;
                }
            return Math.Sqrt((double)min) / 65536;
        }

        internal static SimVec3[] Points(SimSession session, int n)
        {
            var result = new SimVec3[n];
            for (int i = 0; i < n; i++) result[i] = M3TestWorld.Position(session, (ulong)i + 1);
            return result;
        }

        [Test]
        public void Demo_ExactArrival_Spacing_Walls_Speed()
        {
            var map = M2DemoMap.Build();
            var recipe = DemoRecipes.M4();
            var session = SimSession.Create(recipe, WcMapWriter.Write(map));
            var previous = Points(session, 20);
            var arrived = new int[20];
            double minimum = CheckPairs(previous, 0), wallMinimum = double.PositiveInfinity;
            long step = SimFix.Ratio(6000, 1000 * SimClock.TickRate).Raw;
            for (int i = 0; i < 20; i++)
            {
                Assert.That(recipe.Units[i].Key, Is.EqualTo((ulong)i + 1));
                Assert.That(recipe.Units[i].Squad, Is.EqualTo(i < 10 ? 1 : 2));
                Assert.That(recipe.Units[i].XMm, Is.EqualTo((i < 10 ? 6000 : 58000) + ((i % 10) % 4) * 1000 - 1500));
                Assert.That(recipe.Units[i].ZMm, Is.EqualTo((i < 10 ? 10000 : 8000) + (i % 10) / 4 * 1000 - 1000));
            }
            for (int tick = 1; tick <= 600; tick++)
            {
                session.StepOnce();
                var current = Points(session, 20);
                minimum = Math.Min(minimum, CheckPairs(current, tick));
                for (int i = 0; i < 20; i++)
                {
                    if (Squared(current[i], previous[i]) > (BigInteger)step * step) Assert.Fail($"Speed tick={tick} key={i + 1}");
                    Assert.That(map.TryCell(current[i].X, current[i].Z, out int x, out int z) && map.IsPassable(x, z), Is.True);
                    foreach (var wall in map.Walls)
                    {
                        double distance = M3TestWorld.WallDistance(previous[i], current[i], map, wall);
                        wallMinimum = Math.Min(wallMinimum, distance);
                        if (distance < 0.25) Assert.Fail($"Wall tick={tick} key={i + 1} distance={distance:R}");
                    }
                    SimVec3 expected = Target(i);
                    if (current[i].Equals(expected) && arrived[i] == 0) arrived[i] = tick;
                }
                previous = current;
            }
            TestContext.Out.WriteLine($"M4 minimum={minimum:R} wall={wallMinimum:R} arrivals={string.Join(",", arrived)} hash={session.StateHash:X16}");
            for (int i = 0; i < 20; i++)
            {
                TestContext.Out.WriteLine($"key={i + 1} final={previous[i].X.Raw},{previous[i].Z.Raw} target={Target(i).X.Raw},{Target(i).Z.Raw}");
            }
            for (int i = 0; i < 20; i++)
            {
                Assert.That(previous[i], Is.EqualTo(Target(i)), "key=" + (i + 1));
                Assert.That(arrived[i], Is.InRange(1, 600));
            }
        }

        internal static SimVec3 Target(int i) => M3TestWorld.Point(
            (i < 10 ? 44000 : 56000) + (i % 10) % 4 * 2000 - 3000,
            22000 + (i % 10) / 4 * 2000 - 2000);

        [Test]
        public void D_HeadOnFivePairs_ExactSwapBy600()
        {
            var units = new UnitSpawn[10];
            var commands = new BridgeCommand[10];
            for (int i = 0; i < 10; i++)
            {
                units[i] = new UnitSpawn((ulong)i + 1, 0, i + 1, i < 5 ? 4500 : 24500, 4500 + i % 5 * 3000, 6000);
                commands[i] = BridgeCommand.MoveTo(i + 1, i + 1, i < 5 ? 24500 : 4500, 4500 + i % 5 * 3000);
            }
            var session = SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, units, commands), WcMapWriter.Write(M3TestWorld.Boxes(32, 24)));
            double minimum = double.PositiveInfinity;
            for (int tick = 1; tick <= 600; tick++) { session.StepOnce(); minimum = Math.Min(minimum, CheckPairs(Points(session, 10), tick)); }
            int arrived = 0;
            for (int i = 0; i < 10; i++)
                if (M3TestWorld.Position(session, (ulong)i + 1).Equals(M3TestWorld.Point(commands[i].XMm, commands[i].ZMm))) arrived++;
            TestContext.Out.WriteLine($"D arrived={arrived}/10 minimum={minimum:R}m deadline=600");
            for (int i = 0; i < 10; i++)
                Assert.That(M3TestWorld.Position(session, (ulong)i + 1), Is.EqualTo(M3TestWorld.Point(commands[i].XMm, commands[i].ZMm)), "key=" + (i + 1));
        }
    }
}
