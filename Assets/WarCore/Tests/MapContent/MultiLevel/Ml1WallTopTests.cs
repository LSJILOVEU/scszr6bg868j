using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The walk band with the wall top taken from bottom + height, on ground well above the floor, and the .wcmap height
    // points measured from Min.Y on a map whose floor is not 0.
    public sealed class Ml1WallTopTests
    {
        private const long M = Ml1Desc.M;

        // 4 x 1 cells, every height point 3 m above floor 0; a 0.5 m wall from 3.5 m to 4 m over cell 1.
        private static MapDesc Plateau()
        {
            int[] row = Enumerable.Repeat((int)(3 * M), 5).ToArray();
            return Ml1Desc.Make(4, 1, new[] { row, row.ToArray() }, Ml1Desc.Wall(1, 1, 0, 1, 1, M / 2, baseRaw: 3 * M + M / 2));
        }

        // Top 4 m is above the 3 m ground (not buried) and the bottom 3.5 m is under 3 m + 2.0 m: the cell is blocked.
        // A top read as the height alone (0.5 m) would sit below the band and open the cell.
        [Test]
        public void ShortRaisedWallOnHighGroundBlocks()
        {
            WcMap map = Ml1Desc.Valid(Ml1Desc.Bake(Plateau()));
            Assert.That(new[] { map.IsPassable(0, 0), map.IsPassable(1, 0), map.IsPassable(2, 0) }, Is.EqualTo(new[] { true, false, true }));
            Assert.That(Ml1Desc.Gate(Plateau()), Is.Empty);
        }

        // The same .wcmap with that cell marked walkable is rejected.
        [Test]
        public void ShortRaisedWallOnHighGroundMarkedOpenIsAMismatch()
        {
            WcMap map = Ml1Desc.Bake(Plateau());
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapValidation.Validate(WithCell(map, 1, passable: true))).Message,
                Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
        }

        // Floor -1 m and the slope (absolute highest corners 0, 0.5 m, 1 m; 1 m higher above the floor).
        private static MapDesc Low(params MapWall[] walls) => Ml1Desc.Make(-M, 4, 1, Ml1Desc.Slope(), walls);

        // Floor wall on cell 2: the highest point is 2 m above the floor, so the 1 m rule needs a height of 3 m.
        [Test]
        public void OneMetreRuleMeasuresFromTheFloor()
        {
            Assert.That(Ml1Desc.Valid(Ml1Desc.Bake(Low(Ml1Desc.Wall(1, 2, 0, 1, 1, 3 * M)))).IsPassable(2, 0), Is.False);
            Assert.That(Ml1Desc.BakeError(Low(Ml1Desc.Wall(1, 2, 0, 1, 1, 3 * M - 2))), Is.EqualTo("MAPDESC_WCMAP WCMAP_WALL_TERRAIN"));
        }

        // Raised wall on cell 2 with an absolute top of 1 m (the highest point) is buried; 1 raw higher is not.
        [Test]
        public void BuriedMeasuresFromTheFloor()
        {
            MapDesc wall(long top) => Low(Ml1Desc.Wall(1, 2, 0, 1, 1, 2, baseRaw: top - 2));
            Assert.That(Ml1Desc.BakeError(wall(M)), Is.EqualTo("MAPDESC_WCMAP WCMAP_WALL_BURIED"));
            Assert.That(Ml1Desc.Valid(Ml1Desc.Bake(wall(M + 1))).IsPassable(2, 0), Is.False);
        }

        // A lintel over cells 0..2 with its underside at 2.5 m absolute: open, open, blocked, and the .wcmap agrees.
        [Test]
        public void LintelReadsEachCellFromTheFloor()
        {
            WcMap map = Ml1Desc.Valid(Ml1Desc.Bake(Low(Ml1Desc.Wall(1, 0, 0, 3, 1, 4 * M, baseRaw: 2 * M + M / 2))));
            Assert.That(new[] { map.IsPassable(0, 0), map.IsPassable(1, 0), map.IsPassable(2, 0) }, Is.EqualTo(new[] { true, true, false }));
        }

        // Cell (x, 0) set walkable or blocked; everything else unchanged.
        private static WcMap WithCell(WcMap m, int x, bool passable)
        {
            byte[] bits = m.Passability.ToArray();
            if (passable) bits[x >> 3] |= (byte)(1 << (x & 7));
            else bits[x >> 3] &= (byte)~(1 << (x & 7));
            return new WcMap(m.MapId, m.ContentId, m.Min, m.Max, m.ChunkSize, m.FlatHeight, m.Width, m.Depth, m.Layers.ToArray(),
                m.Walls.ToArray(), bits, m.HasHeightField ? m.HeightPoints.ToArray() : null, null, null);
        }
    }
}
