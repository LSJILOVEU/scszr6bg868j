using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M2bMapSessionTests
    {
        private const string DemoFile = "Assets/StreamingAssets/maps/M2Demo.wcmap";
        private const double Radius = 0.25;

        private static byte[] DemoBytes => M2DemoMap.Bytes();

        [Test]
        public void CommittedDemoMap_EqualsGeneratorAndDecodes()
        {
            byte[] committed = File.ReadAllBytes(DemoFile);
            Assert.That(committed, Is.EqualTo(DemoBytes));
            Assert.That(WcMapReader.Read(committed).Walls.Count, Is.EqualTo(4));
        }

        [Test]
        public void VisibleModelAndCollisionAndNavigation_AgreeOnEveryCell()
        {
            byte[] bytes = DemoBytes;
            WcMap file = WcMapReader.Read(bytes);
            SimSession session = SimSession.Create(DemoRecipes.M2b(), bytes);
            ViewMap view = session.Map;
            Assert.That((view.Width, view.Depth), Is.EqualTo((64, 32)));
            Assert.That(view.Walls.Length, Is.EqualTo(file.Walls.Count));
            // Wall model against the file, key by key, with the geometry computed here from raw values.
            for (int i = 0; i < file.Walls.Count; i++)
            {
                WcMapWall wall = file.Walls[i];
                ViewWall shown = view.Walls[i];
                Assert.That((shown.Chunk, shown.Placement), Is.EqualTo((wall.Chunk, wall.Placement)));
                Assert.That(shown.CenterX, Is.EqualTo((file.Min.X.Raw + wall.LocalCenter.X.Raw) / 65536.0).Within(1e-4));
                Assert.That(shown.CenterY, Is.EqualTo((file.FlatHeight.Raw + wall.LocalCenter.Y.Raw) / 65536.0).Within(1e-4));
                Assert.That(shown.CenterZ, Is.EqualTo((file.Min.Z.Raw + wall.LocalCenter.Z.Raw) / 65536.0).Within(1e-4));
                Assert.That(shown.SizeX, Is.EqualTo(wall.HalfX.Raw * 2 / 65536.0).Within(1e-4));
                Assert.That(shown.SizeY, Is.EqualTo(wall.Height.Raw / 65536.0).Within(1e-4));
                Assert.That(shown.SizeZ, Is.EqualTo(wall.HalfZ.Raw * 2 / 65536.0).Within(1e-4));
            }
            // Visible footprint, collision and navigation per cell centre.
            int mismatches = 0, blocked = 0;
            for (int z = 0; z < view.Depth; z++)
                for (int x = 0; x < view.Width; x++)
                {
                    bool visible = Covers(view, x + 0.5, z + 0.5);
                    bool collides = session.BlockedCell(x, z);
                    bool nav = !view.IsPassable(x, z);
                    if (visible != collides || collides != nav) mismatches++;
                    if (nav) blocked++;
                }
            Assert.That(mismatches, Is.EqualTo(0));
            Assert.That(blocked, Is.GreaterThan(0));
        }

        [Test]
        public void Script_Unit1StopsAtWallUnit2Arrives_NeverPenetrating()
        {
            SimSession session = SimSession.Create(DemoRecipes.M2b(), DemoBytes);
            ViewMap view = session.Map;
            ViewEntity start1 = Unit(session, 1);
            double stopX = double.NaN;
            for (int tick = 1; tick <= 600; tick++)
            {
                session.StepOnce();
                foreach (ulong key in new ulong[] { 1, 2 })
                {
                    ViewEntity u = Unit(session, key);
                    foreach (ViewWall w in view.Walls.ToArray())
                        Assert.That(DistanceToWall(u, w), Is.GreaterThanOrEqualTo(Radius - 1e-6), $"tick={tick} unit={key}");
                }
                ViewEntity u1 = Unit(session, 1);
                if (tick == 150)
                {
                    Assert.That(u1.X, Is.GreaterThan(start1.X + 10), "unit 1 walked");
                    Assert.That(Unit(session, 2).X, Is.GreaterThan(4 + 10), "unit 2 walked");
                }
                if (tick == 300) stopX = u1.X;
                if (tick >= 300) Assert.That(u1.X, Is.EqualTo(stopX), $"tick={tick}");
            }
            // Contact is x = 24 - 0.25; the stop lies within one step (0.1 m) of it.
            Assert.That(stopX, Is.EqualTo(24 - Radius).Within(0.1 + 1e-4));
            Assert.That(Unit(session, 1).X, Is.LessThan(60));
            Assert.That(Unit(session, 2).X, Is.EqualTo(60f));
            Assert.That(Unit(session, 2).Z, Is.EqualTo(28f));
        }

        [Test]
        public void TwoIndependentRuns_ShareAll601StateHashes()
        {
            ulong[] first = Hashes(), second = Hashes();
            Assert.That(second, Is.EqualTo(first));
            TestContext.Out.WriteLine("M2b tick600 StateHash " + first[600].ToString("X16"));
        }

        [Test]
        public void NoMapPath_StillRunsM1Recipe()
        {
            var recipe = new WorldRecipe(41, MoverKind.Interim, new[] { new UnitSpawn(1, 0, 1, 0, 0, 3000) },
                new[] { BridgeCommand.MoveTo(1, 1, 10000, 0), BridgeCommand.MoveTo(150, 1, 10000, 10000) });
            SimSession session = SimSession.Create(recipe);
            Assert.That(session.Map, Is.Null);
            while (session.Tick < 600) session.StepOnce();
            TestContext.Out.WriteLine("M1 recipe tick600 StateHash " + session.StateHash.ToString("X16"));
            Assert.That(Unit(session, 1).X, Is.EqualTo(10f));
        }

        // Negative origin, floor at 3 m, two collision layers: units and commands follow the floor, masks combine.
        [Test]
        public void NegativeOriginRaisedFloorTwoLayers_BuildsAndBlocksOnTheFloor()
        {
            WcMap map = SmallMap(new WcMapLayer("walls", 1), new WcMapLayer("rocks", 2));
            byte[] bytes = WcMapWriter.Write(map);
            var recipe = new WorldRecipe(7, MoverKind.Interim, new[] { new UnitSpawn(1, 0, 1, -7000, -1000, 3000) },
                new[] { BridgeCommand.MoveTo(1, 1, 7000, -1000) });
            SimSession session = SimSession.Create(recipe, bytes);
            Assert.That(Unit(session, 1).Y, Is.EqualTo(3f));
            for (int z = 0; z < session.Map.Depth; z++)
                for (int x = 0; x < session.Map.Width; x++)
                    Assert.That(session.BlockedCell(x, z), Is.EqualTo(!session.Map.IsPassable(x, z)), $"cell {x},{z}");
            for (int i = 0; i < 300; i++) session.StepOnce();
            ViewEntity unit = Unit(session, 1);
            Assert.That(unit.Y, Is.EqualTo(3f));
            Assert.That(unit.X, Is.LessThan(-1.0f).And.GreaterThan(-3f), "stopped in front of the wall at x=-1");
        }

        [Test]
        public void WallLayerSharingTheMoverLayer_IsRejected()
        {
            WcMap map = SmallMap(new WcMapLayer("walls", 0), new WcMapLayer("rocks", 2));
            Assert.Throws<InvalidOperationException>(() => SimSession.Create(DemoRecipes.M2b(), WcMapWriter.Write(map)));
        }

        // 16 x 8 m map at (-8, floor 3, -4). One 2 x 8 wall at x in [-1,1) on layer a, one 2 x 2 block at (4,0) on layer b.
        private static WcMap SmallMap(WcMapLayer a, WcMapLayer b)
        {
            const int width = 16, depth = 8;
            SimFix floor = SimFix.FromInt(3);
            WcMapWall Wall(ulong id, string layer, int x, int z, int w, int d) => new WcMapWall(0, id, "wall", layer,
                new SimVec3(SimFix.FromInt(x + 8) + SimFix.Ratio(w, 2), SimFix.FromInt(1), SimFix.FromInt(z + 4) + SimFix.Ratio(d, 2)),
                SimFix.Ratio(w, 2), SimFix.FromInt(2), SimFix.Ratio(d, 2));
            var boxes = new (int x, int z, int w, int d)[] { (-1, -4, 2, 8), (4, 0, 2, 2) };
            var walls = new[] { Wall(1, a.Name, -1, -4, 2, 8), Wall(2, b.Name, 4, 0, 2, 2) };
            var bits = new byte[(width * depth + 7) / 8];
            for (int i = 0; i < width * depth; i++)
            {
                int cx = i % width - 8, cz = i / width - 4;
                bool hit = false;
                foreach (var (x, z, w, d) in boxes)
                    hit |= cx >= x && cx < x + w && cz >= z && cz < z + d;
                if (!hit) bits[i >> 3] |= (byte)(1 << (i & 7));
            }
            return new WcMap("small", "none", new SimVec3(SimFix.FromInt(-8), floor, SimFix.FromInt(-4)),
                new SimVec3(SimFix.FromInt(8), SimFix.FromInt(8), SimFix.FromInt(4)), SimFix.FromInt(16), floor,
                width, depth, new[] { a, b }, walls, bits);
        }

        private static ulong[] Hashes()
        {
            SimSession session = SimSession.Create(DemoRecipes.M2b(), DemoBytes);
            var list = new List<ulong> { session.StateHash };
            while (session.Tick < 600) { session.StepOnce(); list.Add(session.StateHash); }
            return list.ToArray();
        }

        private static ViewEntity Unit(SimSession session, ulong key)
        {
            Assert.That(session.Current.TryGet(key, out ViewEntity entity), Is.True);
            return entity;
        }

        private static bool Covers(ViewMap view, double x, double z)
        {
            foreach (ViewWall w in view.Walls.ToArray())
                if (Math.Abs(x - w.CenterX) <= w.SizeX / 2 && Math.Abs(z - w.CenterZ) <= w.SizeZ / 2) return true;
            return false;
        }

        private static double DistanceToWall(ViewEntity u, ViewWall w)
        {
            double dx = Math.Max(0, Math.Abs(u.X - w.CenterX) - w.SizeX / 2);
            double dz = Math.Max(0, Math.Abs(u.Z - w.CenterZ) - w.SizeZ / 2);
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
