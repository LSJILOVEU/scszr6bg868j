using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-1 acceptance 7 and 8 on the two door demo maps.
    public sealed class Ml1DoorDemoTests
    {
        private const long M = Ml1DoorMaps.Metre;

        private static byte[] DoorBytes() => File.ReadAllBytes(Ml1DoorMaps.DoorPath);
        private static byte[] TerrainBytes() => File.ReadAllBytes(Ml1DoorMaps.TerrainPath);

        [Test]
        public void StoredMapsAreWhatThePrefabStampsAndPassTheGate()
        {
            foreach ((byte[] bytes, MapDesc built) in new[] { (DoorBytes(), Ml1DoorMaps.Door()), (TerrainBytes(), Ml1DoorMaps.DoorTerrain()) })
            {
                Assert.That(bytes, Is.EqualTo(MapDescJson.Write(built)), built.Id);
                Assert.That(MapDescJson.Write(MapDescJson.Read(bytes)), Is.EqualTo(bytes), built.Id + " canonical");
                Assert.That(MapDescJson.Read(bytes).Version, Is.EqualTo(2), built.Id);
                Assert.That(MapGate.Check(bytes, RwrPack.Load(built.Pack)), Is.Empty, built.Id);
            }
        }

        // The slope door: its two cells' lowest ground differs by 0.5 m, and the lintel sits 2 m over the highest corner.
        [Test]
        public void SlopeDoorIsOnTheSlope()
        {
            MapDesc d = MapDescJson.Read(TerrainBytes());
            int z = Ml1DoorMaps.SlopeWallZ, x = Ml1DoorMaps.SlopeDoorX;
            long Low(int cx) => new[] { d.Heights[z][cx], d.Heights[z][cx + 1], d.Heights[z + 1][cx], d.Heights[z + 1][cx + 1] }.Min();
            Assert.That(Low(x + 1) - Low(x), Is.GreaterThanOrEqualTo(M / 2));
            MapWall lintel = d.Walls.Single(w => w.BaseRaw.HasValue);
            long highest = new[] { d.Heights[z][x + 2], d.Heights[z + 1][x + 2] }.Max();
            Assert.That(lintel.BaseRaw, Is.EqualTo(highest + 2 * M));
            Assert.That((lintel.Cell.X, lintel.Cell.Z, lintel.Size.X), Is.EqualTo((x, z, 2)));
        }

        // Acceptance 8: on both maps, what is drawn, Sim's walls, navigation and heights agree.
        [Test]
        public void TerrainCheckIsClean()
        {
            foreach (byte[] bytes in new[] { DoorBytes(), TerrainBytes() })
            {
                MapBakeResult bake = MapBake.Run(bytes);
                SimSession s = SimSession.Create(Movers(bake, new[] { new UnitSpawn(1, 0, 1, 1500, 1500, 6000, 1) }, null), bake.WcMapBytes);
                TerrainCheckResult r = TerrainCheck.Run(s.Map, s);
                Assert.That(r.IsClean, Is.True, r.ToString());
            }
        }

        // The only way through the wall is the doorway: the path from team 1's base to point 2 goes through a doorway cell,
        // and the doorway cells are walkable while the frame cells beside them are not.
        [Test]
        public void PathGoesThroughTheDoorway()
        {
            WcMap map = WcMapReader.Read(MapBake.Run(DoorBytes()).WcMapBytes);
            int x = Ml1DoorMaps.WallX, z = Ml1DoorMaps.DoorZ;
            Assert.That(new[] { map.IsPassable(x, z - 1), map.IsPassable(x, z), map.IsPassable(x, z + 1), map.IsPassable(x, z + 2) },
                Is.EqualTo(new[] { false, true, true, false }));
            Assert.That(Enumerable.Range(0, 40).Count(cz => map.IsPassable(x, cz)), Is.EqualTo(2));
            var finder = new GridPathfinder(WcMapNavigation.Create(map));
            Assert.That(finder.Find(SimVec3.Metres(4, 0, 32), SimVec3.Metres(45, 0, 32)), Is.EqualTo(GridPathStatus.Found));
            int[] path = finder.Path.ToArray();
            Assert.That(path.Any(n => n == z * 60 + x || n == (z + 1) * 60 + x), Is.True);
            Assert.That(path.All(n => n % 60 != x || n / 60 == z || n / 60 == z + 1), Is.True, "crosses x = 30 only in the doorway");
        }

        [Test]
        public void SlopePathGoesThroughTheDoorway()
        {
            WcMap map = WcMapReader.Read(MapBake.Run(TerrainBytes()).WcMapBytes);
            NavGrid nav = TerrainDerivation.Derive(map, RwrPack.Load(MapDescJson.Read(TerrainBytes()).Pack)).Nav;
            int x = Ml1DoorMaps.SlopeDoorX, z = Ml1DoorMaps.SlopeWallZ, w = (int)map.Width;
            Assert.That(Enumerable.Range(0, w).Where(cx => nav.IsPassable(cx, z)).ToArray(), Is.EqualTo(new[] { x, x + 1 }));
            var finder = new GridPathfinder(nav);
            Assert.That(finder.Find(new SimVec3(SimFix.Ratio(27, 2), default, SimFix.Ratio(3, 2)), new SimVec3(SimFix.Ratio(27, 2), default, SimFix.Ratio(15, 2))),
                Is.EqualTo(GridPathStatus.Found));
            Assert.That(finder.Path.ToArray().Any(n => n == z * w + x || n == z * w + x + 1), Is.True);
        }

        // Squad 1 (team 1's six) ordered from the west side to point 2 on the east side: within 600 ticks somebody walks
        // through the doorway and arrives (inside point 2's 6 m radius), and nobody ever stands inside a frame or the lintel's
        // footprint other than the doorway. No combat, no match: only the order and the walls.
        [Test]
        public void SoldiersWalkThroughTheDoorwayAndArrive()
        {
            Walk(DoorBytes(), 1, new MapMm(45000, 32000), out bool arrived, out bool crossed);
            Assert.That(crossed, Is.True, "crossed x = 31");
            Assert.That(arrived, Is.True, "arrived within 6 m of point 2");
        }

        // The slope doorway: one soldier on the west slope below the wall walks north through it, uphill side of the hill.
        [Test]
        public void SoldierWalksThroughTheSlopeDoorway()
        {
            Walk(TerrainBytes(), 1, new MapMm(13500, 9500), out bool arrived, out bool crossed,
                new[] { new UnitSpawn(1, 0, 1, 13500, 1500, 6000, 1) });
            Assert.That(crossed, Is.True, "crossed the wall row");
            Assert.That(arrived, Is.True, "arrived north of the wall");
        }

        private static void Walk(byte[] bytes, int squad, MapMm target, out bool arrived, out bool crossed, UnitSpawn[] units = null)
        {
            MapBakeResult bake = MapBake.Run(bytes);
            units ??= MapRecipe.From(bake, RwrPack.Load(bake.PackId)).Units.ToArray();
            WcMap map = WcMapReader.Read(bake.WcMapBytes);
            bool slope = map.HasTerrain;
            var world = new M6World(Movers(bake, units, BridgeCommand.MoveTo(1, squad, target.X, target.Z)), bake.WcMapBytes);
            ulong[] keys = units.Where(u => u.Squad == squad).Select(u => u.Key).ToArray();
            arrived = crossed = false;
            for (int tick = 0; tick < 600; tick++)
            {
                world.Session.StepOnce();
                foreach (ulong key in keys)
                {
                    SimVec3 p = world.Read<Position>(key).Value;
                    int cx = (int)(p.X.Raw >> 16), cz = (int)(p.Z.Raw >> 16);
                    Assert.That(map.IsPassable(cx, cz), Is.True, $"unit {key} in a blocked cell ({cx}, {cz}) at tick {tick}");
                    if (slope ? cz > Ml1DoorMaps.SlopeWallZ : cx > Ml1DoorMaps.WallX) crossed = true;
                    long dx = p.X.Raw - target.X * M / 1000, dz = p.Z.Raw - target.Z * M / 1000;
                    if ((double)dx * dx + (double)dz * dz <= 36.0 * M * M) arrived = true;
                }
            }
        }

        // Movement only (no combat, no match), with the map's terrain rules when it has terrain.
        private static WorldRecipe Movers(MapBakeResult bake, UnitSpawn[] units, BridgeCommand? order)
        {
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, order.HasValue ? new[] { order.Value } : Array.Empty<BridgeCommand>());
            return WcMapReader.Read(bake.WcMapBytes).HasTerrain ? recipe.WithTerrainRules(RwrPack.Load(bake.PackId)) : recipe;
        }

        // Sight across the doorway is clear (both ends at the aim height, 0.5 m, under the lintel at 2 m); across a frame it
        // is not. A wall-layer ray 2.5 m above the ground through the doorway hits the lintel: it really is overhead.
        [Test]
        public void SightPassesUnderTheLintelAndARayAtTwoAndAHalfMetresHitsIt()
        {
            MapBakeResult bake = MapBake.Run(DoorBytes());
            var world = new M6World(MapRecipe.From(bake, RwrPack.Load(bake.PackId)), bake.WcMapBytes);
            Assert.That(world.Combat.Sees(SimVec3.Metres(25, 0, 20), SimVec3.Metres(35, 0, 20)), Is.True, "through the doorway");
            Assert.That(world.Combat.Sees(SimVec3.Metres(25, 0, 10), SimVec3.Metres(35, 0, 10)), Is.False, "through a frame");
            uint mask = WcMapReader.Read(bake.WcMapBytes).LayerMask("walls");
            SimVec3 from = new SimVec3(SimFix.FromInt(25), SimFix.Ratio(5, 2), SimFix.FromInt(20)), delta = SimVec3.Metres(10, 0, 0);
            Assert.That(world.Sim.SpatialQuery.Any(SpatialQueries.Ray(from, delta, delta.Length, mask)), Is.True, "2.5 m: the lintel");
            SimVec3 low = new SimVec3(SimFix.FromInt(25), SimFix.Ratio(19, 10), SimFix.FromInt(20));
            Assert.That(world.Sim.SpatialQuery.Any(SpatialQueries.Ray(low, delta, delta.Length, mask)), Is.False, "1.9 m: under it");
        }
    }
}
