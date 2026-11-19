using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Small descriptions for the ML-1 tests: floor 0, ceiling 10 m, layer "walls" (bit 1), no points.
    internal static class Ml1Desc
    {
        internal const long M = SimFix.Scale;

        internal static MapWall Wall(ulong id, int x, int z, int w, int d, long heightRaw, long? baseRaw = null) =>
            new MapWall(id, "wall", "walls", new MapXZ(x, z), new MapXZ(w, d), heightRaw, baseRaw: baseRaw);

        internal static MapDesc Make(int width, int depth, int[][] heights, params MapWall[] walls) => Make(0, width, depth, heights, walls);

        internal static MapDesc Make(long floorRaw, int width, int depth, int[][] heights, params MapWall[] walls) =>
            new MapDesc("ml1", "none", "rwr-m6", new MapXZ(width, depth), new MapXZ(0, 0), floorRaw, floorRaw + 10 * M,
                new[] { new MapLayer("walls", 1) }, heights, null, walls, new MapPoint[0], new MapBase[0], new MapUnit[0],
                new MapRun(41, "none"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(walls.Length == 0 ? 1 : walls.Max(w => w.Id) + 1, 1, 1, 1));

        internal static byte[] Bytes(MapDesc d) => MapDescJson.Write(d);
        internal static string Text(MapDesc d) => Encoding.UTF8.GetString(Bytes(d));

        // The bake's model only, no .wcmap validation, so a wrong bitmap shows up as a wrong bit (Valid checks the file rules).
        internal static WcMap Bake(MapDesc d) => MapBake.BuildMap(MapDescJson.Read(Bytes(d)));

        internal static WcMap Valid(WcMap map)
        {
            Assert.DoesNotThrow(() => WcMapValidation.Validate(map), "the .wcmap rules accept the baked map");
            return map;
        }

        internal static string BakeError(MapDesc d) => Assert.Throws<InvalidDataException>(() => MapBake.Run(Bytes(d))).Message;

        // 4 x 1 cells whose highest corners are 0, 0.5 m and 1 m for cells 0, 1 and 2, each time only at the far corner
        // (x + 1, z + 1), so a rule that drops that corner or takes one value for the whole wall reads another height.
        internal static int[][] Slope() => new[]
        {
            new[] { 0, 0, 0, (int)(M / 2), (int)(M / 2) },
            new[] { 0, 0, (int)(M / 2), (int)M, (int)M },
        };

        internal static System.Collections.Generic.IReadOnlyList<MapProblem> Gate(MapDesc d) =>
            MapGate.Check(Bytes(d), RwrPack.Load(d.Pack));
    }

    // ML-1 acceptance 2 and 3: the walk-band rule, on its own and through the bake.
    public sealed class Ml1WallSpanTests
    {
        private const long M = Ml1Desc.M;

        // Flat ground at local 0: a lintel 2.0 m up leaves the cell walkable, 1 raw lower blocks it.
        [Test]
        public void LintelAtTheHeadroomIsOpenAndOneRawLowerBlocks()
        {
            Assert.That(WallSpan.ClearanceRaw, Is.EqualTo(2 * M));
            Assert.That(WallSpan.BelowRaw, Is.EqualTo(M / 4));
            Assert.That(WallSpan.BlocksWalk(2 * M, 3 * M, 0, 0), Is.False);
            Assert.That(WallSpan.BlocksWalk(2 * M - 1, 3 * M, 0, 0), Is.True);
        }

        // The bottom of the band is inclusive: a top that touches ground - 0.25 m blocks, 1 raw lower does not.
        // (Under WALL_BURIED a raised wall's top is always above the ground, so only this function reaches that edge.)
        [Test]
        public void BandBottomIsTheProbeSphereInclusive()
        {
            Assert.That(WallSpan.BlocksWalk(-M, 1, 0, 0), Is.True, "top 1 raw above flat ground");
            Assert.That(WallSpan.BlocksWalk(-M, -M / 4, 0, 0), Is.True, "top at ground - 0.25 m");
            Assert.That(WallSpan.BlocksWalk(-M, -M / 4 - 1, 0, 0), Is.False, "top 1 raw below ground - 0.25 m");
            Assert.That(WallSpan.BlocksWalk(-M, M / 2 - M / 4, M / 2, 3 * M), Is.True, "lowest corner sets the bottom");
            Assert.That(WallSpan.BlocksWalk(-M, M / 2 - M / 4 - 1, M / 2, 3 * M), Is.False, "lowest corner sets the bottom");
        }

        // The smallest raised wall on flat ground (bottom 1 raw, top 3 raw) blocks its cell and bakes.
        [Test]
        public void SmallestRaisedWallOnFlatGroundBlocks()
        {
            WcMap map = Ml1Desc.Bake(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, 2, baseRaw: 1)));
            Assert.That(new[] { map.IsPassable(0, 0), map.IsPassable(1, 0), map.IsPassable(2, 0) }, Is.EqualTo(new[] { true, false, true }));
            Assert.That(map.Walls[0].LocalCenter.Y.Raw, Is.EqualTo(2));
            Ml1Desc.Valid(map);
        }

        [Test]
        public void FlatLintelThroughTheBake()
        {
            WcMap open = Ml1Desc.Bake(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, M, baseRaw: 2 * M)));
            WcMap closed = Ml1Desc.Bake(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, M + 2, baseRaw: 2 * M - 2)));
            Assert.That(open.IsPassable(1, 0), Is.True);
            Assert.That(closed.IsPassable(1, 0), Is.False);
            Assert.That(open.Walls[0].LocalCenter.Y.Raw, Is.EqualTo(2 * M + M / 2), "centre Y = bottom - floor + height / 2");
            Ml1Desc.Valid(open);
            Ml1Desc.Valid(closed);
        }

        // Heights are absolute in the description and relative to the floor in the .wcmap: floor -1 m, a lintel at +1 m
        // absolute is 2 m above the flat ground, open, with centre Y 2 m + half its height above the floor.
        [Test]
        public void RaisedBottomIsMeasuredFromTheFloor()
        {
            WcMap open = Ml1Desc.Bake(Ml1Desc.Make(-M, 3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, M, baseRaw: M)));
            WcMap closed = Ml1Desc.Bake(Ml1Desc.Make(-M, 3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, M + 2, baseRaw: M - 2)));
            Assert.That((open.IsPassable(1, 0), closed.IsPassable(1, 0)), Is.EqualTo((true, false)));
            Assert.That(open.Walls[0].LocalCenter.Y.Raw, Is.EqualTo(2 * M + M / 2));
            Ml1Desc.Valid(open);
            Ml1Desc.Valid(closed);
        }

        // A lintel over three cells whose highest corners are 0, 0.5 m and 1 m, underside 2.6 m: open, open, blocked.
        // Each cell reads its own four corners; underside 2.5 m is the edge of cell 1.
        [TestCase(170394L, new[] { true, true, false })]
        [TestCase(163840L, new[] { true, true, false })]
        [TestCase(163839L, new[] { true, false, false })]
        [TestCase(131072L, new[] { true, false, false })]
        [TestCase(131071L, new[] { false, false, false })]
        public void SlopeLintelReadsEachCellsCorners(long bottom, bool[] open)
        {
            WcMap map = Ml1Desc.Bake(Ml1Desc.Make(4, 1, Ml1Desc.Slope(), Ml1Desc.Wall(1, 0, 0, 3, 1, 4 * M, baseRaw: bottom)));
            Assert.That(new[] { map.IsPassable(0, 0), map.IsPassable(1, 0), map.IsPassable(2, 0) }, Is.EqualTo(open));
            Assert.That(map.IsPassable(3, 0), Is.True);
            Ml1Desc.Valid(map);
        }

        // Floor walls keep blocking their whole footprint (old rule), whatever their height.
        [Test]
        public void FloorWallsBlockTheirFootprint()
        {
            WcMap map = Ml1Desc.Bake(Ml1Desc.Make(4, 1, null, Ml1Desc.Wall(1, 1, 0, 2, 1, 2)));
            Assert.That(new[] { map.IsPassable(0, 0), map.IsPassable(1, 0), map.IsPassable(2, 0), map.IsPassable(3, 0) },
                Is.EqualTo(new[] { true, false, false, true }));
            Ml1Desc.Valid(map);
        }
    }
}
