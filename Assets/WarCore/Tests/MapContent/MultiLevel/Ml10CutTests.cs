using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-10 acceptance 4 (the layer cut) and 5 (fading what stands between the camera and the player) in the demo house:
    // the stair is layer 1 (cells x 10..13, z 9, 0 -> 3 m), the first floor layer 2 (x 9..14, z 6..9, top 3 m, bottom
    // 2.5 m, cut over the stair); 3 m ground walls round x 8..15, z 5..10. Camera at the default yaw and pitch (45, 45):
    // looking back from a unit the line rises 1 m for each metre towards -x and -z together.
    public sealed class Ml10CutTests
    {
        private const long M = SimFix.Scale;
        private static readonly (float X, float Y, float Z) Forward = new IsoCamera(12f).Forward;

        private static ViewEntity At(float x, float y, float z, int layer = 0) => new ViewEntity(1, 0, x, y, z, team: 1, layer: layer);

        private static (ViewPlates Plates, ViewMap Map) House(bool upperWall = false, bool farWall = false)
        {
            byte[] bytes = upperWall || farWall ? MapBake.Run(MapDescJson.Write(WithWalls(upperWall, farWall))).WcMapBytes : PlateDemo.MapBytes();
            return (ViewPlates.From(WcMapReader.Read(bytes)), ViewMap.Parse(bytes));
        }

        // The south ground wall (x 8..15, z 5).
        private static int SouthWall(ViewMap map) =>
            Enumerable.Range(0, map.Walls.Length).Single(w => map.Walls[w].CenterZ == 5.5f && map.Walls[w].SizeX == 8f);

        [Test]
        public void OutsideNothingIsCut()
        {
            var (plates, map) = House();
            LayerCutResult cut = LayerCut.Run(plates, map, At(4.5f, 0, 8.5f), Forward);
            Assert.That(cut.Indoors, Is.False);
            Assert.That(Enumerable.Range(0, cut.PlateCount).Select(cut.Plate), Is.All.EqualTo(CutState.Shown));
            Assert.That(Enumerable.Range(0, cut.WallCount).Select(cut.Wall), Is.All.EqualTo(CutState.Shown), "the house is behind the unit");
        }

        // Under the first floor: the floor (bottom 2.5 m > 0 + 2 m) is hidden, the stair (lowest bottom -0.5 m) and every ground
        // wall stay; the south wall stands between the camera and the unit and fades.
        [Test]
        public void UnderTheFloorTheFloorIsHidden()
        {
            var (plates, map) = House();
            LayerCutResult cut = LayerCut.Run(plates, map, At(11.5f, 0, 7.5f), Forward);
            Assert.That((cut.Indoors, cut.Plate(0), cut.Plate(1)), Is.EqualTo((true, CutState.Shown, CutState.Hidden)));
            Assert.That(Enumerable.Range(0, cut.WallCount).Select(cut.Wall), Has.None.EqualTo(CutState.Hidden));
            Assert.That(cut.Wall(SouthWall(map)), Is.EqualTo(CutState.Faded));
            Assert.That((cut.IsHidden(At(11.5f, 3, 7.5f, 2)), cut.IsHidden(At(12f, 1.5f, 9.5f, 1)), cut.IsHidden(At(20f, 0, 3f))),
                Is.EqualTo((true, false, false)), "units on the hidden floor hide; on the stair and the ground they do not");
            Assert.That((cut.Pickable(2), cut.Pickable(1), cut.Pickable(0)), Is.EqualTo((false, true, true)));
        }

        // Halfway up the stair (x 12: 2 m of 4 at 0.75 m a metre, y = 1.5 m): under the floor's rectangle (its cutout), so
        // indoors; the floor's bottom 2.5 m is not above 1.5 + 2 = 3.5 m, so nothing hides. The floor stands between the camera
        // and the unit (from the feet the line reaches 2.5 m over cell (11, 8)) and fades; the stair under the unit does not.
        [Test]
        public void HalfwayUpTheStairNothingHides()
        {
            var (plates, map) = House();
            LayerCutResult cut = LayerCut.Run(plates, map, At(12f, 1.5f, 9.5f, 1), Forward);
            Assert.That((cut.Indoors, cut.Plate(0), cut.Plate(1)), Is.EqualTo((true, CutState.Shown, CutState.Faded)));
            Assert.That(Enumerable.Range(0, cut.WallCount).Select(cut.Wall), Has.None.EqualTo(CutState.Hidden));
            Assert.That(cut.Pickable(2), Is.False, "a faded plate is not picked");
        }

        // On the floor nothing is above: outdoors, and the floor under the unit neither hides nor fades.
        [Test]
        public void OnTheFloorTheFloorStays()
        {
            var (plates, map) = House();
            LayerCutResult cut = LayerCut.Run(plates, map, At(11.5f, 3f, 7.5f, 2), Forward);
            Assert.That((cut.Indoors, cut.Plate(0), cut.Plate(1)), Is.EqualTo((false, CutState.Shown, CutState.Shown)));
        }

        // Looking towards -x and -z at 20 degrees, the line back from a unit on the stair rises slower (0.52 m a metre of x)
        // than the stair (0.75 m): it would enter the stair a few centimetres up it. The plate under the unit never fades.
        [Test]
        public void ThePlateUnderTheUnitNeverFades()
        {
            var (plates, map) = House();
            var low = new IsoCamera(12f, 0f, 225f, 20f).Forward;
            LayerCutResult cut = LayerCut.Run(plates, map, At(11f, 0.75f, 9.5f, 1), low);
            Assert.That(cut.Plate(0), Is.EqualTo(CutState.Shown));
            Assert.That(LayerCut.Run(plates, map, At(11f, 0.75f, 9.5f), low).Plate(0), Is.EqualTo(CutState.Faded),
                "the same point seen as a ground unit's: the stair is in the way");
        }

        // The rule's edges: the floor hides when its bottom (2.5 m) is above feet + 2 m, not when equal; a wall standing on the
        // floor (bottom 3 m) hides when its bottom is at or above feet + 2 m.
        [Test]
        public void TheHeadroomEdges()
        {
            var (plates, map) = House(upperWall: true);
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 0.5f, 7.5f), Forward).Plate(1), Is.Not.EqualTo(CutState.Hidden), "2.5 = 0.5 + 2 (over the unit it fades)");
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 0.49f, 7.5f), Forward).Plate(1), Is.EqualTo(CutState.Hidden));
            int upper = Enumerable.Range(0, map.Walls.Length).Single(w => map.Walls[w].CenterY - map.Walls[w].SizeY / 2 == 3f);
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 1f, 7.5f), Forward).Wall(upper), Is.EqualTo(CutState.Hidden), "3 = 1 + 2");
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 1.01f, 7.5f), Forward).Wall(upper), Is.Not.EqualTo(CutState.Hidden));
            Assert.That(LayerCut.Run(plates, map, At(4.5f, 0f, 8.5f), Forward).Wall(upper), Is.EqualTo(CutState.Shown), "outdoors");
        }

        // A wall as high as the floor's wall but away from the house (2 m tall, bottom 3 m, at cell (20, 3)) is not part of
        // the building: under the floor it stays while the wall on the floor hides.
        [Test]
        public void AHighWallAwayFromTheHouseStays()
        {
            var (plates, map) = House(upperWall: true, farWall: true);
            int upper = Enumerable.Range(0, map.Walls.Length).Single(w => map.Walls[w].CenterX == 9.5f && map.Walls[w].CenterY == 4f);
            int far = Enumerable.Range(0, map.Walls.Length).Single(w => map.Walls[w].CenterX == 20.5f);
            LayerCutResult cut = LayerCut.Run(plates, map, At(11.5f, 0, 7.5f), Forward);
            Assert.That((cut.Indoors, cut.Wall(upper), cut.Wall(far)), Is.EqualTo((true, CutState.Hidden, CutState.Shown)));
        }

        // Fading follows the line from the camera: the south wall fades when the unit is behind it and not when the unit is in
        // front of it; no player, or a dead one, cuts nothing.
        [Test]
        public void FadingFollowsTheLine()
        {
            var (plates, map) = House();
            int south = SouthWall(map);
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 0, 6.5f), Forward).Wall(south), Is.EqualTo(CutState.Faded), "behind the wall");
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 0, 4f), Forward).Wall(south), Is.EqualTo(CutState.Shown), "in front of it");
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 0, 7.5f), Forward).Wall(south), Is.EqualTo(CutState.Faded),
                "1.5 m north of the wall the line from the feet meets it at 0.1 + 2.12 m, below its 3 m top");
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 0, 8.5f), Forward).Wall(south), Is.EqualTo(CutState.Shown),
                "2.5 m north it passes over at 0.1 + 3.54 m");
            LayerCutResult none = LayerCut.Run(plates, map, null, Forward);
            LayerCutResult dead = LayerCut.Run(plates, map, new ViewEntity(1, 0, 11.5f, 0, 7.5f, team: 1, alive: false), Forward);
            foreach (LayerCutResult cut in new[] { none, dead })
                Assert.That(Enumerable.Range(0, cut.PlateCount).Select(cut.Plate).Concat(Enumerable.Range(0, cut.WallCount).Select(cut.Wall)),
                    Is.All.EqualTo(CutState.Shown));
        }

        // The demo house with a 2 m wall standing on the first floor at cell (9, 6) (bottom 3 m), one as high at cell (20, 3)
        // away from the house, or both.
        private static MapDesc WithWalls(bool upperWall, bool farWall)
        {
            MapDesc d = PlateDemoMap.Desc();
            var extra = new System.Collections.Generic.List<MapWall>();
            if (upperWall) extra.Add(new MapWall(6, "wall", "walls", new MapXZ(9, 6), new MapXZ(1, 1), 2 * M, null, null, 3 * M));
            if (farWall) extra.Add(new MapWall(7, "wall", "walls", new MapXZ(20, 3), new MapXZ(1, 1), 2 * M, null, null, 3 * M));
            var walls = d.Walls.Concat(extra).ToArray();
            return new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null,
                d.Plates.ToArray(), d.Cutouts.ToArray(), walls, d.Points.ToArray(), d.Bases.ToArray(), d.Units.ToArray(), d.Run,
                d.Owners.ToArray(), null, null, d.Stamps.ToArray(), d.Removed.ToArray(), new MapNext(8, 3, 3, 5, 3, 2));
        }
    }
}
