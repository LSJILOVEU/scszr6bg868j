using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // SCALE-2a, the Unity half: the cubes (EntityViews), the showcase soldiers, the pinned lean, the shot lines, the hand, the
    // chips and the flash are drawn at the simulation's sizes in a world with a body, and exactly as before in one without.
    public sealed class Scale2aViewsTests
    {
        private const float Eps = 1e-4f;

        private static ShowcaseStage Stage(string id)
        {
            if (id == "house") return Showcase.LegacyHouse; // AIM-2: the house stage now has a body; the old house (rwr-house) has none
            foreach (ShowcaseStage stage in Showcase.Stages)
                if (stage.Id == id) return stage;
            throw new ArgumentException(id);
        }

        private static ShowcaseScene Scripted(string id)
        {
            var scene = new ShowcaseScene(Stage(id));
            foreach (var item in scene.Stage.Script()) scene.Session.SetPlayerInput(item.AtTick, item.Input);
            return scene;
        }

        private static void Run(ShowcaseScene scene, long toTick)
        {
            while (scene.Session.Tick < toTick && !scene.Session.IsFinished)
            {
                scene.Session.StepOnce();
                scene.Stepped();
            }
            scene.Show(1f, null);
        }

        private static Transform Soldier(ShowcaseScene scene, ulong key) => scene.Root.transform.Find(ShowcaseUnits.Prefix + key);

        // The highest point drawn of a soldier's body parts (marks and lines left out), over its feet.
        private static float Highest(Transform soldier, float feet)
        {
            float top = float.NegativeInfinity;
            foreach (string part in new[] { "Body", "Head", "Gun", "Blade", ShowcaseUnits.PackName })
            {
                Transform t = soldier.Find(part);
                if (t != null && t.gameObject.activeSelf) top = Mathf.Max(top, t.GetComponent<Renderer>().bounds.max.y);
            }
            return top - feet;
        }

        // S2 and S5: with sizes a cube is the body as the simulation has it (2r x Top x 2r on its feet) tick by tick, and so is
        // its silhouette; without sizes the old 0.5 x 1 x 0.5 cube lifted 0.5.
        [Test]
        public void S5_CubesAreTheBodyAsTheSimulationHasIt()
        {
            SimSession session = SimSession.Create(StanceDemo.Recipe(), StanceDemo.MapBytes());
            BodySizes sizes = BodySizes.Of(session);
            var root = new GameObject("scale-2a cubes");
            try
            {
                var views = new EntityViews(root.transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, new[] { Color.white, Color.red, Color.blue }, sizes);
                var covered = new HashSet<ulong> { 5 };
                bool crouched = false;
                while (session.Tick < 90)
                {
                    session.StepOnce();
                    FrameView frame = session.Current;
                    views.Apply(frame, frame, 0f, null, covered);
                    Assert.That(frame.TryGet(5, out ViewEntity e), Is.True);
                    Transform cube = root.transform.Find("Unit 5");
                    float top = sizes.Top(e);
                    Assert.That(cube.localScale.y, Is.EqualTo(top).Within(Eps), "tick " + session.Tick);
                    Assert.That(cube.localScale.x, Is.EqualTo(0.5f).Within(Eps));
                    Assert.That(cube.position.y, Is.EqualTo(e.Y + top / 2).Within(Eps));
                    Transform silhouette = root.transform.Find(EntityViews.SilhouettePrefix + 5);
                    Assert.That(silhouette.localScale, Is.EqualTo(cube.localScale));
                    Assert.That(silhouette.position.y, Is.EqualTo(cube.position.y).Within(Eps));
                    if (e.Stance == 1)
                    {
                        crouched = true;
                        Assert.That(cube.localScale.y, Is.EqualTo(1.0f).Within(Eps));
                        Assert.That(cube.position.y, Is.EqualTo(e.Y + 0.5f).Within(Eps));
                    }
                    else Assert.That(cube.localScale.y, Is.EqualTo(1.75f).Within(Eps));
                }
                Assert.That(crouched, Is.True);
                UnityEngine.Object.DestroyImmediate(root);
                root = new GameObject("scale-2a old cubes");
                var old = new EntityViews(root.transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, new[] { Color.white, Color.red, Color.blue });
                old.Apply(session.Current, session.Current, 0f, null, covered);
                Transform before = root.transform.Find("Unit 5");
                Assert.That(session.Current.TryGet(5, out ViewEntity five), Is.True);
                Assert.That(before.localScale, Is.EqualTo(new Vector3(0.5f, 1f, 0.5f)));
                Assert.That(before.position.y, Is.EqualTo(five.Y + 0.5f).Within(Eps));
                Assert.That(root.transform.Find(EntityViews.SilhouettePrefix + 5).localScale, Is.EqualTo(new Vector3(0.5f, 1f, 0.5f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // S5b: the house, terrain and melee stages (no body) draw SHOW-1's soldier exactly: every part where it was.
        [TestCase("house")]
        [TestCase("terrain")]
        [TestCase("melee")]
        public void S5b_AStageWithoutABodyDrawsTheOldSoldier(string id)
        {
            using (ShowcaseScene scene = Scripted(id))
            {
                Assert.That(scene.Sizes, Is.Null);
                foreach (ViewEntity e in scene.Session.Current.Entities)
                {
                    Transform s = Soldier(scene, e.Key);
                    Assert.That(s.Find(ShowcaseUnits.PackName), Is.Null, "no pack");
                    Assert.That(s.Find("Body").localPosition, Is.EqualTo(new Vector3(0f, 0.85f, 0f)));
                    Assert.That(s.Find("Body").localScale, Is.EqualTo(new Vector3(0.5f, 0.85f, 0.5f)));
                    Assert.That(s.Find("Head").localPosition, Is.EqualTo(new Vector3(0f, 1.7f - 0.12f, 0f)));
                    Assert.That(s.Find("Head").localScale, Is.EqualTo(new Vector3(0.3f, 0.3f, 0.3f)));
                    Transform gun = s.Find("Gun"), blade = s.Find("Blade");
                    if (gun != null)
                    {
                        Assert.That(gun.localPosition, Is.EqualTo(new Vector3(0.2f, 1.25f, 0.45f)));
                        Assert.That(gun.localScale, Is.EqualTo(new Vector3(0.08f, 0.1f, 0.9f)));
                    }
                    else
                    {
                        Assert.That(blade.localPosition, Is.EqualTo(new Vector3(0.28f, 1.3f, 0.5f)));
                        Assert.That(blade.localScale, Is.EqualTo(new Vector3(0.05f, 0.1f, 1.1f)));
                    }
                    Assert.That(s.Find("Shield").localPosition, Is.EqualTo(new Vector3(-0.1f, 1.05f, 0.42f)));
                    Assert.That(s.Find("Shield").localScale, Is.EqualTo(new Vector3(0.7f, 0.9f, 0.08f)));
                    Assert.That(s.Find("Nerve").localPosition, Is.EqualTo(new Vector3(0f, 1.7f + 0.35f, 0f)));
                    Assert.That(s.Find("Nerve").localScale, Is.EqualTo(new Vector3(0.28f, 0.28f, 0.28f)));
                    Assert.That(s.Find("Leader").localPosition, Is.EqualTo(new Vector3(0f, 1.7f + 0.8f, 0f)));
                    Assert.That(s.Find("Leader").localScale, Is.EqualTo(new Vector3(0.3f, 0.3f, 0.3f)));
                }
            }
        }

        // S6 and S12: the lineup at tick 150: standing, crouched and prone. Crouched head top 1.0 and gun 0.75; prone nothing
        // higher than 0.5; parts by the table.
        [Test]
        public void S6_TheLineupIsDrawnByTheTable()
        {
            using (ShowcaseScene scene = Scale2aLineupHost.Open())
            {
                Assert.That(scene.Sizes, Is.Not.Null);
                Run(scene, 150);
                FrameView frame = scene.Session.Current;
                BodySizes sizes = scene.Sizes;
                for (ulong key = 1; key <= 3; key++)
                {
                    Assert.That(frame.TryGet(key, out ViewEntity e), Is.True);
                    Assert.That(e.Stance, Is.EqualTo((int)key - 1));
                    Transform s = Soldier(scene, key);
                    DrawnBody d = ShowcaseUnits.Drawn(sizes, e);
                    Assert.That(s.Find("Head").localPosition.y, Is.EqualTo(d.Head.Y).Within(Eps), "soldier " + key);
                    Assert.That(s.Find("Gun").localPosition.y, Is.EqualTo(d.Gun.Y).Within(Eps), "soldier " + key);
                    Assert.That(s.Find("Gun").localScale.z, Is.EqualTo(0.84f).Within(Eps), "soldier " + key);
                    Assert.That(s.Find(ShowcaseUnits.PackName).localScale.x, Is.EqualTo(0.35f).Within(Eps), "soldier " + key);
                    Assert.That(s.localScale, Is.EqualTo(Vector3.one));
                }
                Transform standing = Soldier(scene, 1), crouched = Soldier(scene, 2), prone = Soldier(scene, 3);
                Assert.That(Highest(standing, 0f), Is.EqualTo(1.75f).Within(1e-3f));
                Assert.That(crouched.Find("Head").GetComponent<Renderer>().bounds.max.y, Is.EqualTo(1.0f).Within(1e-3f));
                Assert.That(crouched.Find("Gun").position.y, Is.EqualTo(0.75f).Within(1e-3f));
                Assert.That(Highest(prone, 0f), Is.LessThanOrEqualTo(0.5f + 1e-3f));
                Assert.That(prone.Find("Body").GetComponent<Renderer>().bounds.size.z + prone.Find("Body").GetComponent<Renderer>().bounds.size.x,
                    Is.GreaterThan(1.4f), "lying along the ground");
                Assert.That(scene.Root.transform.Find(ShowcaseUnits.RingName).GetComponent<LineRenderer>().enabled, Is.False);
                string rows = Scale2aLineupHost.Rows(scene, "x.png");
                Assert.That(rows, Does.Contain(",1,0,1.750\n").And.Contain(",2,1,1.000\n").And.Contain(",3,2,0.500\n"));
            }
        }

        // S7: a 2 m cube fitted to a 1.75 m body is drawn 1.75 m tall.
        [Test]
        public void S7_AModelIsFittedToTheBodyByItsBounds()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                cube.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
                float fit = ShowcaseUnits.FitModel(cube, 1.75f);
                Assert.That(fit, Is.EqualTo(0.875f).Within(1e-5f));
                Assert.That(cube.GetComponent<Renderer>().bounds.size.y, Is.EqualTo(1.75f).Within(Eps));
            }
            finally { UnityEngine.Object.DestroyImmediate(cube); }
        }

        // S8: lines and tracers run between the simulation's heights with sizes, the end lifted to the ground; old heights without.
        [Test]
        public void S8_ShotsAreDrawnAtTheSimulationsHeights()
        {
            var root = new GameObject("scale-2a shots");
            try
            {
                SimSession bodied = SimSession.Create(StanceDemo.Recipe(), StanceDemo.MapBytes());
                var lines = new ShotLines(root.transform, new[] { Color.white }, 1.5f, null, BodySizes.Of(bodied), 0f);
                var oldLines = new ShotLines(root.transform, new[] { Color.white }, 1.5f);
                var fire = new ViewFire(1f, 2f, 9f, 2f, 1, false, 0.75f, 1.5f);
                var under = new ViewFire(1f, 2f, 9f, 2f, 1, false, 1.5f, -0.4f);
                Assert.That(lines.Start(fire), Is.EqualTo(new Vector3(1f, 0.75f, 2f)));
                Assert.That(lines.End(fire), Is.EqualTo(new Vector3(9f, 1.5f, 2f)));
                Assert.That(lines.End(under).y, Is.EqualTo(0f), "not below the flat floor");
                Assert.That(oldLines.Start(fire).y, Is.EqualTo(1.5f));
                Assert.That(oldLines.End(under).y, Is.EqualTo(1.5f));
                var fx = new ShowcaseFx(root.transform);
                var shots = new ShowcaseShots(root.transform, new[] { 0xffffff }, null, fx, BodySizes.Of(bodied));
                var oldShots = new ShowcaseShots(root.transform, new[] { 0xffffff }, null, fx);
                Vector3 from = shots.From(fire, 0f);
                Assert.That(from, Is.EqualTo(new Vector3(1f, 0.75f, 2f)));
                Assert.That(shots.To(fire, from, 0f, 0f), Is.EqualTo(new Vector3(9f, 1.5f, 2f)));
                Assert.That(shots.To(under, shots.From(under, 0f), 0f, 0f).y, Is.EqualTo(0f), "not below the flat floor");
                Assert.That(oldShots.From(fire, 0f).y, Is.EqualTo(1.4f), "the old muzzle over the feet");
                Assert.That(oldShots.To(under, oldShots.From(under, 0f), 0f, 0f).y, Is.EqualTo(1.4f), "level on a flat map");
                Assert.That(shots.Reach, Is.EqualTo(0.84f).Within(Eps));
                Assert.That(oldShots.Reach, Is.EqualTo(0.9f));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // S9 (replaces SHOW-1's "a pinned soldier ducks"): with a body a pinned soldier who is not walking keeps his height
        // and leans 15..25 degrees forward, his highest point at least 0.9 of his top; an unpressed one does not lean. Without
        // a body the old duck stays.
        [Test]
        public void S9_APinnedSoldierLeansAndKeepsHisHeight()
        {
            using (ShowcaseScene scene = Scripted("battle"))
            {
                BodySizes sizes = scene.Sizes;
                Assert.That(sizes, Is.Not.Null);
                int pinned = 0, calm = 0;
                for (int round = 0; round < 12 && pinned == 0; round++)
                {
                    Run(scene, 330 + 30 * round);
                    FrameView previous = scene.Session.Previous, current = scene.Session.Current;
                    foreach (ViewEntity e in current.Entities)
                    {
                        if (!e.Alive || !previous.TryGet(e.Key, out ViewEntity was)) continue;
                        float moved = Mathf.Sqrt((e.X - was.X) * (e.X - was.X) + (e.Z - was.Z) * (e.Z - was.Z)) * SimSession.TickRate;
                        if (moved > 0.5f) continue;
                        Transform s = Soldier(scene, e.Key);
                        Assert.That(s.localScale.y, Is.EqualTo(1f).Within(Eps), "soldier " + e.Key);
                        float lean = Mathf.DeltaAngle(0f, s.localEulerAngles.x);
                        if (e.Nerve == 1)
                        {
                            Assert.That(lean, Is.InRange(15f, 25f), "soldier " + e.Key);
                            Assert.That(Highest(s, e.Y), Is.GreaterThanOrEqualTo(0.9f * sizes.Top(e)), "soldier " + e.Key);
                            pinned++;
                        }
                        else if (e.Nerve == 0)
                        {
                            Assert.That(Mathf.Abs(lean), Is.LessThan(Eps), "soldier " + e.Key);
                            calm++;
                        }
                    }
                }
                Assert.That(pinned, Is.GreaterThan(0), "somebody is pinned within twenty seconds");
                Assert.That(calm, Is.GreaterThan(0));
            }
            using (ShowcaseScene scene = Scripted("house"))
            {
                Assert.That(scene.Sizes, Is.Null);
                ViewEntity any = scene.Session.Current.Entities[0];
                var pinnedFrame = new FrameView(scene.Session.Tick, new[]
                {
                    new ViewEntity(any.Key, any.Archetype, any.X, any.Y, any.Z, any.Squad, any.Team, true, 1, any.Layer, any.Band, any.Stance),
                });
                scene.Units.Apply(pinnedFrame, pinnedFrame, 1f, null, null, null, null, false);
                Assert.That(Soldier(scene, any.Key).localScale.y, Is.EqualTo(ShowcaseUnits.DuckScale).Within(Eps), "no body: the old duck");
                Assert.That(ShowcaseUnits.DuckScale, Is.EqualTo(0.62f));
            }
        }

        // S13: the grenade's hand is the body's muzzle (1.5 standing in the battle stage), chips fly at 0.63 of the top, the
        // flash sits a gun's length ahead; without a body 1.5, 1.1 and 0.9 as before.
        [Test]
        public void S13_HandChipsAndFlashFollowTheBody()
        {
            using (ShowcaseScene scene = Scripted("battle"))
            {
                ulong key = scene.Session.ControlledKey.Value;
                Assert.That(scene.Session.Current.TryGet(key, out ViewEntity me), Is.True);
                Assert.That(scene.HandHeight(me), Is.EqualTo(scene.Sizes.Muzzle(me)));
                Assert.That(scene.HandHeight(me), Is.EqualTo(1.5f).Within(Eps));
                Assert.That(scene.ChipsHeight(me), Is.EqualTo(0.63f * scene.Sizes.Top(me)).Within(Eps));
                Assert.That(scene.ChipsHeight(me), Is.EqualTo(1.1025f).Within(Eps));
                Assert.That(scene.Shots.Reach, Is.EqualTo(DrawnParts.GunLength(scene.Sizes.StandingHeight)).Within(Eps));
            }
            using (ShowcaseScene scene = Scripted("house"))
            {
                ulong key = scene.Session.ControlledKey.Value;
                Assert.That(scene.Session.Current.TryGet(key, out ViewEntity me), Is.True);
                Assert.That(scene.HandHeight(me), Is.EqualTo(1.5f));
                Assert.That(scene.ChipsHeight(me), Is.EqualTo(1.1f));
                Assert.That(scene.Shots.Reach, Is.EqualTo(0.9f));
            }
        }
    }
}
