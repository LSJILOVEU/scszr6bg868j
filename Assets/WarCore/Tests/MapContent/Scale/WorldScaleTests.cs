using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // SCALE-1: how the world's sizes relate to each other. Every assertion reads both sides from the code or the shipped data
    // (a constant, a content layer, a demo map, or a world built from them), so a test goes red when a relation breaks and
    // never because a copied number went stale. The few absolute values written here are the ones docs/world-scale.md names as
    // the unit of the table: a 1 m cell, 30 steps a second, a 0.3 m seam rounded down.
    // A content layer that no longer loads fails the relation test that needed it (an assertion, not an error), because a
    // body that breaks the headroom rule is refused by the loader itself.
    public sealed class WorldScaleTests
    {
        private const long M = SimFix.Scale;

        // ---- shipped data ----

        private readonly struct Layer
        {
            internal readonly string Cartridge, Id;
            internal readonly ContentPack Pack;
            internal Layer(string cartridge, string id, ContentPack pack) { Cartridge = cartridge; Id = id; Pack = pack; }
        }

        private static List<Layer> Shipped()
        {
            ContentCatalog catalog = ContentCatalog.Find();
            Assert.That(catalog.Cartridges, Does.Contain("Rwr").And.Contain("Ancient"), "both shipped cartridges are listed");
            var all = new List<Layer>();
            foreach (string cartridge in catalog.Cartridges)
                foreach (string id in catalog.Layers(cartridge))
                {
                    ContentPack pack = null;
                    try { pack = catalog.Load(id); }
                    catch (ContentException e) { Assert.Fail("layer " + id + " of " + cartridge + " does not load: " + e.Message); }
                    all.Add(new Layer(cartridge, id, pack));
                }
            Assert.That(all, Is.Not.Empty);
            return all;
        }

        private static ContentPack Rwr(string id) => ShippedLayer("Rwr", id);

        private static ContentPack ShippedLayer(string cartridge, string id)
        {
            foreach (Layer l in Shipped())
                if (l.Cartridge == cartridge && l.Id == id) return l.Pack;
            Assert.Fail("no layer " + id + " in " + cartridge);
            return null;
        }

        // The speed of a unit type in mm/s, or null when its layer gives none (the RWR types take the recipe's speed).
        private static long? SpeedOf(ContentPack pack, string unitType)
        {
            try { return pack.SpawnOfUnitType(unitType).SpeedMmPerSec; }
            catch (ContentException) { return null; }
        }

        private static long StepRaw(long mmPerSec) => SimFix.Ratio(mmPerSec, 1000 * SimClock.TickRate).Raw;

        // ---- worlds ----

        private const ulong Player = 1, Enemy = 10, Reserve = 20;

        private static UnitSpawn Soldier(ulong key, int x, int z, int team, bool reserve = false) =>
            new UnitSpawn(key, 0, team, x * 1000L, z * 1000L, 3000, team, -1, reserve);

        // A flat 80 x 40 m match: the player and an enemy deployed at the start, one reserve of the player's team waiting for
        // reinforcement, every unit moving at 3000 mm/s.
        private static M6World Match()
        {
            MatchPack pack = RespawnWorld.Pack(RespawnWorld.Points(RespawnWorld.Spawn(1, 30, 10)),
                RespawnWorld.Bases(new SpawnAnchor(1, RespawnWorld.P(5, 38))), 300);
            var units = new List<UnitSpawn> { Soldier(Player, 20, 20, 1), Soldier(Enemy, 78, 38, 2), Soldier(Reserve, 30, 10, 1, reserve: true) };
            return RespawnWorld.World(units, pack);
        }

        // Deploys the reserve through the real deployer, as a reinforcement does.
        private static void Reinforce(M6World w)
        {
            var deployer = Ml7Worlds.Field<IUnitDeployer>(w.Reinforce, "deployer");
            Assert.That(deployer.TryDeploy(w.Unit(Reserve), RespawnWorld.P(30, 10)), Is.True, "the reserve is deployed");
        }

        // ---- maps ----

        private static readonly MapNext Next = new MapNext(1, 1, 1, 1);

        private static MapDesc Flat() =>
            new MapDesc("scale-flat", "none", "scale", new MapXZ(12, 4), new MapXZ(0, 0), 0, 10 * M, new[] { new MapLayer("walls", 1) },
                null, null, new MapWall[0], new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(41, "teamIdle"),
                new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0], Next);

        private static IEnumerable<(string Name, MapDesc Desc)> DemoMaps()
        {
            yield return ("PlateDemoMap", PlateDemoMap.Desc());
            yield return ("PlateAssaultMap", PlateAssaultMap.Desc());
        }

        // W1: a map cell, a height point step and the unit of the fixed-point scale are the same metre.
        [Test]
        public void W01_CellAndHeightSpacingAreOneMetre()
        {
            WcMap map = M3TestWorld.Boxes(4, 4);
            Assert.That(map.CellSize, Is.EqualTo(SimFix.FromInt(1)), "map cell");
            // A 2 x 2 field whose points rise one raw metre per step along x: the height read one cell along is the next point.
            var points = new int[9];
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++) points[j * 3 + i] = i * (int)M;
            var field = new HeightField(2, 2, default, default, points);
            Assert.That(field.TryHeightAt(map.CellSize, default, out SimFix next), Is.True);
            Assert.That(next.Raw, Is.EqualTo(field.VertexRaw(1, 0)), "one cell along is the next height point");
            Assert.That(field.TryHeightAt(SimFix.FromRaw(map.CellSize.Raw / 2), default, out SimFix half), Is.True);
            Assert.That(half.Raw, Is.EqualTo((field.VertexRaw(0, 0) + field.VertexRaw(1, 0)) / 2), "half a cell along is halfway");
        }

        // W2: 30 steps a second, and both ways a unit gets its speed (placed at the start, deployed as a reinforcement) turn
        // mm per second into the same raw per step with that 30.
        [Test]
        public void W02_StepsPerSecondAndSpeedConversion()
        {
            Assert.That(SimClock.TickRate, Is.EqualTo(30));
            long expected = 3000L * M / (1000L * SimClock.TickRate);
            M6World w = Match();
            Assert.That(w.Read<MoveIntent>(Player).SpeedPerTick.Raw, Is.EqualTo(expected), "unit placed at the start");
            Reinforce(w);
            Assert.That(w.Read<MoveIntent>(Reserve).SpeedPerTick.Raw, Is.EqualTo(expected), "unit deployed later");
        }

        // W3: one soldier radius everywhere: the content constant, the collider of a unit placed at the start, the collider of a
        // unit deployed as a reinforcement, the wall probe below a walk band, and the radii of the sweep and the terrain mover.
        [Test]
        public void W03_SoldierRadiusIsTheSameEverywhere()
        {
            long r = CombatSection.UnitRadiusRaw;
            M6World w = Match();
            Assert.That(w.Read<Collider>(Player).Radius.Raw, Is.EqualTo(r), "collider of a unit placed at the start");
            Reinforce(w);
            Assert.That(w.Read<Collider>(Reserve).Radius.Raw, Is.EqualTo(r), "collider of a deployed reinforcement");
            Assert.That(WallSpan.BelowRaw, Is.EqualTo(r), "wall probe below the walk band");
            Assert.That(NavGrid.MaxClearRadiusRaw, Is.EqualTo(r), "sweep radius cap");
            Assert.That(InterimLocomotion.UnitFootprint.Raw, Is.EqualTo(r), "terrain mover footprint");
        }

        // W4: every shipped body fits the headroom and holds its own eye and muzzle: 2r <= height <= clearance,
        // r <= eye <= height - r, 0 <= muzzle <= height.
        [Test]
        public void W04_ShippedBodiesFitTheHeadroomAndTheirOwnPoints()
        {
            long r = CombatSection.UnitRadiusRaw;
            int withBody = 0;
            foreach (Layer l in Shipped())
            {
                if (!l.Pack.Body.HasValue) continue;
                withBody++;
                CombatBody b = l.Pack.Body.Value;
                string at = l.Id + " body";
                Assert.That(b.Height.Raw, Is.GreaterThanOrEqualTo(2 * r), at + " height is at least a diameter");
                Assert.That(b.Height.Raw, Is.LessThanOrEqualTo(WallSpan.ClearanceRaw), at + " height fits under the headroom");
                Assert.That(b.Eye.Raw, Is.InRange(r, b.Height.Raw - r), at + " eye");
                Assert.That(b.Muzzle.Raw, Is.InRange(0L, b.Height.Raw), at + " muzzle");
            }
            Assert.That(withBody, Is.GreaterThan(0), "some shipped layer carries a body");
        }

        // W5: the underside of a door's lintel is the headroom above the floor, and no shipped body is taller than the doorway.
        [Test]
        public void W05_DoorwayIsTheHeadroom()
        {
            MapStampPlan plan = null;
            Assert.DoesNotThrow(() => plan = MapPrefabs.Door(Flat(), "door", new MapXZ(0, 1), true, 10, 4, 2, 4 * M, "walls", "wall"));
            MapWall lintel = plan.Walls[1].Template;
            Assert.That(lintel.BaseRaw, Is.Not.Null, "the lintel hangs on a base");
            Assert.That(lintel.BaseRaw.Value, Is.EqualTo(WallSpan.ClearanceRaw), "lintel underside above the floor");
            Assert.That(WallSpan.BlocksWalk(lintel.BaseRaw.Value, 4 * M, 0, 0), Is.False, "the doorway can be walked");
            Assert.That(WallSpan.BlocksWalk(lintel.BaseRaw.Value - 1, 4 * M, 0, 0), Is.True, "one raw lower and it cannot");
            foreach (Layer l in Shipped())
                if (l.Pack.Body.HasValue)
                    Assert.That(l.Pack.Body.Value.Height.Raw, Is.LessThanOrEqualTo(lintel.BaseRaw.Value), l.Id + " body fits through the door");
        }

        // W6: the default window suits every shipped body: sill >= 2r, sill <= min(eye, muzzle) - r, opening top >=
        // max(eye, muzzle) + r, and the prefab accepts that body at the defaults.
        [Test]
        public void W06_DefaultWindowSuitsEveryShippedBody()
        {
            long r = CombatSection.UnitRadiusRaw;
            int withBody = 0;
            foreach (Layer l in Shipped())
            {
                if (!l.Pack.Body.HasValue) continue;
                withBody++;
                CombatBody b = l.Pack.Body.Value;
                string at = l.Id + " body";
                Assert.That(MapPrefabs.DefaultSillRaw, Is.GreaterThanOrEqualTo(2 * r), at + ": sill is no threshold");
                Assert.That(MapPrefabs.DefaultSillRaw, Is.LessThanOrEqualTo(Math.Min(b.Eye.Raw, b.Muzzle.Raw) - r), at + ": sill below eye and muzzle");
                Assert.That(MapPrefabs.DefaultOpeningRaw, Is.GreaterThanOrEqualTo(Math.Max(b.Eye.Raw, b.Muzzle.Raw) + r), at + ": opening above eye and muzzle");
                CombatBody body = b;
                Assert.DoesNotThrow(() => MapPrefabs.Window(Flat(), "win", new MapXZ(0, 1), true, 10, 4, 2, 4 * M, "walls", "wall", body), at + ": prefab accepts it");
            }
            Assert.That(withBody, Is.GreaterThan(0), "some shipped layer carries a body");
        }

        // W7: the least storey that stacks is the headroom plus the thinnest plate, and both demo maps are at least that tall.
        [Test]
        public void W07_DemoStoreysAreAtLeastTheLeastStackableHeight()
        {
            long least = WallSpan.ClearanceRaw + WcMapPlate.MinThickRaw;
            foreach ((string name, MapDesc desc) in DemoMaps())
            {
                Assert.That(desc.Heights, Is.Null, name + " stands on a flat floor");
                int flat = 0;
                foreach (MapPlate p in desc.Plates)
                {
                    Assert.That(p.ThickRaw, Is.GreaterThanOrEqualTo(WcMapPlate.MinThickRaw), name + " plate " + p.Id + " thickness");
                    if (!p.YRaw.HasValue) continue;
                    flat++;
                    long storey = p.YRaw.Value - desc.FloorRaw;
                    Assert.That(storey, Is.GreaterThanOrEqualTo(least), name + " plate " + p.Id + " storey height");
                    Assert.That(storey - p.ThickRaw, Is.GreaterThanOrEqualTo(WallSpan.ClearanceRaw), name + " plate " + p.Id + " leaves the headroom below it");
                }
                Assert.That(flat, Is.GreaterThan(0), name + " has a flat floor plate");
            }
        }

        // W8: the stair of each demo map is no steeper than its content layer allows (no slope rule means the height field's
        // own limit) and no steeper than the gate's plate slope.
        [Test]
        public void W08_DemoStairsAreWithinTheAllowedSlopes()
        {
            foreach ((string name, MapDesc desc) in DemoMaps())
            {
                TerrainRules rules = Rwr(desc.Pack).Terrain;
                long packNum = rules?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator, packDen = rules?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator;
                int stairs = 0;
                foreach (MapPlate p in desc.Plates)
                {
                    if (p.Slope == null) continue;
                    stairs++;
                    long rise = Math.Abs(p.Slope.ToRaw - p.Slope.FromRaw);
                    long run = (p.Slope.Axis == "x" ? p.Size.X : p.Size.Z) * M;
                    string at = name + " plate " + p.Id;
                    Assert.That(rise * packDen, Is.LessThanOrEqualTo(packNum * run), at + " against the content layer " + desc.Pack);
                    Assert.That(rise * MapGate.PlateSlopeDen, Is.LessThanOrEqualTo(MapGate.PlateSlopeNum * run), at + " against the gate");
                }
                Assert.That(stairs, Is.GreaterThan(0), name + " has a stair");
            }
        }

        // W9: the seam between layers is 0.3 m rounded down, and thinner than the thinnest plate.
        [Test]
        public void W09_SeamToleranceIsThreeTenthsOfAMetreAndThinnerThanAPlate()
        {
            Assert.That(LayeredNav.StepRaw, Is.EqualTo(SimFix.Ratio(3, 10).Raw));
            Assert.That(LayeredNav.StepRaw, Is.LessThan(WcMapPlate.MinThickRaw));
        }

        // The unit speeds the shipped data and the recipe default give, in mm/s.
        private static List<long> ShippedSpeeds()
        {
            var speeds = new List<long>();
            foreach (Layer l in Shipped())
                for (int i = 0; i < l.Pack.UnitTypeCount; i++)
                {
                    long? mm = SpeedOf(l.Pack, l.Pack.UnitTypeId(i));
                    if (mm.HasValue) speeds.Add(mm.Value);
                }
            // RWR soldiers take the recipe's speed.
            WorldRecipe recipe = MapRecipe.From(MapBake.Run(PlateDemoMap.DescBytes()), Rwr("rwr-m6"));
            Assert.That(recipe.Units.Length, Is.GreaterThan(0));
            for (int i = 0; i < recipe.Units.Length; i++) speeds.Add(recipe.Units[i].SpeedMmPerSec);
            return speeds;
        }

        // W10: every shipped unit speed, as a step, is within the one-step cap of a world with terrain.
        [Test]
        public void W10_ShippedSpeedsFitTheTerrainStepCap()
        {
            foreach (long mm in ShippedSpeeds())
                Assert.That(StepRaw(mm), Is.LessThanOrEqualTo(InterimLocomotion.MaxTerrainStepRaw), mm + " mm/s");
        }

        // W11: the fastest shipped unit on the fastest shipped surface stays inside the cap after the surface multiplier, and
        // so does the largest step on the largest multiplier the terrain accepts.
        [Test]
        public void W11_FastestUnitOnFastestSurfaceFitsTheScaledCap()
        {
            long fastest = 0;
            foreach (long mm in ShippedSpeeds()) fastest = Math.Max(fastest, StepRaw(mm));
            long milli = 0;
            foreach (Layer l in Shipped())
                for (int i = 0; l.Pack.Terrain != null && i < l.Pack.Terrain.SurfaceCount; i++)
                    milli = Math.Max(milli, l.Pack.Terrain.Surface(i).SpeedMilli);
            Assert.That(milli, Is.GreaterThan(0), "some shipped layer has surfaces");
            Assert.That(fastest * milli / 1000, Is.LessThanOrEqualTo(InterimLocomotion.MaxEffectiveStepRaw), "fastest shipped unit on the fastest shipped surface");
            Assert.That(InterimLocomotion.MaxTerrainStepRaw * SurfaceDef.MaxSpeedMilli / 1000, Is.LessThanOrEqualTo(InterimLocomotion.MaxEffectiveStepRaw),
                "largest step on the largest accepted multiplier");
        }

        // W12: the largest map side and the height limit fit inside the world's coordinate limit.
        [Test]
        public void W12_MapAndHeightLimitsFitTheWorld()
        {
            Assert.That(HeightField.MaxSide, Is.LessThanOrEqualTo(SimVec3.WorldLimitMetres));
            Assert.That(HeightField.HeightLimitRaw, Is.LessThanOrEqualTo(SimVec3.WorldLimitMetres * M));
        }

        // W13: every shipped melee swing starts within its reach, and its reach is longer than two soldiers standing edge to edge.
        [Test]
        public void W13_MeleeReachCoversTheSwingStartAndTwoRadii()
        {
            long r = CombatSection.UnitRadiusRaw;
            int swings = 0;
            foreach (Layer l in Shipped())
            {
                CombatPack combat = l.Pack.BuildCombat();
                for (int i = 0; i < combat.Melee.Length; i++)
                {
                    swings++;
                    MeleeSpec spec = combat.Melee[i];
                    Assert.That(spec.EngageRange.Raw, Is.LessThanOrEqualTo(spec.Radius.Raw), l.Id + " melee " + i + " start distance");
                    Assert.That(spec.Radius.Raw, Is.GreaterThan(2 * r), l.Id + " melee " + i + " reach against 2r");
                }
            }
            Assert.That(swings, Is.GreaterThan(0), "some shipped layer has melee");
        }

        // W14: no shipped weapon's falloff reaches farther than the diagonal of the largest map.
        [Test]
        public void W14_WeaponFalloffFitsTheLargestMap()
        {
            long side = HeightField.MaxSide * M;
            int weapons = 0;
            foreach (Layer l in Shipped())
            {
                CombatPack combat = l.Pack.BuildCombat();
                for (int i = 0; i < combat.Weapons.Length; i++)
                {
                    weapons++;
                    long end = combat.Weapons[i].FalloffEnd.Raw;
                    Assert.That(end * end, Is.LessThanOrEqualTo(2 * side * side), l.Id + " weapon " + i + " falloff end");
                }
            }
            Assert.That(weapons, Is.GreaterThan(0), "some shipped layer has weapons");
        }

        // W15: the spatial index cell of a built world is at least a soldier's diameter.
        [Test]
        public void W15_SpatialCellIsAtLeastASoldierDiameter()
        {
            M6World w = Match();
            FieldInfo cell = typeof(GridSpatialIndex).GetField("cellSize", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(cell, Is.Not.Null, "the index keeps its cell size in a field called cellSize");
            Assert.That((long)cell.GetValue(w.Sim.SpatialIndex), Is.GreaterThanOrEqualTo(2 * CombatSection.UnitRadiusRaw));
        }
    }
}
