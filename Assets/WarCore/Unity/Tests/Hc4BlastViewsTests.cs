using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // HC-4: a real blast is drawn from its own radii: a ring on the ground at the outer radius that fades out, the scorch
    // mark across the inner radius, and one burst for the camera. The radii here are made up, so a fixed size cannot pass.
    public sealed class Hc4BlastViewsTests
    {
        private static List<Transform> LiveRings(ShowcaseScene scene)
        {
            var rings = new List<Transform>();
            foreach (Transform child in scene.Root.transform)
                if (child.name == ShowcaseFx.DangerRingName && child.gameObject.activeSelf) rings.Add(child);
            return rings;
        }

        private static void CheckBlast(float inner, float outer)
        {
            using (var scene = new ShowcaseScene(Showcase.Stages[0]))
            {
                scene.DrawBlast(new ViewBlast(105f, 2f, 89f, inner, outer, 1));
                List<Transform> rings = LiveRings(scene);
                Assert.That(rings.Count, Is.EqualTo(1));
                Assert.That(ShowcaseFx.RingRadiusOf(rings[0]), Is.EqualTo(outer).Within(0.01f));
                Assert.That(new Vector2(rings[0].position.x, rings[0].position.z), Is.EqualTo(new Vector2(105f, 89f)));
                Assert.That(scene.Fx.Bursts.Count, Is.EqualTo(1));
                Transform scorch = scene.Root.transform.Find(ShowcaseFx.ScorchName);
                if (inner > 0f)
                {
                    Assert.That(scorch, Is.Not.Null);
                    Assert.That(scorch.localScale.x, Is.EqualTo(2f * inner).Within(0.01f));
                    Assert.That(new Vector2(scorch.position.x, scorch.position.z), Is.EqualTo(new Vector2(105f, 89f)));
                }
                else Assert.That(scorch, Is.Null, "no full-harm core, no scorch mark");
            }
        }

        // S5: the shipped frag (2 m and 6 m), a smaller one (1.5 m and 4.5 m) and one without a core.
        [Test]
        public void TheRingAndScorchFollowTheRadii()
        {
            CheckBlast(2f, 6f);
            CheckBlast(1.5f, 4.5f);
            CheckBlast(0f, 3f);
        }

        // S6: halfway through the fade the ring is still there and thinner; after it the ring is gone.
        [Test]
        public void TheRingFadesOut()
        {
            using (var scene = new ShowcaseScene(Showcase.Stages[0]))
            {
                scene.DrawBlast(new ViewBlast(105f, 2f, 89f, 2f, 6f, 1));
                Transform ring = LiveRings(scene)[0];
                scene.Fx.Update(BlastLook.RingFadeSeconds / 2f);
                Assert.That(ring.gameObject.activeSelf, Is.True);
                Assert.That(ring.GetComponent<LineRenderer>().widthMultiplier, Is.EqualTo(BlastLook.RingWidth / 2f).Within(0.01f));
                scene.Fx.Update(BlastLook.RingFadeSeconds / 2f + 0.01f);
                Assert.That(ring.gameObject.activeSelf, Is.False);
                Assert.That(LiveRings(scene), Is.Empty);
            }
        }
    }
}
