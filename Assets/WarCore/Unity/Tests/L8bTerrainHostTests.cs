using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // L8b: TerrainHost.SelfCheck over the chain scene objects -> view data -> Sim -> control points. Each broken scene
    // reports exactly the rule it breaks (the first word of every line), and the clean scenes report nothing.
    public sealed class L8bTerrainHostTests
    {
        private sealed class Scene : IDisposable
        {
            public SimSession Session;
            public ViewMap Drawn;
            public FrameView Frame;
            public GameObject Map;
            public GameObject Points;

            public string Check() => TerrainHost.SelfCheck(Drawn, Session, Frame, Map.transform, Points.transform);

            public void Dispose()
            {
                Transform ground = Map == null ? null : Map.transform.Find("Ground");
                MeshFilter filter = ground == null ? null : ground.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name != "Cube")
                    UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                if (Map != null) UnityEngine.Object.DestroyImmediate(Map);
                if (Points != null) UnityEngine.Object.DestroyImmediate(Points);
            }
        }

        // Map view built once from drawn (default: the session's own map), discs applied once from frame (default: Current).
        private static Scene Build(SimSession session, ViewMap drawn = null, FrameView frame = null)
        {
            drawn = drawn ?? session.Map;
            frame = frame ?? session.Current;
            var scene = new Scene { Session = session, Drawn = drawn, Frame = frame };
            scene.Map = MapView.Build(drawn, null, null);
            scene.Points = new GameObject("L8b points");
            new PointViews(scene.Points.transform, ViewGround.For(drawn)).Apply(frame);
            return scene;
        }

        private static SimSession Demo() => SimSession.Create(ContentLayers.Terrain("rwr-terrain-demo"), TerrainBake.DemoMapBytes());

        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        private static void Only(string result, params string[] rules)
        {
            var seen = new SortedSet<string>();
            foreach (string line in result.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                seen.Add(line.Split(' ')[0]);
            Assert.That(seen, Is.EqualTo(new SortedSet<string>(rules)), result);
        }

        [Test]
        public void L8b_DemoIsClean()
        {
            using (Scene s = Build(Demo()))
            {
                Assert.That(s.Check(), Is.Empty);
                Vector3 p1 = s.Points.transform.Find("Point 1").position, p2 = s.Points.transform.Find("Point 2").position;
                Assert.That(new[] { Bits(p1.x), Bits(p1.y), Bits(p1.z) }, Is.EqualTo(new[] { Bits(22.5f), Bits(5.75f + 0.02f), Bits(16.5f) }));
                Assert.That(new[] { Bits(p2.x), Bits(p2.y), Bits(p2.z) }, Is.EqualTo(new[] { Bits(38.5f), Bits(0f + 0.02f), Bits(8.5f) }));
            }
        }

        [Test]
        public void L8b_DiscRaised_ReportsDisc()
        {
            using (Scene s = Build(Demo()))
            {
                s.Points.transform.Find("Point 1").position += new Vector3(0f, 0.01f, 0f);
                Only(s.Check(), "disc");
            }
        }

        [Test]
        public void L8b_GroundVertexMoved_ReportsGround()
        {
            using (Scene s = Build(Demo()))
            {
                Mesh mesh = s.Map.transform.Find("Ground").GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices;
                vertices[800] += new Vector3(0f, 1f, 0f);
                mesh.vertices = vertices;
                Only(s.Check(), "ground");
            }
        }

        [Test]
        public void L8b_OtherMapDrawn_ReportsTerrain()
        {
            using (Scene s = Build(Demo(), ViewMap.Parse(TerrainBake.CheckerBytes(8))))
                Only(s.Check(), "terrain");
        }

        [Test]
        public void L8b_DrawnPointMoved_ReportsPoints()
        {
            SimSession session = Demo();
            FrameView f = session.Current;
            var points = f.Points.ToArray();
            ViewPoint p = points[0];
            points[0] = new ViewPoint(p.Id, p.X, p.Z + 1f, p.Radius, p.Owner, p.Capturing, p.Progress);
            var frame = new FrameView(f.Tick, f.Entities.ToArray(), f.Fires, points, f.Match, f.Respawn);
            using (Scene s = Build(session, null, frame))
                Only(s.Check(), "points");
        }

        [Test]
        public void L8b_DiscDeleted_ReportsMissingDisc()
        {
            using (Scene s = Build(Demo()))
            {
                UnityEngine.Object.DestroyImmediate(s.Points.transform.Find("Point 2").gameObject);
                Assert.That(s.Check(), Is.EqualTo("disc 2 missing\n"));
            }
        }

        [Test]
        public void L8b_RootMoved_ReportsRootOnly()
        {
            using (Scene s = Build(Demo()))
            {
                s.Map.transform.position += new Vector3(0f, 1f, 0f);
                Only(s.Check(), "root");
            }
        }

        [Test]
        public void L8b_WallMovedOrAdded_ReportsWall()
        {
            using (Scene s = Build(Demo()))
            {
                var walls = new List<Transform>();
                foreach (Transform child in s.Map.transform)
                    if (child.name.StartsWith("Wall ", StringComparison.Ordinal)) walls.Add(child);
                Assert.That(walls.Count, Is.EqualTo(1), "the demo map has one wall");
                walls[0].localPosition += new Vector3(0.5f, 0f, 0f);
                Only(s.Check(), "wall");
                walls[0].localPosition -= new Vector3(0.5f, 0f, 0f);
                Assert.That(s.Check(), Is.Empty, "wall restored");
                var extra = GameObject.CreatePrimitive(PrimitiveType.Cube);
                extra.name = "Wall 9:9";
                extra.transform.SetParent(s.Map.transform, false);
                Only(s.Check(), "wall");
            }
        }

        [Test]
        public void L8b_HudStartsWithHudLines()
        {
            SimSession session = Demo();
            string hud = TerrainHost.Hud(session.Current, session.StateHash, session.Map);
            Assert.That(hud, Does.StartWith("Tick 0\nStateHash " + session.StateHash.ToString("X16") + "\n"));
        }

        [Test]
        public void L8b_ShotPrefixIsAPlainFileName()
        {
            foreach (string good in new[] { "L8b", "L8b-A3a", "L8b_x1" })
                Assert.That(TerrainHost.IsShotPrefix(good), Is.True, good);
            foreach (string bad in new[] { null, "", "x", "l8b", "L8b/x", "L8b\\x", "L8b/../x", "/tmp/L8b", "L8b.x", "L8b x" })
                Assert.That(TerrainHost.IsShotPrefix(bad), Is.False, bad ?? "null");
        }

        [Test]
        public void L8b_A3aReplicaIsClean()
        {
            using (Scene s = Build(SimSession.Create(A3aDemo.Recipe(), A3aDemo.MapBytes())))
                Assert.That(s.Check(), Is.Empty);
        }

        [Test]
        public void L8b_FlatMapCleanAndGroundShiftedInZ_ReportsGround()
        {
            using (Scene s = Build(SimSession.Create(DemoRecipes.M7(), DemoRecipes.M7MapBytes())))
            {
                Assert.That(s.Check(), Is.Empty);
                s.Map.transform.Find("Ground").localPosition += new Vector3(0f, 0f, 1f);
                Only(s.Check(), "ground");
            }
        }
    }
}
