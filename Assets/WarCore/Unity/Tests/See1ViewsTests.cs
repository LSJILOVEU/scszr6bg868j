using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // SEE-1 acceptance 11: the street demo drawn: the silhouettes of the covered team mates, the faded and the plain wall material, and the
    // self-check of the drawn map. Walls 0, 1, 2 are the 6 m building, the 9 m building and the sandbags; team mates are keys 2, 3, 4.
    public sealed class See1ViewsTests
    {
        private sealed class Scene : IDisposable
        {
            public SimSession Session;
            public GameObject Map, Units;
            public PlateViews Views;
            public EntityViews Entities;
            public Material Wall;
            private FrameView previous;

            public static Scene Build()
            {
                SimSession session = SimSession.Create(StreetDemo.Recipe(), StreetDemo.MapBytes());
                foreach (var item in StreetDemo.Script()) session.SetPlayerInput(item.AtTick, item.Input);
                var scene = new Scene { Session = session, Wall = new Material(Shader.Find("Standard")), Units = new GameObject("SEE-1 units") };
                scene.Map = MapView.Build(session.Map, new Material(Shader.Find("Standard")), scene.Wall);
                scene.Views = new PlateViews(scene.Map.transform, session.Map.Plates, session.Map, scene.Wall, scene.Wall);
                var palette = new[] { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f) };
                scene.Entities = new EntityViews(scene.Units.transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, palette);
                scene.previous = session.Current;
                return scene;
            }

            // Step to the tick and draw it the way PlateHost does (the default camera).
            public void Show(long tick)
            {
                while (Session.Tick < tick)
                {
                    previous = Session.Current;
                    Session.StepOnce();
                }
                var forward = new IsoCamera(12f).Forward;
                Session.Current.TryGet(StreetDemo.PlayerKey, out ViewEntity me);
                LayerCutResult cut = LayerCut.Run(Session.Map.Plates, Session.Map, me, forward, LayerCut.SeeRadius);
                HashSet<ulong> covered = Silhouettes.Run(Session.Map.Plates, Session.Map, cut, me, Session.Current.Entities, forward);
                Views.Apply(cut);
                Entities.Apply(previous, Session.Current, 1f, cut, covered);
            }

            public Transform Silhouette(ulong key) => Units.transform.Find(EntityViews.SilhouettePrefix + key);

            public Renderer WallRenderer(int index)
            {
                ViewWall v = Session.Map.Walls[index];
                return Map.transform.Find("Wall " + v.Chunk + ":" + v.Placement).GetComponent<Renderer>();
            }

            public void Dispose()
            {
                if (Map != null) UnityEngine.Object.DestroyImmediate(Map);
                if (Units != null) UnityEngine.Object.DestroyImmediate(Units);
                if (Wall != null) UnityEngine.Object.DestroyImmediate(Wall);
            }
        }

        private static bool Active(Transform t) => t != null && t.gameObject.activeSelf;

        [Test]
        public void TheStreetIsClean()
        {
            using (Scene s = Scene.Build())
                Assert.That(TerrainHost.SelfCheck(s.Session.Map, s.Session, s.Session.Current, s.Map.transform, s.Units.transform), Is.EqualTo(""));
        }

        // Tick 0: mates 2 and 3 stand behind the two buildings: both have a silhouette; mate 4 in the open and the enemies have none.
        // Tick 300: the 9 m building fades, so mate 3's silhouette is off and mate 2's (behind the 6 m building) stays on.
        [Test]
        public void CoveredTeamMatesHaveASilhouette()
        {
            using (Scene s = Scene.Build())
            {
                s.Show(0);
                Assert.That(new[] { Active(s.Silhouette(2)), Active(s.Silhouette(3)), s.Silhouette(4) == null }, Is.EqualTo(new[] { true, true, true }));
                Assert.That(new[] { s.Silhouette(10) == null, s.Silhouette(11) == null, s.Silhouette(12) == null, s.Silhouette(1) == null },
                    Is.EqualTo(new[] { true, true, true, true }));
                s.Show(300);
                Assert.That(new[] { Active(s.Silhouette(2)), s.Silhouette(3) != null && !Active(s.Silhouette(3)) }, Is.EqualTo(new[] { true, true }));
            }
        }

        // The silhouette is a flat see-through body at the unit's place.
        [Test]
        public void TheSilhouetteIsAtTheUnit()
        {
            using (Scene s = Scene.Build())
            {
                s.Show(0);
                Transform mate = s.Units.transform.Find("Unit 2");
                Assert.That(s.Silhouette(2).position, Is.EqualTo(mate.position));
                Material m = s.Silhouette(2).GetComponent<Renderer>().sharedMaterial;
                Assert.That(m.color.a, Is.EqualTo(0.6f).Within(0.001f));
            }
        }

        // Tick 0 every wall has the plain material; tick 150 the 6 m building has the see-through copy (alpha 0.3), the others the plain one.
        [Test]
        public void FadedWallsUseTheSeeThroughMaterial()
        {
            using (Scene s = Scene.Build())
            {
                s.Show(0);
                for (int w = 0; w < 3; w++) Assert.That(s.WallRenderer(w).sharedMaterial, Is.SameAs(s.Wall));
                s.Show(150);
                Material faded = s.WallRenderer(0).sharedMaterial;
                Assert.That(faded, Is.Not.SameAs(s.Wall));
                Assert.That(faded.color.a, Is.EqualTo(LayerCut.FadeAlpha).Within(0.001f));
                Assert.That(s.WallRenderer(1).sharedMaterial, Is.SameAs(s.Wall));
                Assert.That(s.WallRenderer(2).sharedMaterial, Is.SameAs(s.Wall));
            }
        }
    }
}
