using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-E1g T0b: a match started from the map file takes the owners and the spawn positions of the points, and reinforcements
    // come in at the bases and at the spawn of the point a team holds.
    public sealed class MapE1gStartTests
    {
        private static readonly long[][] Spawn =
        {
            new long[] { 109500, 38500 }, new long[] { 40500, 30500 }, new long[] { 10500, 89500 },
            new long[] { 61500, 150500 }, new long[] { 119500, 123500 },
        };

        [Test]
        public void TheRecipeCarriesOwnersAndSpawnPositionsOnTheGround()
        {
            ControlPointDef[] points = MapE1gWorld.Recipe().Match.Points.ToArray();
            Assert.That(points.Select(p => p.InitialOwner), Is.EqualTo(new[] { 1, 0, 0, 0, 2 }));
            Assert.That(points.All(p => p.HasSpawn), Is.True);
            Assert.That(points.Select(p => p.Id), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            for (int k = 0; k < 5; k++)
            {
                SimVec3 s = points[k].Spawn;
                Assert.That((s.X.Raw, s.Z.Raw), Is.EqualTo((Spawn[k][0] * 65536 / 1000, Spawn[k][1] * 65536 / 1000)), "spawn " + k);
                Assert.That(MapE1gWorld.Tables.Ground.TryHeightAt(s.X, s.Z, out SimFix y), Is.True);
                Assert.That(s.Y.Raw, Is.EqualTo(y.Raw), "the spawn stands on the interpolated ground, spawn " + k);
            }
        }

        [Test]
        public void TheMatchStartsWithTheFileOwnersAndKeepsThemFor200Ticks()
        {
            M6World w = MapE1gWorld.World();
            Assert.That(w.Capture.Points.ToArray().Select(p => p.Owner), Is.EqualTo(new[] { 1, 0, 0, 0, 2 }), "tick 0");
            for (int t = 0; t < 200; t++)
            {
                w.Step();
                Assert.That(w.Capture.Changes.Length, Is.EqualTo(0), "tick " + t);
            }
            Assert.That(w.Capture.Points.ToArray().Select(p => p.Owner), Is.EqualTo(new[] { 1, 0, 0, 0, 2 }), "tick 200");
        }

        // Each team's two reinforcements of the first 200 ticks come in near one of the places the team may deploy at (its base,
        // or the spawn of a point it holds), and at least one at the spawn of its home point. The deploy record is emptied at
        // the start of every tick, so it is collected after every step.
        [Test]
        public void EachTeamDeploysTwiceByTick200AndOneComesInAtItsHomePoint()
        {
            M6World w = MapE1gWorld.World();
            var deployed = new List<DeployRecord>();
            for (int t = 0; t < 200; t++)
            {
                w.Step();
                deployed.AddRange(w.Reinforce.Deployed.ToArray());
            }
            long[][][] places =
            {
                new[] { new long[] { 98500, 8500 }, Spawn[0] },
                new[] { new long[] { 118500, 150500 }, Spawn[4] },
            };
            var report = new List<string>();
            for (int team = 1; team <= 2; team++)
            {
                List<DeployRecord> mine = deployed.Where(d => d.Team == team).ToList();
                Assert.That(mine.Count, Is.EqualTo(2), "team " + team);
                int home = 0;
                foreach (DeployRecord d in mine)
                {
                    double best = places[team - 1].Min(p => Dist(d.Point, p));
                    int nearest = Dist(d.Point, places[team - 1][0]) <= Dist(d.Point, places[team - 1][1]) ? 0 : 1;
                    Assert.That(best, Is.LessThanOrEqualTo(3.5), "team " + team + " tick " + d.Tick);
                    if (nearest == 1) home++;
                    report.Add("team " + team + " tick " + d.Tick + " near " + (nearest == 0 ? "base" : "home point") + " " + best.ToString("F2") + " m");
                }
                Assert.That(home, Is.GreaterThanOrEqualTo(1), "team " + team + " has at least one at its home point");
            }
            TestContext.WriteLine("MAPE1G T0b " + string.Join("; ", report));
        }

        // Horizontal distance in metres from a deploy point to a place given in millimetres.
        private static double Dist(SimVec3 p, long[] mm)
        {
            double dx = p.X.Raw / 65536.0 - mm[0] / 1000.0, dz = p.Z.Raw / 65536.0 - mm[1] / 1000.0;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
