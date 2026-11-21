using System;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // SHOW-1: the showcase scene as drawn. Every stage builds clean (TerrainHost.SelfCheck) with one soldier per unit and one
    // flag per point; shots, bullets, the fallen, the ducking, the camera and the grenade preview are read off the objects.
    public sealed class Show1ViewsTests
    {
        private static ShowcaseStage Stage(string id)
        {
            foreach (ShowcaseStage stage in Showcase.Stages)
                if (stage.Id == id) return stage;
            throw new ArgumentException(id);
        }

        // Step to a tick with the stage's script, drawing every drawEvery ticks and at the end, as pictures do (dt 0).
        private static void Run(ShowcaseScene scene, long toTick, int drawEvery = 30)
        {
            while (scene.Session.Tick < toTick && !scene.Session.IsFinished)
            {
                scene.Session.StepOnce();
                scene.Stepped();
                if (scene.Session.Tick % drawEvery == 0) scene.Show(1f, null);
            }
            scene.Show(1f, null);
        }

        private static ShowcaseScene Scripted(string id)
        {
            var scene = new ShowcaseScene(Stage(id));
            foreach (var item in scene.Stage.Script()) scene.Session.SetPlayerInput(item.AtTick, item.Input);
            return scene;
        }

        [TestCase("battle")]
        [TestCase("bullets")]
        [TestCase("house")]
        [TestCase("terrain")]
        [TestCase("melee")]
        public void AStageBuildsCleanWithASoldierPerUnitAndAFlagPerPoint(string id)
        {
            using (ShowcaseScene scene = Scripted(id))
            {
                Assert.That(scene.SelfCheck(), Is.EqualTo(""));
                FrameView frame = scene.Session.Current;
                Assert.That(scene.Units.Count, Is.EqualTo(frame.Entities.Length));
                foreach (ViewEntity e in frame.Entities)
                    Assert.That(scene.Root.transform.Find(ShowcaseUnits.Prefix + e.Key), Is.Not.Null, "soldier " + e.Key);
                foreach (ViewPoint p in frame.Points)
                {
                    Assert.That(scene.Root.transform.Find(ShowcaseScene.FlagPrefix + p.Id), Is.Not.Null, "flag " + p.Id);
                    Assert.That(scene.Root.transform.Find("Point " + p.Id), Is.Not.Null, "disc " + p.Id);
                }
                Assert.That(Mathf.Max(scene.MiniMap.width, scene.MiniMap.height), Is.EqualTo(192));
                Run(scene, 90);
                Assert.That(scene.SelfCheck(), Is.EqualTo(""));
                Assert.That(scene.Units.Count, Is.EqualTo(scene.Session.Current.Entities.Length));
                Assert.That(scene.Shake, Is.EqualTo(Vector2.zero), "nothing shakes in a picture");
            }
            Assert.That(GameObject.Find("Showcase " + id), Is.Null, "Dispose removes the stage");
        }

        [Test]
        public void TheScriptedPlayersShotsAreCountedAndDrawn()
        {
            using (ShowcaseScene scene = Scripted("battle"))
            {
                Run(scene, 119);
                Assert.That(scene.Shots.PlayerShots, Is.EqualTo(0));
                int tracers = 0;
                for (int t = 0; t < 12; t++)
                {
                    scene.Session.StepOnce();
                    scene.Stepped();
                    tracers = Mathf.Max(tracers, scene.Shots.LiveTracers(scene.Session.Tick));
                }
                Assert.That(scene.Shots.PlayerShots, Is.GreaterThan(0), "the shooter of a shot is found by where it was fired from");
                Assert.That(tracers, Is.GreaterThan(0));
                Assert.That(scene.Fx.LiveChips, Is.GreaterThan(0), "chips where the shots landed");
            }
        }

        [Test]
        public void ProjectilesAreDrawnAsStreaksAndTheirShooterIsFound()
        {
            using (ShowcaseScene scene = Scripted("bullets"))
            {
                Run(scene, 122);
                Assert.That(scene.InFlight, Is.GreaterThan(0));
                Assert.That(scene.Shots.ShownBullets, Is.GreaterThan(0));
                Assert.That(scene.Shots.PlayerShots, Is.GreaterThan(0));
                Assert.That(scene.Shots.LiveTracers(scene.Session.Tick), Is.EqualTo(0), "no instant tracers in this stage");
            }
        }

        [Test]
        public void AFallenSoldierLiesFlatAndAStandingOneIsUpright()
        {
            using (ShowcaseScene scene = Scripted("house"))
            {
                Run(scene, 100);
                FrameView frame = scene.Session.Current;
                int fallen = 0, standing = 0;
                foreach (ViewEntity e in frame.Entities)
                {
                    Transform body = scene.Root.transform.Find(ShowcaseUnits.Prefix + e.Key);
                    Assert.That(body, Is.Not.Null);
                    if (e.Alive)
                    {
                        Assert.That(body.up.y, Is.GreaterThan(0.9f), "soldier " + e.Key + " stands");
                        standing++;
                    }
                    else
                    {
                        Assert.That(Mathf.Abs(body.up.y), Is.LessThan(0.01f), "soldier " + e.Key + " lies");
                        fallen++;
                    }
                }
                Assert.That(fallen, Is.GreaterThan(0));
                Assert.That(standing, Is.GreaterThan(0));
            }
        }

        [Test]
        public void APinnedSoldierDucks()
        {
            using (ShowcaseScene scene = Scripted("battle"))
            {
                int ducking = 0;
                for (int round = 0; round < 12 && ducking == 0; round++)
                {
                    Run(scene, 330 + 30 * round);
                    foreach (ViewEntity e in scene.Session.Current.Entities)
                    {
                        if (!e.Alive) continue;
                        float height = scene.Root.transform.Find(ShowcaseUnits.Prefix + e.Key).localScale.y;
                        Assert.That(height, Is.EqualTo(e.Nerve == 1 ? ShowcaseUnits.DuckScale : 1f).Within(1e-4f), "soldier " + e.Key);
                        if (e.Nerve == 1) ducking++;
                    }
                }
                Assert.That(ducking, Is.GreaterThan(0), "somebody is pinned within twenty seconds");
            }
        }

        [Test]
        public void AHitSoldierFlashesAndTheFlashEnds()
        {
            using (ShowcaseScene scene = Scripted("melee"))
            {
                ulong key = scene.Session.ControlledKey.Value;
                Assert.That(scene.Units.SetStatus(key, false, 100f), Is.False, "the same health is not a hit");
                Assert.That(scene.Units.Flashing(key), Is.False);
                Assert.That(scene.Units.SetStatus(key, false, 60f), Is.True);
                Assert.That(scene.Units.Flashing(key), Is.True);
                Assert.That(scene.Units.SetStatus(key, false, 60f), Is.False);
                Assert.That(scene.Units.SetStatus(999999, false, 1f), Is.False, "an unknown key");
                scene.Show(1f, null, 0.5f);
                Assert.That(scene.Units.Flashing(key), Is.False, "half a second later");
            }
        }

        [Test]
        public void TheCameraSitsOnThePlayerInAPictureAndRunsAheadTowardTheMouseInPlay()
        {
            using (ShowcaseScene scene = Scripted("battle"))
            {
                ulong key = scene.Session.ControlledKey.Value;
                Assert.That(scene.Session.Current.TryGet(key, out ViewEntity me), Is.True);
                scene.Show(1f, null);
                Assert.That(new Vector2(scene.Camera.FocusX, scene.Camera.FocusZ), Is.EqualTo(new Vector2(me.X, me.Z)));
                for (int i = 0; i < 240; i++) scene.Show(1f, null, 1f / 60f, new Vector2(1f, 0f));
                var ahead = scene.Camera.MoveAxes(1f, 0f);
                float reach = scene.Camera.Zoom * ShowcaseScene.LeadShare;
                Assert.That(scene.Camera.FocusX, Is.EqualTo(me.X + ahead.X * reach).Within(0.05f));
                Assert.That(scene.Camera.FocusZ, Is.EqualTo(me.Z + ahead.Z * reach).Within(0.05f));
            }
        }

        [Test]
        public void TheGrenadePreviewFliesBurstsShakesAndChangesNothingInTheSimulation()
        {
            using (ShowcaseScene scene = Scripted("battle"))
            {
                ulong before = scene.Session.StateHash;
                Assert.That(scene.ThrowPreview(new Vector3(20f, 0f, 61f)), Is.True);
                Assert.That(scene.ThrowPreview(new Vector3(20f, 0f, 61f)), Is.False, "one at a time");
                Assert.That(scene.Fx.GrenadeInFlight, Is.True);
                int bursts = 0;
                float shake = 0f;
                for (int i = 0; i < 12; i++)
                {
                    scene.Show(1f, null, 0.125f);
                    bursts += scene.Fx.Bursts.Count;
                    shake = Mathf.Max(shake, scene.Shake.magnitude);
                }
                Assert.That(bursts, Is.EqualTo(1));
                Assert.That(scene.Fx.GrenadeInFlight, Is.False);
                Assert.That(shake, Is.GreaterThan(0f));
                Assert.That(scene.Root.transform.Find(ShowcaseFx.ScorchName), Is.Not.Null);
                Assert.That(scene.Session.StateHash, Is.EqualTo(before), "the preview is drawn only");
            }
        }

        [Test]
        public void APictureShowsTheCommittedFrameAndPlayBlendsTheLastStep()
        {
            using (ShowcaseScene scene = Scripted("melee"))
            {
                scene.Session.StepOnce();
                FrameView previous = scene.Session.Previous, current = scene.Session.Current;
                Assert.That(ShowcaseScene.Blend(previous, current, 1f), Is.SameAs(current));
                Assert.That(ShowcaseScene.Blend(previous, current, 2f), Is.SameAs(current));
                FrameView start = ShowcaseScene.Blend(previous, current, -1f);
                Assert.That(start, Is.Not.SameAs(current));
                Assert.That(start.Entities.Length, Is.EqualTo(current.Entities.Length));
                FrameView zero = FrameView.Interpolate(previous, current, 0f);
                for (int i = 0; i < start.Entities.Length; i++)
                {
                    Assert.That(start.Entities[i].X, Is.EqualTo(zero.Entities[i].X), "below 0 is held at 0");
                    Assert.That(start.Entities[i].Z, Is.EqualTo(zero.Entities[i].Z));
                }
            }
        }

        [Test]
        public void EveryCharacterOfAShotLabelHasAGlyph()
        {
            foreach (string line in ShowcaseHost.ShotLabel(600, 0xABCDEF0123456789UL, 4))
                foreach (char c in line)
                    Assert.That(TerrainPlayerHost.HasGlyph(c), Is.True, "'" + c + "' in \"" + line + "\"");
            Assert.That(ShowcaseHost.ShotLabel(150, 0x1UL, 0), Is.EqualTo(new[] { "TICK 150", "STATEHASH 0000000000000001", "PART 1" }));
        }

        [Test]
        public void SoundThatIsTooFarToHearDoesNothing()
        {
            var holder = new GameObject("SHOW-1 sound test");
            try
            {
                var sound = new ShowcaseSound(holder);
                sound.NewTick();
                sound.Shot(10f * ShowcaseSound.HearMetres, false);
                sound.Muted = true;
                sound.Step();
                sound.Hurt();
                sound.Burst(0f);
                Assert.That(sound.Muted, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
    }
}
