using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // AI-P5a, what the view is told: each unit's squad, as the spawn key of its leader.
    public sealed partial class AiP5aTests
    {
        private static ViewEntity Seen(M6World w, ulong key)
        {
            Assert.That(w.Session.Current.TryGet(key, out ViewEntity seen), Is.True, "key " + key);
            return seen;
        }

        // The band of a member is its leader's spawn key, of a leader its own, of a skirmisher and of a dead man 0; a battle
        // without squads shows 0 for everybody. An interpolated frame keeps the band of the newer frame.
        [Test]
        public void TheViewShowsEachMansSquad()
        {
            M6World w = Ten(out ulong[] k, null, x => Kill(x, 20, ByElect(Keys(1, 10))[6]));
            while (w.Session.Tick < Form - 1) w.Session.StepOnce();
            foreach (ulong key in k) Assert.That(Seen(w, key).Band, Is.Zero, "before the first form tick");
            FrameView before = w.Session.Current;
            w.Session.StepOnce();
            foreach (ulong key in new[] { k[0], k[5], k[6], k[7], k[8] }) Assert.That(Seen(w, key).Band, Is.EqualTo(k[0]), "key " + key);
            foreach (ulong key in new[] { k[1], k[2] }) Assert.That(Seen(w, key).Band, Is.EqualTo(k[1]), "key " + key);
            foreach (ulong key in new[] { k[3], k[4], k[9], 90UL }) Assert.That(Seen(w, key).Band, Is.Zero, "key " + key);
            FrameView between = FrameView.Interpolate(before, w.Session.Current, 0.5f);
            Assert.That(between.TryGet(k[5], out ViewEntity half), Is.True);
            Assert.That(half.Band, Is.EqualTo(k[0]));
            while (w.Session.Tick < 20) w.Session.StepOnce();
            Assert.That(Seen(w, k[6]).Alive, Is.False);
            Assert.That(Seen(w, k[6]).Band, Is.Zero, "a dead man is in no squad");
            Assert.That(Seen(w, k[5]).Band, Is.EqualTo(k[0]));

            M6World plain = Field(null, new[] { Man(1, 1, 20), Man(2, 1, 21), Man(90, 2, 150, 50) });
            while (plain.Session.Tick < Form) plain.Session.StepOnce();
            foreach (ulong key in new ulong[] { 1, 2, 90 }) Assert.That(Seen(plain, key).Band, Is.Zero);
            Assert.That(new ViewEntity(7, 0, 1f, 2f, 3f).Band, Is.Zero);
            Assert.That(new ViewEntity(7, 0, 1f, 2f, 3f, band: 9).Band, Is.EqualTo(9UL));
        }
    }
}
