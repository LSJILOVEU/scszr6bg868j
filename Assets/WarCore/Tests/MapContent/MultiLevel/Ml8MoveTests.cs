using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;
using static WarCore.Tests.Ml8Worlds;

namespace WarCore.Tests
{
    // ML-8 acceptance 1 (versions), 4 (heights and steps), 6 (crowding), 7 (the player), 9 (M-i 2) and 10 (walls).
    public sealed class Ml8MoveTests
    {
        private const long S = SimFix.Scale;

        // The demo house raised by lift (raw): the floor height, the stair and the first floor go up with it; heights, when
        // given, are a flat field at the same raised floor.
        private static byte[] Raised(long lift, bool field)
        {
            MapDesc d = PlateDemoMap.Desc();
            var plates = new[]
            {
                new MapPlate(1, new MapXZ(10, 9), new MapXZ(4, 1), null, new MapSlope("x", lift, 3 * M + lift), M / 2),
                new MapPlate(2, new MapXZ(9, 6), new MapXZ(6, 4), 3 * M + lift, null, M / 2),
            };
            var walls = field ? d.Walls.Select(w => new MapWall(w.Id, w.Kind, w.Layer, w.Cell, w.Size, w.HeightRaw, baseRaw: lift)).ToArray() : d.Walls.ToArray();
            return Bytes(new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, field ? 0 : lift, d.CeilingRaw + lift, d.Layers.ToArray(),
                field ? Ml4Maps.Ground(24, 16, lift) : null, null, plates, d.Cutouts.ToArray(), walls, d.Points.ToArray(), d.Bases.ToArray(),
                d.Units.ToArray(), d.Run, d.Owners.ToArray(), null, null, d.Stamps.ToArray(), d.Removed.ToArray(), d.Next));
        }

        // 1: the locomotion's version names the plate binding; worlds without plates keep theirs.
        [Test]
        public void OnlyPlateWorldsChangeTheVersion()
        {
            var one = new[] { Unit(1, 1, 3500, 7500) };
            SimSession crowd = null;
            Assert.DoesNotThrow(() => crowd = Walk(Demo, one), "a plate map without a pack builds (the default rules)");
            Assert.That(M3TestWorld.Locomotion(crowd).StateVersion, Is.EqualTo(4 + 100 + 200), "crowd, terrain branch, plates");
            Assert.That(M3TestWorld.Locomotion(Walk(Demo, one, mover: MoverKind.InterimPath)).StateVersion, Is.EqualTo(3 + 100 + 200));
            Assert.That(M3TestWorld.Locomotion(World(NoPlates, new[] { Unit(1, 1, 500, 500) }, preview: false)).StateVersion, Is.EqualTo(4), "flat, no plates");
            WcMap heights = WcMapReader.Read(Raised(M / 4, true));
            Assert.That(heights.HasTerrain, Is.True);
        }

        // 4: on the stair every tick's height is the hand-computed slope (0.75 m a cell from x = 10 m) and the step is the
        // unit's own speed (the stair's surface, 1000; the ground under the stair is blocked); the floor is at 3 m.
        [Test]
        public void TheStairAndFloorGiveTheHeightAndTheStep()
        {
            SimSession s = Walk(Demo, new[] { Unit(1, 1, 9500, 8500) }, new[] { BridgeCommand.MoveTo(1, 1, 12500, 7500, 2) }, MoverKind.InterimPath);
            long speed = M3TestWorld.Component<MoveIntent>(s).SpeedPerTick.Raw;
            int stairSteps = 0;
            for (int t = 0; t < 200; t++)
            {
                SimVec3 before = M3TestWorld.Position(s);
                int layerBefore = Vertical(s, 1).Layer;
                s.StepOnce();
                SimVec3 p = M3TestWorld.Position(s);
                int layer = Vertical(s, 1).Layer;
                if (layer == 1) Assert.That(p.Y.Raw, Is.EqualTo(StairTop(p.X.Raw)), "tick " + t);
                if (layer == 2) Assert.That(p.Y.Raw, Is.EqualTo(3 * M), "tick " + t);
                if (layer == 1 && layerBefore == 1 && p.X.Raw % (M / 2) != 0)
                {
                    Assert.That((p.X.Raw - before.X.Raw, p.Z.Raw - before.Z.Raw), Is.EqualTo((speed, 0L)), "a whole step along x, tick " + t);
                    stairSteps++;
                }
            }
            Assert.That(stairSteps, Is.GreaterThan(10));
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(P(12500, 3 * M, 7500)), "arrival is horizontal and exact");
        }

        // 4: back on the ground a flat map's units stand at the map's floor height, a height field's at the field.
        [TestCase(false)]
        [TestCase(true)]
        public void BackOnTheGroundTheHeightIsTheGrounds(bool field)
        {
            const long lift = M / 4;
            byte[] map = Raised(lift, field);
            WorldRecipe recipe = Recipe(new[] { Unit(1, 1, 12500, 7500, 2) }, new[] { BridgeCommand.MoveTo(1, 1, 3500, 7500) });
            if (field) recipe = recipe.WithPack(Wp23.Pack());
            SimSession s = SimSession.Create(recipe, map);
            Assert.That(M3TestWorld.Position(s).Y.Raw, Is.EqualTo(3 * M + lift));
            bool down = false;
            for (int t = 0; t < 300; t++)
            {
                s.StepOnce();
                if (Vertical(s, 1).Layer != 0) continue;
                down = true;
                Assert.That(M3TestWorld.Position(s).Y.Raw, Is.EqualTo(lift), "tick " + t);
            }
            Assert.That(down && M3TestWorld.Position(s).Equals(P(3500, lift, 7500)), Is.True);
            Assert.That(M3TestWorld.Locomotion(s).IsArrived(M3TestWorld.Unit(s)), Is.True, "the target itself stands on the ground");
        }

        // 6: a unit on the first floor and one on the ground under it cross the same (x, z) without giving way; the floor
        // unit stays on the floor's top every tick.
        [Test]
        public void UnitsAboveAndBelowPassEachOther()
        {
            var units = new[] { Unit(1, 1, 10500, 7500, 2), Unit(2, 2, 13500, 7500) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 13500, 7500, 2), BridgeCommand.MoveTo(2, 2, 10500, 7500) };
            SimSession s = Walk(Demo, units, script);
            long nearest = long.MaxValue;
            for (int t = 0; t < 100; t++)
            {
                s.StepOnce();
                SimVec3 a = M3TestWorld.Position(s, 1), b = M3TestWorld.Position(s, 2);
                Assert.That((a.Y.Raw, a.Z.Raw, b.Y.Raw, b.Z.Raw), Is.EqualTo((3 * M, 7 * M + M / 2, 0L, 7 * M + M / 2)), "straight lines, tick " + t);
                nearest = Math.Min(nearest, Math.Abs(a.X.Raw - b.X.Raw));
            }
            Assert.That(nearest, Is.LessThan(M / 2), "they did meet");
            Assert.That((M3TestWorld.Position(s, 1), M3TestWorld.Position(s, 2)), Is.EqualTo((P(13500, 3 * M, 7500), P(10500, 0, 7500))),
                Show(M3TestWorld.Position(s, 1)) + " " + Show(M3TestWorld.Position(s, 2)));
        }

        // 6: on the stair two units still give way: one walking up never comes nearer than the two radii to one standing.
        [Test]
        public void UnitsOnTheStairStillGiveWay()
        {
            var units = new[] { Unit(1, 1, 11500, 9500, 1), Unit(2, 2, 9500, 9500) };
            SimSession s = Walk(Demo, units, new[] { BridgeCommand.MoveTo(1, 2, 12500, 7500, 2) });
            long nearest = long.MaxValue;
            for (int t = 0; t < 150; t++)
            {
                s.StepOnce();
                SimVec3 a = M3TestWorld.Position(s, 1), b = M3TestWorld.Position(s, 2);
                long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
                nearest = Math.Min(nearest, dx * dx + dz * dz);
            }
            Assert.That(nearest, Is.GreaterThanOrEqualTo((M / 2) * (M / 2)), "never through the standing unit");
            Assert.That(Vertical(s, 2).Layer, Is.Not.EqualTo((ushort)2), "it cannot get past on a one-cell stair");
        }

        // 6: the vertical overlap rule, with the ML-3 geometry (half height floor(H / 2); centre = foot + half for a
        // foot-anchored body, else the foot). Two ranges overlap while |c1 - c2| < h1 + h2.
        [Test]
        public void BodiesOverlapByTheirVerticalRanges()
        {
            bool Overlap(long c1, long h1, long c2, long h2) => (bool)typeof(InterimLocomotion)
                .GetMethod("BodiesOverlap", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { c1, h1, c2, h2 });
            Assert.That((Overlap(0, M / 2, M, M / 2), Overlap(0, M / 2, M - 1, M / 2)), Is.EqualTo((false, true)), "old geometry, H = 1 m");
            Assert.That((Overlap(M, M / 2, 0, M / 2), Overlap(M - 1, M / 2, 0, M / 2)), Is.EqualTo((false, true)), "either order");
            long h18 = 18 * M / 20, h12 = 12 * M / 20;
            Assert.That((Overlap(h18, h18, h18 + h18 + h12, h12), Overlap(h18, h18, h18 + h18 + h12 - 1, h12)), Is.EqualTo((false, true)), "1.8 m and 1.2 m bodies");

            // The crowd table takes each unit's half height and lift from its collider and the foot-anchored layers.
            SimSession s = Walk(Demo, new[] { Unit(1, 1, 3500, 7500), Unit(2, 2, 5500, 7500) });
            InterimLocomotion l = M3TestWorld.Locomotion(s);
            l.BindPlates(M3TestWorld.Sim(s).Entities.Resolve<VerticalState>(), Field<LayeredNav>(l, "layers"), Field<HeightField[]>(l, "tops"), default, 1u);
            EntityStore entities = M3TestWorld.Sim(s).Entities;
            entities.Set(entities.Resolve<Collider>(), M3TestWorld.Unit(s, 1), new Collider(SimFix.Ratio(1, 4), SimFix.FromRaw(18 * M / 10 + 1), 1u));
            entities.Set(entities.Resolve<Collider>(), M3TestWorld.Unit(s, 2), new Collider(SimFix.Ratio(1, 4), SimFix.FromRaw(12 * M / 10), 4u));
            s.StepOnce();
            Array table = Field<Array>(l, "provisional");
            long Member(int i, string name) => (long)table.GetValue(i).GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(table.GetValue(i));
            Assert.That((Member(0, "Half"), Member(0, "Lift")), Is.EqualTo(((18 * M / 10 + 1) / 2, (18 * M / 10 + 1) / 2)), "foot-anchored: lifted by half (floored)");
            Assert.That((Member(1, "Half"), Member(1, "Lift")), Is.EqualTo((12 * M / 10 / 2, 0L)), "another layer: centred on the foot");
        }

        // 7: the player walks up the stair with the direction keys and changes layer; at a floor edge with no seam none of
        // the three candidates leaves the floor.
        [Test]
        public void ThePlayerClimbsAndStaysOnTheFloor()
        {
            SimSession s = Player(9500, 8500, 0);
            var layers = new System.Collections.Generic.List<int> { 0 };
            void Hold(int x, int z, int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    s.SetPlayerInput(s.Tick + 1, new PlayerInput(x, z));
                    s.StepOnce();
                    if (layers[layers.Count - 1] != Vertical(s, 1).Layer) layers.Add(Vertical(s, 1).Layer);
                }
            }
            Hold(0, 1000, 10);
            Hold(1000, 0, 80);
            Assert.That(layers, Is.EqualTo(new[] { 0, 1, 2 }), "ground, stair, floor; at " + Show(M3TestWorld.Position(s)));
            Assert.That(M3TestWorld.Position(s).Y.Raw, Is.EqualTo(3 * M));

            SimSession f = Player(9800, 7500, 2);
            for (int i = 0; i < 40; i++)
            {
                f.SetPlayerInput(f.Tick + 1, new PlayerInput(-1000, i % 2 == 0 ? 300 : -300));
                f.StepOnce();
                SimVec3 p = M3TestWorld.Position(f);
                Assert.That((Vertical(f, 1).Layer, p.Y.Raw), Is.EqualTo(((ushort)2, 3 * M)), "tick " + i);
                Assert.That(p.X.Raw, Is.GreaterThanOrEqualTo(9 * M + M / 4 - 1), "the footprint stays on the floor, tick " + i);
            }
        }

        // A player world in the demo house: the player (key 1) and an enemy far off on the ground; a match that does not end.
        private static SimSession Player(long xMm, long zMm, int layer)
        {
            var units = new[] { new UnitSpawn(1, 0, 1, xMm, zMm, 3000, 1, layer: layer), new UnitSpawn(10, 0, 2, 22500, 2500, 1, 2) };
            var points = new[]
            {
                new ControlPointDef(1, SimVec3.Metres(5, 0, 13), SimFix.FromInt(2), 1, true, SimVec3.Metres(5, 0, 13)),
                new ControlPointDef(90, SimVec3.Metres(19, 0, 13), SimFix.FromInt(2), 2), new ControlPointDef(91, SimVec3.Metres(12, 0, 2), SimFix.FromInt(2)),
            };
            MatchPack pack = RespawnWorld.Pack(points, new[] { new SpawnAnchor(1, SimVec3.Metres(2, 0, 8)), new SpawnAnchor(2, SimVec3.Metres(22, 0, 8)) }, 300);
            WorldRecipe recipe = RespawnWorld.Recipe(units, pack).WithPlatesPreview();
            SimSession s = null;
            Assert.DoesNotThrow(() => s = SimSession.Create(recipe, Demo), "a match and combat on a plate map without a pack");
            return s;
        }

        // 9 (M-i 2): plates too high to block the ground (bottom 2.5 m over ground of at most 0.5 m) and with no seams change
        // nothing for units given ground orders. Ground slope <= 1/1 and H = 1 m: within 0.5 m horizontally the heights differ
        // by at most 0.5 m < h1 + h2 = 1 m, so the vertical rule always finds the bodies overlapping, as without plates.
        [Test]
        public void HighPlatesChangeNothingOnTheGround()
        {
            int[][] heights = Enumerable.Range(0, 7).Select(j => Enumerable.Range(0, 13).Select(i => (int)((i + j) % 3 * (M / 4))).ToArray()).ToArray();
            MapDesc With(bool plates) => Ml4Maps.Make(0, 12, 6, heights, plates ? new[] { Ml4Maps.Flat(1, 6, 1, 4, 3, 3 * M) } : new MapPlate[0]);
            var units = new[] { Unit(1, 1, 500, 500), Unit(2, 2, 11500, 5500), Unit(3, 3, 3500, 1500), Unit(4, 4, 8500, 2500) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 11500, 2500), BridgeCommand.MoveTo(2, 2, 500, 1500), BridgeCommand.MoveTo(3, 3, 9500, 4500),
                BridgeCommand.MoveTo(40, 4, 6500, 1500), BridgeCommand.MoveTo(60, 1, 7500, 5500) };
            SimSession Make(bool plates) => SimSession.Create(Recipe(units, script, preview: plates).WithPack(Wp23.Pack()), Bytes(With(plates)));
            SimSession a = Make(true), b = Make(false);
            for (int tick = 1; tick <= 300; tick++)
            {
                a.StepOnce(); b.StepOnce();
                for (ulong key = 1; key <= 4; key++)
                    Assert.That(M3TestWorld.Position(a, key), Is.EqualTo(M3TestWorld.Position(b, key)), "unit " + key + " at tick " + tick);
            }
            Assert.That(M3TestWorld.Position(a, 1).X.Raw, Is.GreaterThan(5 * M), "the units did move");
        }

        // 10: a unit on the first floor walks over a ground wall whose top meets the floor's bottom; a ground unit walks under
        // a wall standing on the first floor.
        [Test]
        public void WallsBlockOnlyTheirOwnLevel()
        {
            MapDesc d = PlateDemoMap.Desc();
            var walls = d.Walls.Concat(new[]
            {
                new MapWall(6, "wall", "walls", new MapXZ(11, 7), new MapXZ(1, 1), 5 * M / 2),
                new MapWall(7, "wall", "walls", new MapXZ(12, 8), new MapXZ(1, 1), 2 * M, baseRaw: 3 * M),
            }).ToArray();
            byte[] map = Bytes(new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null,
                d.Plates.ToArray(), d.Cutouts.ToArray(), walls, d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(), d.Run, d.Owners.ToArray(),
                null, null, d.Stamps.ToArray(), d.Removed.ToArray(), new MapNext(8, 3, 3, 5, 3, 2)));
            var units = new[] { Unit(1, 1, 9500, 7500, 2), Unit(2, 2, 14500, 8500) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 13500, 7500, 2), BridgeCommand.MoveTo(2, 2, 9500, 8500) };
            SimSession s = Walk(map, units, script);
            for (int t = 0; t < 120; t++)
            {
                s.StepOnce();
                Assert.That((M3TestWorld.Position(s, 1).Z.Raw, M3TestWorld.Position(s, 2).Z.Raw), Is.EqualTo((7 * M + M / 2, 8 * M + M / 2)), "straight, tick " + t);
            }
            Assert.That((M3TestWorld.Position(s, 1), M3TestWorld.Position(s, 2)), Is.EqualTo((P(13500, 3 * M, 7500), P(9500, 0, 8500))),
                Show(M3TestWorld.Position(s, 1)) + " " + Show(M3TestWorld.Position(s, 2)));
        }
    }
}
