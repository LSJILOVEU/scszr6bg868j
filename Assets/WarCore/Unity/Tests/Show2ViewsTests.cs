using System;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // SHOW-2: the hamlet stage as drawn: soldiers drawn as tall as their stance, the simulation's blast drawn where it goes
    // off, and the drawn grenade of a real throw landing without a drawn burst of its own.
    public sealed class Show2ViewsTests
    {
        private static ShowcaseScene Hamlet()
        {
            ShowcaseStage stage = null;
            foreach (ShowcaseStage s in Showcase.Stages)
                if (s.Id == "hamlet") stage = s;
            var scene = new ShowcaseScene(stage);
            foreach (var item in stage.Script()) scene.Session.SetPlayerInput(item.AtTick, item.Input);
            foreach (BridgeCommand command in stage.Commands()) scene.Session.Enqueue(command);
            return scene;
        }

        private static void Run(ShowcaseScene scene, long toTick)
        {
            while (scene.Session.Tick < toTick)
            {
                scene.Session.StepOnce();
                scene.Stepped();
            }
            scene.Show(1f, null);
        }

        [Test]
        public void ASoldierIsDrawnAsTallAsItsStance()
        {
            using (ShowcaseScene scene = Hamlet())
            {
                float[] scales = scene.Stage.StanceScales();
                BodySizes sizes = scene.Sizes;
                Run(scene, 200);
                int crouching = 0;
                foreach (ViewEntity e in scene.Session.Current.Entities)
                {
                    if (!e.Alive) continue;
                    Transform soldier = scene.Root.transform.Find(ShowcaseUnits.Prefix + e.Key);
                    if (sizes == null)
                    {
                        float want = Mathf.Min(scales[e.Stance], e.Nerve == 1 ? ShowcaseUnits.DuckScale : 1f);
                        Assert.That(soldier.localScale.y, Is.EqualTo(want).Within(1e-4f), "soldier " + e.Key);
                    }
                    else
                    {
                        // SCALE-2a: with body sizes the parts are laid out at the stance's own top; the soldier is not squashed.
                        Assert.That(soldier.localScale.y, Is.EqualTo(1f).Within(1e-4f), "soldier " + e.Key);
                        Transform head = soldier.Find("Head");
                        float headTop = head.localPosition.y + head.localScale.y / 2f;
                        bool lying = DrawnParts.For(sizes.StandingHeight, sizes.Radius, sizes.Top(e), sizes.Muzzle(e)).Lying;
                        if (lying) Assert.That(headTop, Is.LessThanOrEqualTo(sizes.Top(e) + 1e-4f), "soldier " + e.Key);
                        else Assert.That(headTop, Is.EqualTo(sizes.Top(e)).Within(1e-4f), "soldier " + e.Key);
                    }
                    if (e.Stance == 1) crouching++;
                }
                Assert.That(crouching, Is.GreaterThan(30), "team 1 was ordered to crouch at tick 180");
            }
        }

        [Test]
        public void TheSimulationsBlastIsDrawnWhereItGoesOff()
        {
            using (ShowcaseScene scene = Hamlet())
            {
                Run(scene, 359);
                Assert.That(scene.Root.transform.Find(ShowcaseFx.ScorchName), Is.Null);
                scene.Session.StepOnce();
                scene.Stepped();
                Assert.That(scene.Session.Current.Blasts.Length, Is.EqualTo(1));
                Assert.That(scene.Fx.Bursts.Count, Is.EqualTo(1));
                Vector3 at = scene.Fx.Bursts[0];
                Assert.That(new Vector2(at.x, at.z), Is.EqualTo(new Vector2(105f, 89f)));
                Transform scorch = scene.Root.transform.Find(ShowcaseFx.ScorchName);
                Assert.That(scorch, Is.Not.Null);
                Assert.That(new Vector2(scorch.position.x, scorch.position.z), Is.EqualTo(new Vector2(105f, 89f)));
            }
        }

        [Test]
        public void TheDrawnGrenadeOfARealThrowLandsWithoutABurstOfItsOwn()
        {
            using (ShowcaseScene scene = Hamlet())
            {
                Run(scene, 30);
                ulong before = scene.Session.StateHash;
                Assert.That(scene.ThrowReal(new Vector3(100f, 0f, 30f)), Is.True);
                Assert.That(scene.ThrowReal(new Vector3(100f, 0f, 30f)), Is.False, "one at a time");
                int bursts = 0;
                for (int i = 0; i < 12; i++)
                {
                    scene.Show(1f, null, 0.125f);
                    bursts += scene.Fx.Bursts.Count;
                }
                Assert.That(scene.Fx.GrenadeInFlight, Is.False, "it has landed");
                Assert.That(bursts, Is.EqualTo(0));
                Assert.That(scene.Root.transform.Find(ShowcaseFx.ScorchName), Is.Null);
                Assert.That(scene.Session.StateHash, Is.EqualTo(before), "drawing changes nothing in the simulation");
            }
        }
    }
}
