using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-P1: the three-lane playtest map is data only. These checks keep the description consistent and runnable.
    public sealed class MapP1LanesTests
    {
        internal const string LanesPath = "Assets/WarCore/Maps/lanes-playtest.map.json";
        private static byte[] Bytes() => File.ReadAllBytes(LanesPath);
        private static MapDesc Desc() => MapDescJson.Read(Bytes());

        [Test]
        public void DescriptionIsCanonicalAndPassesTheGate()
        {
            byte[] bytes = Bytes();
            Assert.That(MapDescJson.Write(MapDescJson.Read(bytes)), Is.EqualTo(bytes), "canonical bytes");
            Assert.That(MapGate.Check(bytes, RwrPack.Load(Desc().Pack)), Is.Empty);
        }

        // Fair for AI comparisons: every wall, point, base and unit has a point-mirrored twin about the map centre, teams swapped.
        [Test]
        public void LayoutIsPointSymmetricWithTeamsSwapped()
        {
            MapDesc d = Desc();
            int w = d.Size.X, h = d.Size.Z;
            long wMm = w * 1000L, hMm = h * 1000L;
            foreach (MapWall a in d.Walls)
                Assert.That(d.Walls.Any(b => b.Size.Equals(a.Size) && b.HeightRaw == a.HeightRaw
                    && b.Cell.Equals(new MapXZ(w - a.Cell.X - a.Size.X, h - a.Cell.Z - a.Size.Z))), "wall " + a.Id);
            foreach (MapPoint a in d.Points)
                Assert.That(d.Points.Any(b => b.RadiusMm == a.RadiusMm && b.At.Equals(new MapMm(wMm - a.At.X, hMm - a.At.Z))
                    && b.InitialOwner == (a.InitialOwner == 0 ? 0 : 3 - a.InitialOwner)), "point " + a.Id);
            foreach (MapBase a in d.Bases)
                Assert.That(d.Bases.Any(b => b.Team == 3 - a.Team && b.At.Equals(new MapMm(wMm - a.At.X, hMm - a.At.Z))), "base " + a.Id);
            foreach (MapUnit a in d.Units)
                Assert.That(d.Units.Any(b => b.Team == 3 - a.Team && b.Reserve == a.Reserve && b.At.Equals(new MapMm(wMm - a.At.X, hMm - a.At.Z))),
                    "unit " + a.Key);
        }

        // Points sit in three lanes (z bands split by the lane walls); a straight line between points in different lanes hits a wall,
        // so a flank cannot be seen from the other lane. Lane bounds are the lane walls' rows.
        [Test]
        public void PointsSpreadOverThreeLanesThatWallsKeepApart()
        {
            MapDesc d = Desc();
            int[] lanes = d.Points.Select(p => p.At.Z < 46500 ? 0 : p.At.Z < 93500 ? 1 : 2).ToArray();
            Assert.That(lanes.Distinct().Count(), Is.EqualTo(3));
            Assert.That(d.Points.Select(p => p.At.X).Distinct().Count(), Is.GreaterThanOrEqualTo(3));
            for (int i = 0; i < d.Points.Count; i++)
                for (int j = i + 1; j < d.Points.Count; j++)
                {
                    if (lanes[i] == lanes[j]) continue;
                    MapMm a = d.Points[i].At, b = d.Points[j].At;
                    Assert.That(d.Walls.Any(wall => Hits(a, b, wall)), "points " + d.Points[i].Id + " and " + d.Points[j].Id);
                }
        }

        [Test]
        public void SeedFortyOneEndsWithAWinnerAndNoStuckUnitsAndRepeats()
        {
            byte[] bytes = Bytes();
            MapBakeResult bake = MapBake.Run(bytes);
            WorldRecipe recipe = MapRecipe.From(bake, RwrPack.Load(bake.PackId));
            var a = new M6World(recipe, bake.WcMapBytes); var b = new M6World(recipe, bake.WcMapBytes);
            var meter = new M7StuckMeter(a); meter.Sample();
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < 9000)
            {
                a.Step(); b.Step(); meter.Sample();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "repeat tick=" + a.Sim.Clock.Tick);
            }
            TestContext.Out.WriteLine($"MAP_P1 end={a.Match.EndTick} winner={a.Match.Winner} reason={a.Match.EndReason} hash={a.Hash:X16} stuck={meter.StuckCount}");
            Assert.That(a.Match.Winner, Is.InRange(1, 2));
            Assert.That(meter.StuckCount, Is.Zero, "Report nonzero; do not tune the map to pass.");
        }

        // Segment a-b (millimetres) against the wall's footprint (whole cells, metres), slab method.
        private static bool Hits(MapMm a, MapMm b, MapWall wall)
        {
            double t0 = 0, t1 = 1;
            double ax = a.X / 1000.0, az = a.Z / 1000.0, dx = b.X / 1000.0 - ax, dz = b.Z / 1000.0 - az;
            return Clip(-dx, ax - wall.Cell.X, ref t0, ref t1) && Clip(dx, wall.Cell.X + wall.Size.X - ax, ref t0, ref t1)
                && Clip(-dz, az - wall.Cell.Z, ref t0, ref t1) && Clip(dz, wall.Cell.Z + wall.Size.Z - az, ref t0, ref t1);
        }

        private static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }
    }
}
