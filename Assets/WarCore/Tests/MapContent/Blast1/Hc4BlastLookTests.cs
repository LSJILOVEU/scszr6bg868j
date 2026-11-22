using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests.Blast1
{
    // HC-4: a drawn blast follows the blast's own radii. The frame carries the inner and outer radius from the content pack,
    // BlastLook turns them into the drawn sizes, and the grenade's drawn reach and flight time come from the throw's numbers.
    public sealed class Hc4BlastLookTests
    {
        // The demo's blast with the given radii in millimetres (a layer over rwr-field holding one blast of the demo's id).
        private static ViewBlast DemoBlast(int innerMm, int outerMm)
        {
            string kind = "{ \"id\": \"" + BlastDemo.Blast + "\", \"innerMm\": " + innerMm + ", \"outerMm\": " + outerMm
                + ", \"damage\": 100, \"friendly\": true, \"walls\": true }";
            ContentPack pack = Ml3Content.Load("hc4-blast", Ml3Content.Layer("hc4-blast", "rwr-field", "\"combat\": { \"blasts\": [ " + kind + " ] }"));
            SimSession s = SimSession.Create(BlastDemo.Recipe(pack), BlastDemo.MapBytes());
            for (int t = 0; t < BlastDemo.BlastTick; t++) s.StepOnce();
            Assert.That(s.Current.Blasts.Length, Is.EqualTo(1));
            return s.Current.Blasts[0];
        }

        // S1: the shipped frag (2 m and 6 m) and a smaller one (1.5 m and 4.5 m) reach the frame as they are in the pack.
        [Test]
        public void TheFrameCarriesBothRadiiFromThePack()
        {
            SimSession s = SimSession.Create(BlastDemo.Recipe(), BlastDemo.MapBytes());
            for (int t = 0; t < BlastDemo.BlastTick; t++) s.StepOnce();
            ViewBlast shipped = s.Current.Blasts[0];
            Assert.That((shipped.Inner, shipped.Radius), Is.EqualTo((2f, 6f)));
            ViewBlast small = DemoBlast(1500, 4500);
            Assert.That((small.Inner, small.Radius), Is.EqualTo((1.5f, 4.5f)));
            ViewBlast coreless = DemoBlast(0, 3000);
            Assert.That((coreless.Inner, coreless.Radius), Is.EqualTo((0f, 3f)));
        }

        // S1: the hamlet stage's scripted grenade bursts with the frag's radii.
        [Test]
        public void TheHamletGrenadeCarriesTheFragRadii()
        {
            ShowcaseStage stage = null;
            foreach (ShowcaseStage each in Showcase.Stages)
                if (each.Id == "hamlet") stage = each;
            Assert.That(stage, Is.Not.Null);
            SimSession s = stage.Start();
            foreach (BridgeCommand command in stage.Commands()) s.Enqueue(command);
            while (s.Current.Blasts.Length == 0 && s.Tick < 400) s.StepOnce();
            Assert.That(s.Tick, Is.EqualTo(360));
            Assert.That((s.Current.Blasts[0].Inner, s.Current.Blasts[0].Radius), Is.EqualTo((2f, 6f)));
        }

        // S2: the scorch mark spans the inner radius, the dust ring sits on it, the ring marks the outer radius; flash and smoke
        // grow with the outer radius and stay near the old fixed sizes (3.2 m and 4.6 m) for the shipped frag.
        [Test]
        public void TheDrawnSizesFollowTheRadii()
        {
            BlastLook frag = BlastLook.Of(2f, 6f), small = BlastLook.Of(1.5f, 4.5f);
            Assert.That((frag.ScorchDiameter, frag.DustRadius, frag.RingRadius, frag.HasCore), Is.EqualTo((4f, 2f, 6f, true)));
            Assert.That((small.ScorchDiameter, small.DustRadius, small.RingRadius), Is.EqualTo((3f, 1.5f, 4.5f)));
            Assert.That(small.FlashSize, Is.EqualTo(0.75f * frag.FlashSize).Within(1e-5f));
            Assert.That(small.SmokeSize, Is.EqualTo(0.75f * frag.SmokeSize).Within(1e-5f));
            Assert.That(frag.FlashSize, Is.InRange(3.2f * 0.7f, 3.2f * 1.3f));
            Assert.That(frag.SmokeSize, Is.InRange(4.6f * 0.7f, 4.6f * 1.3f));
        }

        // S3: no core is allowed (nothing at the inner radius is drawn); the outer radius must be larger than the inner one.
        [Test]
        public void TheRadiiAreChecked()
        {
            BlastLook coreless = BlastLook.Of(0f, 6f);
            Assert.That((coreless.HasCore, coreless.ScorchDiameter, coreless.DustRadius, coreless.RingRadius), Is.EqualTo((false, 0f, 0f, 6f)));
            Assert.That(() => BlastLook.Of(5.99f, 6f), Throws.Nothing);
            Assert.That(() => BlastLook.Of(6f, 6f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => BlastLook.Of(7f, 6f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => BlastLook.Of(-0.01f, 6f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => BlastLook.Of(float.NaN, 6f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => BlastLook.Of(2f, float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // S4: the ring fades from whole to gone over RingFadeSeconds; reach and flight are read from the throw's numbers.
        [Test]
        public void TheRingFadesAndTheThrowIsReadFromItsNumbers()
        {
            float fade = BlastLook.RingFadeSeconds;
            Assert.That(fade, Is.InRange(0.4f, 1f));
            Assert.That(BlastLook.RingAlpha(0f), Is.EqualTo(1f));
            Assert.That(BlastLook.RingAlpha(fade / 4f), Is.EqualTo(0.75f).Within(0.01f));
            Assert.That(BlastLook.RingAlpha(fade / 2f), Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(BlastLook.RingAlpha(fade), Is.EqualTo(0f));
            Assert.That(BlastLook.RingAlpha(fade * 2f), Is.EqualTo(0f));
            Assert.That(BlastLook.ReachMetres(28000), Is.EqualTo(28f));
            Assert.That(BlastLook.ReachMetres(30000), Is.EqualTo(30f));
            Assert.That(BlastLook.FlightSeconds(33), Is.EqualTo(1.1f).Within(1e-6f));
            Assert.That(BlastLook.FlightSeconds(45), Is.EqualTo(1.5f).Within(1e-6f));
            Assert.That(BlastLook.ReachMetres(ShowcaseActions.ReachMm), Is.EqualTo(28f), "the shipped reach is unchanged");
            Assert.That(BlastLook.FlightSeconds(ShowcaseActions.FuseTicks), Is.EqualTo(1.1f).Within(1e-6f), "the shipped flight is unchanged");
        }
    }
}
