using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-E1g T0c (a wall on sloping ground stands from the floor and its top is flat) and E3 (the 62 walls).
    public sealed class MapE1gWallTests
    {
        private static readonly ContentPack PackOfLayer = RwrPack.Load(MapE1gBuild.Pack);

        // ---- T0c ----

        private static MapDocument WithHouse(long heightRaw) =>
            MapE1gBuild.Ground(MapE1gBuild.GenText(), MapE1gBuild.ReadDem()).Document
                .WithWallAdded(new MapWallInfo("wall", "walls", 34, 34, 9, 6, heightRaw, false, 0));

        private static string[] Codes(MapDocument doc) => MapGate.Check(doc.Save(), PackOfLayer).Select(p => p.Code).ToArray();

        [Test]
        public void ASixMetreWallOnTheSlopeIsRefusedAndTheLiftedOneIsNot()
        {
            MapDocument low = WithHouse(393216);
            Assert.That(Codes(low), Is.EqualTo(new[] { "GATE_WALL_TERRAIN" }));
            var h = low.Desc.Heights;
            long under = MapE1gBuild.HighestUnder(h, 34, 34, 9, 6);
            Assert.That(under, Is.EqualTo(741212), "the highest ground point under b-house");
            Assert.That(393216, Is.LessThan(under + 65536), "why it is refused: the top is not 1 m above the highest point");
            MapDocument lifted = WithHouse(1134428);
            Assert.That(Codes(lifted), Is.Empty);
            Assert.That(1134428, Is.EqualTo(under + 393216), "lowest side 6 m, as the art list asks");
        }

        [Test]
        public void TheBakedBoxStandsOnTheFloorAndIsAsTallAsAsked()
        {
            MapBakeResult baked = MapBake.Run(WithHouse(1134428).Save());
            WcMap map = WcMapReader.Read(baked.WcMapBytes);
            Assert.That(map.Walls.Count, Is.EqualTo(1));
            WcMapWall w = map.Walls[0];
            Assert.That(w.Height.Raw, Is.EqualTo(1134428));
            Assert.That(w.LocalCenter.Y.Raw - w.Height.Raw / 2, Is.EqualTo(0), "bottom on the floor");
            Assert.That(w.LocalCenter.Y.Raw + w.Height.Raw / 2, Is.EqualTo(1134428), "top");
            Assert.That(map.Min.Y.Raw, Is.EqualTo(0), "the floor");
        }

        [Test]
        public void TheWallTopIsFlatWhileTheGroundBelowIsNot()
        {
            var h = WithHouse(1134428).Desc.Heights;
            long low = long.MaxValue;
            for (int z = 34; z <= 40; z++)
                for (int x = 34; x <= 43; x++) low = System.Math.Min(low, h[z][x]);
            Assert.That(1134428 - low, Is.GreaterThan(393216), "on the low side the wall stands taller than the 6 m asked");
        }

        // ---- E3 ----

        private static IReadOnlyList<MapWallInfo> Walls() => MapE1gBuild.Build().Walls();

        [Test]
        public void TheListIsTheArtList()
        {
            MapE1gLayout.Piece[] p = MapE1gLayout.Pieces;
            Assert.That(p.Length, Is.EqualTo(62));
            Assert.That(p.Take(MapE1gLayout.Solids).Select(x => x.Name), Is.EqualTo(new[]
                { "a-barn", "a-house", "e-farm", "e-farm-b", "e-house", "b-house", "c-farm", "c-barn", "d-house" }));
            foreach ((string kind, int n) in new[] { ("hedge", 27), ("hay", 19), ("wood", 3), ("truck", 4) })
                Assert.That(p.Count(x => x.Name.StartsWith(kind + "-")), Is.EqualTo(n), kind);
            Assert.That(p.Skip(MapE1gLayout.Solids).All(x => x.WantedMm == 2000), Is.True, "cover is 2 m");
            Assert.That(p.Take(MapE1gLayout.Solids).Select(x => x.WantedMm), Is.EqualTo(new[] { 3000, 6000, 6000, 6000, 6000, 6000, 6000, 3000, 6000 }));
            Assert.That(p.Sum(x => x.W * x.D), Is.EqualTo(884), "cells covered");
            var seen = new HashSet<(int, int)>();
            foreach (MapE1gLayout.Piece x in p)
                for (int j = x.Z; j < x.Z + x.D; j++)
                    for (int i = x.X; i < x.X + x.W; i++)
                        Assert.That(seen.Add((i, j)), Is.True, x.Name + " overlaps at " + i + "," + j);
            Assert.That(p.All(x => x.X >= 0 && x.Z >= 0 && x.X + x.W <= 160 && x.Z + x.D <= 160), Is.True);
        }

        [Test]
        public void SixtyTwoWallsWithTheListsCellsAndSizes()
        {
            IReadOnlyList<MapWallInfo> walls = Walls();
            Assert.That(walls.Count, Is.EqualTo(62));
            for (int k = 0; k < 62; k++)
            {
                MapE1gLayout.Piece p = MapE1gLayout.Pieces[k];
                MapWallInfo w = walls[k];
                Assert.That((w.Id, w.Kind, w.Layer, w.CellX, w.CellZ, w.SizeX, w.SizeZ, w.HasBase), Is.EqualTo(((ulong)(k + 1), "wall", "walls", p.X, p.Z, p.W, p.D, false)), p.Name);
            }
        }

        [Test]
        public void TheNineHousesAreAsHighAsTheCardSays()
        {
            IReadOnlyList<MapWallInfo> walls = Walls();
            Assert.That(walls.Take(9).Select(w => w.HeightRaw), Is.EqualTo(new long[]
                { 345374, 492176, 420086, 408944, 447610, 1134428, 1079378, 988938, 1002046 }));
        }

        [Test]
        public void EveryWallIsFlatTopEvenAndUnderTheCeiling()
        {
            MapDocument doc = MapE1gBuild.Build();
            var h = doc.Desc.Heights;
            long sum = 0;
            foreach (MapWallInfo w in doc.Walls())
            {
                // Worked out again here, not through the build code's helper.
                long high = 0;
                for (int z = w.CellZ; z <= w.CellZ + w.SizeZ; z++)
                    for (int x = w.CellX; x <= w.CellX + w.SizeX; x++) high = System.Math.Max(high, h[z][x]);
                long wanted = MapE1gLayout.Pieces[(int)w.Id - 1].WantedMm == 6000 ? 393216 : MapE1gLayout.Pieces[(int)w.Id - 1].WantedMm == 3000 ? 196608 : 131072;
                long expect = high + wanted;
                if (expect % 2 != 0) expect++;
                Assert.That(w.HeightRaw, Is.EqualTo(expect), "wall " + w.Id);
                Assert.That(w.HeightRaw % 2, Is.EqualTo(0), "wall " + w.Id);
                Assert.That(w.HeightRaw, Is.LessThan(MapE1gBuild.CeilingRaw));
                Assert.That(w.HeightRaw, Is.GreaterThanOrEqualTo(high + 65536), "1 m over the highest point, the gate's rule");
                sum += w.HeightRaw;
            }
            Assert.That(sum, Is.EqualTo(31228554));
        }
    }
}
