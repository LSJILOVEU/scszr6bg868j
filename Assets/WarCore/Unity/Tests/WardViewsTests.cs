using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // MAP-BL0: the walled ward's stage as drawn. It builds clean (TerrainHost.SelfCheck) with one soldier per unit and one
    // flag per point, and is still clean after 150 ticks of its script.
    public sealed class WardViewsTests
    {
        [Test]
        public void TheWardBuildsCleanWithASoldierPerUnitAndAFlagPerPoint()
        {
            Assert.That(ShowcaseSets.Stages(ShowcaseHost.WardSet).Count, Is.EqualTo(1));
            ShowcaseStage stage = ShowcaseSets.Stages(ShowcaseHost.WardSet)[0];
            using (var scene = new ShowcaseScene(stage))
            {
                foreach (var item in scene.Stage.Script()) scene.Session.SetPlayerInput(item.AtTick, item.Input);
                Assert.That(scene.SelfCheck(), Is.EqualTo(""));
                FrameView frame = scene.Session.Current;
                Assert.That(frame.Points.Length, Is.EqualTo(2));
                Assert.That(scene.Units.Count, Is.EqualTo(frame.Entities.Length));
                foreach (ViewEntity e in frame.Entities)
                    Assert.That(scene.Root.transform.Find(ShowcaseUnits.Prefix + e.Key), Is.Not.Null, "soldier " + e.Key);
                foreach (ViewPoint p in frame.Points)
                    Assert.That(scene.Root.transform.Find(ShowcaseScene.FlagPrefix + p.Id), Is.Not.Null, "flag " + p.Id);
                while (scene.Session.Tick < 150 && !scene.Session.IsFinished)
                {
                    scene.Session.StepOnce();
                    scene.Stepped();
                    if (scene.Session.Tick % 30 == 0) scene.Show(1f, null);
                }
                scene.Show(1f, null);
                Assert.That(scene.Session.Tick, Is.EqualTo(150));
                Assert.That(scene.SelfCheck(), Is.EqualTo(""));
                Assert.That(scene.Units.Count, Is.EqualTo(scene.Session.Current.Entities.Length));
            }
            Assert.That(GameObject.Find("Showcase ward"), Is.Null, "Dispose removes the stage");
        }

        [Test]
        public void TheHostKeysNameTheWardSet()
        {
            Assert.That(ShowcaseHost.WardSet, Is.EqualTo("ward"));
            Assert.That(ShowcaseHost.SetKey, Is.EqualTo("WarCore.Show1.Set"));
            Assert.That(ShowcaseHost.Prefix, Is.EqualTo("SHOW1"));
        }
    }
}
