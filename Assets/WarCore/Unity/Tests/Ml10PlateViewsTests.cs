using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // ML-10 acceptance 7: the plate demo's drawn plates against PlateMesh and Sim (TerrainHost.SelfCheck, rule "plates"),
    // and the layer cut on the drawn plates, walls and units.
    public sealed class Ml10PlateViewsTests
    {
        private sealed class Scene : IDisposable
        {
            public SimSession Session;
            public GameObject Map, Units;
            public PlateViews Plates;

            public string Check() => TerrainHost.SelfCheck(Session.Map, Session, Session.Current, Map.transform, Units.transform);

            public void Dispose()
            {
                if (Map != null)
                    foreach (MeshFilter f in Map.GetComponentsInChildren<MeshFilter>())
                        if (f.sharedMesh != null && f.name.StartsWith(PlateViews.Prefix, StringComparison.Ordinal))
                            UnityEngine.Object.DestroyImmediate(f.sharedMesh);
                if (Map != null) UnityEngine.Object.DestroyImmediate(Map);
                if (Units != null) UnityEngine.Object.DestroyImmediate(Units);
            }
        }

        private static Scene Build()
        {
            SimSession session = SimSession.Create(PlateDemo.Recipe(), PlateDemo.MapBytes());
            var scene = new Scene { Session = session, Map = MapView.Build(session.Map, null, null), Units = new GameObject("ML-10 units") };
            scene.Plates = new PlateViews(scene.Map.transform, session.Map.Plates, session.Map, null, null);
            new PointViews(scene.Units.transform, ViewGround.For(session.Map)).Apply(session.Current);
            return scene;
        }

        private static string[] Rules(string result)
        {
            var seen = new SortedSet<string>();
            foreach (string line in result.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) seen.Add(line.Split(' ')[0]);
            return new List<string>(seen).ToArray();
        }

        [Test]
        public void TheDemoIsClean()
        {
            using (Scene s = Build())
            {
                Assert.That(s.Check(), Is.EqualTo(""));
                Assert.That(s.Map.transform.Find(PlateViews.Prefix + "1"), Is.Not.Null, "the stair");
                Assert.That(s.Map.transform.Find(PlateViews.Prefix + "2"), Is.Not.Null, "the first floor");
            }
        }

        [Test]
        public void AMovedPlateIsReported()
        {
            using (Scene s = Build())
            {
                Mesh mesh = s.Map.transform.Find(PlateViews.Prefix + "2").GetComponent<MeshFilter>().sharedMesh;
                Vector3[] v = mesh.vertices;
                v[0].y += 0.25f;
                mesh.vertices = v;
                Assert.That(Rules(s.Check()), Is.EqualTo(new[] { "plates" }));
                StringAssert.Contains("plate 2: vertex 0 (top of solid cell 0) is off", s.Check());
            }
        }

        [Test]
        public void AMissingPlateIsReported()
        {
            using (Scene s = Build())
            {
                UnityEngine.Object.DestroyImmediate(s.Map.transform.Find(PlateViews.Prefix + "1").gameObject);
                Assert.That(s.Check(), Is.EqualTo("plates plate 1: missing\n"));
            }
        }

        // Under the first floor the floor is not rendered; on the floor it is.
        [Test]
        public void TheCutHidesTheFloorOverThePlayer()
        {
            using (Scene s = Build())
            {
                var down = new ViewEntity(1, 0, 11.5f, 0f, 7.5f, team: 1);
                s.Plates.Apply(LayerCut.Run(s.Session.Map.Plates, s.Session.Map, down, new IsoCamera(12f).Forward));
                Assert.That(s.Map.transform.Find(PlateViews.Prefix + "2").GetComponent<Renderer>().enabled, Is.False);
                var up = new ViewEntity(1, 0, 11.5f, 3f, 7.5f, team: 1, layer: 2);
                s.Plates.Apply(LayerCut.Run(s.Session.Map.Plates, s.Session.Map, up, new IsoCamera(12f).Forward));
                Assert.That(s.Map.transform.Find(PlateViews.Prefix + "2").GetComponent<Renderer>().enabled, Is.True);
            }
        }
    }
}
