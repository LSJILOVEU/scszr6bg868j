using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // SEE-1 street demo (StreetDemo): 64 x 40 m ground, a 6 m building on x 8..22 and a 9 m one on x 26..42 (both z 8..14), a
    // 1.2 m sandbag line on x 30..36, z 26..27; the player walks east along z 19 at 0.1 m a tick from x 4.5 (tick 1 on, standing
    // from tick 480 at 52.4). Team mates (squad 2): key 2 at (15.5, 16) behind the 6 m building, key 3 at (34.5, 17) behind the 9 m
    // one, key 4 at (46, 22) in the open; enemies: keys 10, 11, 12 at (16.5, 16.5), (36, 16) and (56, 19). Walls 0, 1, 2 in the
    // file's order are the 6 m building, the 9 m building and the sandbags.
    public sealed class See1StreetTests
    {
        private static SimSession Run(long ticks)
        {
            SimSession s = SimSession.Create(StreetDemo.Recipe(), StreetDemo.MapBytes());
            foreach (var item in StreetDemo.Script()) s.SetPlayerInput(item.AtTick, item.Input);
            for (long tick = 0; tick < ticks; tick++) s.StepOnce();
            return s;
        }

        private static (CutState[] Walls, ulong[] Silhouettes) Look(SimSession s)
        {
            s.Current.TryGet(StreetDemo.PlayerKey, out ViewEntity me);
            LayerCutResult cut = LayerCut.Run(s.Map.Plates, s.Map, me, Forward, LayerCut.SeeRadius);
            var set = Silhouettes.Run(s.Map.Plates, s.Map, cut, me, s.Current.Entities, Forward);
            return (Enumerable.Range(0, cut.WallCount).Select(cut.Wall).ToArray(), set.OrderBy(k => k).ToArray());
        }

        [Test]
        public void TheStreetIsAMapOfWallsOnly()
        {
            SimSession s = Run(0);
            Assert.That((s.Map.Walls.Length, s.Map.Plates == null, s.Map.Width, s.Map.Depth), Is.EqualTo((3, true, 64, 40)));
            Assert.That(s.Map.Walls.ToArray().Select(w => w.CenterY + w.SizeY / 2), Is.EqualTo(new[] { 6f, 9f, 1.2f }).Within(0.001f));
        }

        // Tick 0, the player at (4.5, 19): the ring (2 m) reaches x 6.5, short of every shadow: nothing fades; the team mates behind
        // the two buildings are silhouettes, the one in the open and the enemies are not.
        [Test]
        public void AtTheStartBothBuildingsCoverTheirTeamMates()
        {
            var (walls, silhouettes) = Look(Run(0));
            Assert.That(walls, Is.EqualTo(new[] { CutState.Shown, CutState.Shown, CutState.Shown }));
            Assert.That(silhouettes, Is.EqualTo(new ulong[] { 2, 3 }));
        }

        // Tick 150, at (19.5, 19): the 6 m building fades (its sample (19.5, 16.5) is in the shadow, z <= 18.17); the 9 m one stands
        // east of every sample's shadow (it starts at x 26); mate 2 behind the faded building is seen through it, mate 3 stays covered.
        [Test]
        public void BesideTheFirstBuildingItFades()
        {
            SimSession s = Run(150);
            s.Current.TryGet(StreetDemo.PlayerKey, out ViewEntity me);
            Assert.That(me.X, Is.EqualTo(19.5f).Within(0.01f));
            var (walls, silhouettes) = Look(s);
            Assert.That(walls, Is.EqualTo(new[] { CutState.Faded, CutState.Shown, CutState.Shown }));
            Assert.That(silhouettes, Is.EqualTo(new ulong[] { 3 }));
        }

        // Tick 300, at (34.5, 19): the 9 m building fades (its shadow reaches z 20.29), the 6 m one is 12 m behind and shows again; mate 2
        // is covered by it, mate 3 is seen through the faded one. The sandbags never fade.
        [Test]
        public void BesideTheSecondBuildingItFadesAndTheFirstCoversAgain()
        {
            SimSession s = Run(300);
            s.Current.TryGet(StreetDemo.PlayerKey, out ViewEntity me);
            Assert.That(me.X, Is.EqualTo(34.5f).Within(0.01f));
            var (walls, silhouettes) = Look(s);
            Assert.That(walls, Is.EqualTo(new[] { CutState.Shown, CutState.Faded, CutState.Shown }));
            Assert.That(silhouettes, Is.EqualTo(new ulong[] { 2 }));
        }
    }
}
