using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-1 acceptance 4 (WALL_BURIED, 1 m rule only for floor walls) and 6 (check order), on .wcmap and in the gate.
    public sealed class Ml1ValidationTests
    {
        private const long M = Ml1Desc.M;

        // One raised wall on cell 2 of the slope (highest corner 1 m): top = 1 m is buried, 1 m + 1 raw is not.
        private static MapDesc OnCellTwo(long topRaw, int heightRaw = 2) =>
            Ml1Desc.Make(4, 1, Ml1Desc.Slope(), Ml1Desc.Wall(1, 0, 0, 1, 1, 4 * M), Ml1Desc.Wall(2, 2, 0, 1, 1, heightRaw, baseRaw: topRaw - heightRaw));

        [Test]
        public void BuriedRaisedWallIsRejectedOneRawHigherIsNot()
        {
            Assert.That(Ml1Desc.BakeError(OnCellTwo(M)), Is.EqualTo("MAPDESC_WCMAP WCMAP_WALL_BURIED"));
            WcMap map = Ml1Desc.Valid(Ml1Desc.Bake(OnCellTwo(M + 1)));
            Assert.That(map.IsPassable(2, 0), Is.False);
        }

        // The navigation check follows the walk band for raised walls: an open lintel cell marked blocked (the footprint
        // rule) is a mismatch, and so is a blocking raised wall's cell marked open.
        [Test]
        public void NavigationCheckFollowsTheWalkBand()
        {
            WcMap open = Ml1Desc.Valid(Ml1Desc.Bake(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, M, baseRaw: 2 * M))));
            WcMap closed = Ml1Desc.Valid(Ml1Desc.Bake(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, M + 2, baseRaw: 2 * M - 2))));
            Assert.That(Code(WithWall(open, 0, open.Walls[0].Height.Raw, open.Walls[0].LocalCenter.Y.Raw, passable: false)), Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
            Assert.That(Code(WithWall(closed, 0, closed.Walls[0].Height.Raw, closed.Walls[0].LocalCenter.Y.Raw, passable: true)), Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
        }

        // The highest corner of every footprint cell counts, the far edges included: a wall over cells 0..2 is judged by 1 m.
        [Test]
        public void BuriedIsJudgedOnTheHighestCornerOfTheFootprint()
        {
            MapDesc wide(long top) => Ml1Desc.Make(4, 1, Ml1Desc.Slope(), Ml1Desc.Wall(1, 0, 0, 3, 1, 2, baseRaw: top - 2));
            Assert.That(Ml1Desc.BakeError(wide(M)), Is.EqualTo("MAPDESC_WCMAP WCMAP_WALL_BURIED"));
            Assert.That(Ml1Desc.Valid(Ml1Desc.Bake(wide(M + 1))).IsPassable(0, 0), Is.False);
        }

        [Test]
        public void GateReportsABuriedWallAndBakesTheRest()
        {
            Assert.That(Ml1Desc.Gate(OnCellTwo(M)).Select(p => (p.Code, p.Id, p.CellX, p.CellZ)),
                Is.EqualTo(new[] { ("GATE_WALL_BURIED", "wall 2", 2, 0) }));
            Assert.That(Ml1Desc.Gate(OnCellTwo(M + 1)), Is.Empty);
        }

        // A raised sill-like wall 0.5 m above the highest corner passes; a floor wall with the same top fails the 1 m rule.
        [Test]
        public void OneMetreRuleIsOnlyForFloorWalls()
        {
            MapDesc raised = OnCellTwo(M + M / 2, (int)(M / 2));
            MapDesc floor = Ml1Desc.Make(4, 1, Ml1Desc.Slope(), Ml1Desc.Wall(1, 0, 0, 1, 1, 4 * M), Ml1Desc.Wall(2, 2, 0, 1, 1, M + M / 2));
            Assert.That(Ml1Desc.Valid(Ml1Desc.Bake(raised)).IsPassable(2, 0), Is.False);
            Assert.That(Ml1Desc.Gate(raised), Is.Empty);
            Assert.That(Ml1Desc.BakeError(floor), Is.EqualTo("MAPDESC_WCMAP WCMAP_WALL_TERRAIN"));
            Assert.That(Ml1Desc.Gate(floor).Select(p => p.Code), Is.EqualTo(new[] { "GATE_WALL_TERRAIN" }));
        }

        // Raised <=> centre Y > half height. Centre Y = half height + 1 raw is raised (1 m rule skipped, WALL_BURIED checked);
        // centre Y = half height is a floor wall (1 m rule). Same top, 1.5 m, over a highest corner of 1 m.
        [Test]
        public void RaisedMeansCentreAboveHalfHeight()
        {
            WcMap baked = MapBake.BuildMap(OnCellTwo(M + M / 2, (int)(M / 2)));
            WcMapWall sill = baked.Walls[1];
            Assert.That(sill.LocalCenter.Y.Raw, Is.EqualTo(M + M / 4));
            Assert.DoesNotThrow(() => WcMapValidation.Validate(baked));

            WcMap lowest = WithWall(baked, 1, M + M / 2 - 2, (M + M / 2 - 2) / 2 + 1, passable: false);
            Assert.DoesNotThrow(() => WcMapValidation.Validate(lowest), "centre Y = half height + 1 raw");
            WcMap floorWall = WithWall(baked, 1, M + M / 2, (M + M / 2) / 2, passable: false);
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapValidation.Validate(floorWall)).Message, Is.EqualTo("WCMAP_WALL_TERRAIN"));
        }

        // Centre Y may now exceed half height, but the top stays inside Min.Y..Max.Y; below half height is still out of bounds.
        [Test]
        public void RaisedWallStaysInsideTheVerticalBounds()
        {
            WcMap baked = MapBake.BuildMap(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, 2 * M, baseRaw: M)));
            long range = baked.Max.Y.Raw - baked.Min.Y.Raw;
            Assert.DoesNotThrow(() => WcMapValidation.Validate(WithWall(baked, 0, 2 * M, range - M, passable: true)), "top at Max.Y");
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapValidation.Validate(WithWall(baked, 0, 2 * M, range - M + 1, passable: true))).Message,
                Is.EqualTo("WCMAP_BOUNDS"), "top 1 raw above Max.Y");
            Assert.That(Assert.Throws<InvalidDataException>(() => WcMapValidation.Validate(WithWall(baked, 0, 2 * M, M - 1, passable: true))).Message,
                Is.EqualTo("WCMAP_BOUNDS"), "bottom 1 raw below Min.Y");
        }

        // Acceptance 6. With a raised wall the height table is checked before the navigation bitmap: a short table is
        // HEIGHT_SIZE, not an index error, even when the bitmap is also wrong. Without one, the same broken file reports
        // what it reported before this change: the bitmap first.
        [Test]
        public void HeightTableIsCheckedFirstOnlyWithARaisedWall()
        {
            WcMap raised = MapBake.BuildMap(Ml1Desc.Make(4, 1, Ml1Desc.Slope(), Ml1Desc.Wall(1, 2, 0, 1, 1, M, baseRaw: 3 * M)));
            WcMap floor = MapBake.BuildMap(Ml1Desc.Make(4, 1, Ml1Desc.Slope(), Ml1Desc.Wall(1, 2, 0, 1, 1, 4 * M)));
            Assert.That(Code(Broken(raised)), Is.EqualTo("WCMAP_HEIGHT_SIZE"));
            Assert.That(Code(Broken(floor)), Is.EqualTo("WCMAP_NAV_WALL_MISMATCH"));
            Assert.That(Code(Broken(floor, bitmap: false)), Is.EqualTo("WCMAP_HEIGHT_SIZE"));
        }

        private static string Code(WcMap map) => Assert.Throws<InvalidDataException>(() => WcMapValidation.Validate(map)).Message;

        // Height table one point short; optionally the first cell's passability bit flipped.
        private static WcMap Broken(WcMap m, bool bitmap = true)
        {
            byte[] bits = m.Passability.ToArray();
            if (bitmap) bits[0] ^= 1;
            return new WcMap(m.MapId, m.ContentId, m.Min, m.Max, m.ChunkSize, m.FlatHeight, m.Width, m.Depth, m.Layers.ToArray(),
                m.Walls.ToArray(), bits, m.HeightPoints.Take(m.HeightPoints.Count - 1).ToArray(), null, null);
        }

        // Wall i replaced by the same footprint with this height and centre Y; its cells' passability set as given.
        private static WcMap WithWall(WcMap m, int i, long heightRaw, long centreY, bool passable)
        {
            WcMapWall w = m.Walls[i];
            WcMapWall[] walls = m.Walls.ToArray();
            walls[i] = new WcMapWall(w.Chunk, w.Placement, w.KindId, w.LayerName,
                new SimVec3(w.LocalCenter.X, SimFix.FromRaw(centreY), w.LocalCenter.Z), w.HalfX, SimFix.FromRaw(heightRaw), w.HalfZ);
            byte[] bits = m.Passability.ToArray();
            int x0 = (int)((w.LocalCenter.X.Raw - w.HalfX.Raw) / M), x1 = (int)((w.LocalCenter.X.Raw + w.HalfX.Raw) / M);
            int z0 = (int)((w.LocalCenter.Z.Raw - w.HalfZ.Raw) / M), z1 = (int)((w.LocalCenter.Z.Raw + w.HalfZ.Raw) / M);
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    int index = z * (int)m.Width + x;
                    if (passable) bits[index >> 3] |= (byte)(1 << (index & 7));
                    else bits[index >> 3] &= (byte)~(1 << (index & 7));
                }
            return new WcMap(m.MapId, m.ContentId, m.Min, m.Max, m.ChunkSize, m.FlatHeight, m.Width, m.Depth, m.Layers.ToArray(),
                walls, bits, m.HasHeightField ? m.HeightPoints.ToArray() : null, null, null);
        }
    }
}
