using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ml7bMaps;

namespace WarCore.Tests
{
    // ML-9b acceptance 2 (description), 3 (bake), 9 (gate) and invariant 3 (round trips): a point's centre on a plate.
    public sealed class Ml9bDescBakeTests
    {
        // A point whose centre stands on the plate with this id (no spawn).
        internal static MapPoint OnPlate(int id, long xMm, long zMm, ulong? plate) => new MapPoint(id, new MapMm(xMm, zMm), 3000, 0, plate: plate);

        // Point 1's centre on the first floor (id 9, layer 2) of the house; point 2 and both bases on the ground.
        internal static MapDesc Upstairs(long xMm = 11500, long zMm = 7500, bool stair = true) =>
            House(new[] { OnPlate(1, xMm, zMm, 9), Point(2, 19000, 13000, new MapMm(19000, 13000)) }, stair: stair);

        private const string Key = "\"initialOwner\": 0,\n      \"plate\": 9\n";

        [Test]
        public void ThePlateKeyRoundTrips()
        {
            string text = Text(Upstairs());
            MapDesc d = Read(text);
            Assert.That(d.Version, Is.EqualTo(2));
            Assert.That((d.Points[0].Plate, d.Points[1].Plate), Is.EqualTo(((ulong?)9, (ulong?)null)));
            Assert.That(Encoding.UTF8.GetString(MapDescJson.Write(d)), Is.EqualTo(text), "version 2 bytes round trip");
            StringAssert.Contains(Key, text);
            Assert.That(Text(House()).Contains(Key), Is.False, "no id, no key");
        }

        // The key alone makes the model version 2; on the bare tree it counts as a version 2 key, and without it version 2 is a
        // version error; under version 1 it is an unknown key (as in Ml7bDescTests.EachKeyAloneNeedsVersionTwo).
        [Test]
        public void TheKeyAloneNeedsVersionTwo()
        {
            MapPoint[] points = { OnPlate(1, 1500, 1500, 7) };
            MapDesc d = Ml4Maps.Make(0, 6, 4, null, new MapPlate[0]);
            d = new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null, new MapPlate[0],
                new MapCutout[0], new MapWall[0], points, new MapBase[0], new MapUnit[0], d.Run, d.Owners.ToArray(), null, null, new MapStamp[0],
                new MapSrc[0], new MapNext(1, 2, 1, 1, 1, 1));
            Assert.That(d.Version, Is.EqualTo(2));
            Assert.That(new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null, new MapPlate[0],
                new MapCutout[0], new MapWall[0], new[] { OnPlate(1, 1500, 1500, null) }, new MapBase[0], new MapUnit[0], d.Run, d.Owners.ToArray(),
                null, null, new MapStamp[0], new MapSrc[0], new MapNext(1, 2, 1, 1, 1, 1)).Version, Is.EqualTo(1), "the same without the id");
            string text = Text(d);
            Assert.That(Error(text), Is.EqualTo("MAPDESC_PLATE point 1 line " + Line(text, "\"plate\": 7")));
            string without = text.Replace(",\n      \"plate\": 7", "");
            Assert.That(Error(without), Is.EqualTo("MAPDESC_FORMAT line 3"));
            string one = text.Replace("\"version\": 2,", "\"version\": 1,");
            Assert.That(Error(one), Is.EqualTo("MAPDESC_KEY point 1 line " + Line(one, "\"plate\": 7")));
        }

        [Test]
        public void AnUnknownPlateIsRefused()
        {
            string text = Text(Upstairs());
            string other = text.Replace(Key, "\"initialOwner\": 0,\n      \"plate\": 8\n");
            Assert.That(Error(other), Is.EqualTo("MAPDESC_PLATE point 1 line " + Line(other, "\"plate\": 8")));
            string zero = text.Replace(Key, "\"initialOwner\": 0,\n      \"plate\": 0\n");
            Assert.That(Error(zero), Is.EqualTo("MAPDESC_PLATE point 1 line " + Line(zero, "\"plate\": 0")), "ids start at 1");
        }

        [Test]
        public void TheEditorKeepsThePlate()
        {
            var edit = new MapEdit(House());
            int point = edit.AddPoint(OnPlate(1, 11500, 6500, 9));
            Assert.That(edit.Current.Points.Single(p => p.Id == point).Plate, Is.EqualTo((ulong?)9));
        }

        // Plates listed 9 then 5: id 9 is layer 2 (ascending id), and the centre stands on its top.
        [Test]
        public void TheCentreBakesOntoThePlate()
        {
            MapSetup setup = Setup(Upstairs());
            Assert.That((setup.ControlPoints[0].Layer, setup.ControlPoints[0].Center.Y.Raw, setup.ControlPoints[0].Center.X.Raw),
                Is.EqualTo((2, 3 * M, 11500 * M / 1000)));
            Assert.That((setup.ControlPoints[1].Layer, setup.ControlPoints[1].Center.Y.Raw), Is.EqualTo((0, 0L)));
            Assert.That(setup.ControlPoints[0].SpawnLayer, Is.EqualTo(0), "no spawn");
            // On the stair (id 5, layer 1): 0 -> 3 m over x 10..14, so x = 12.25 m stands at 2.25 * 0.75 m.
            MapSetup slope = Setup(House(new[] { OnPlate(1, 12250, 9500, 5), Point(2, 19000, 13000, new MapMm(19000, 13000)) }));
            Assert.That((slope.ControlPoints[0].Layer, slope.ControlPoints[0].Center.Y.Raw), Is.EqualTo((1, 9 * M / 4 * 3 / 4)));
        }

        [Test]
        public void ACentreOffASolidCellIsRefused()
        {
            Assert.That(BakeError(Upstairs(11500, 9500)), Is.EqualTo("MAPDESC_PLATE_ANCHOR point 1"), "the first floor's cutout over the stair");
            Assert.That(BakeError(Upstairs(16500, 7500)), Is.EqualTo("MAPDESC_PLATE_ANCHOR point 1"), "east of the plate");
            Assert.That(Setup(Upstairs(14500, 6500)).ControlPoints[0].Layer, Is.EqualTo(2), "a solid cell under a wall bakes: walkability is the gate's");
        }

        // 9: a ground base reaches the point upstairs by the stair; without the stair it does not, though the ground grid
        // reaches the cell under the centre.
        [Test]
        public void TheGateWalksUpToTheCentre()
        {
            Assert.That(Gate(Upstairs()), Is.Empty);
            Assert.That(Gate(Upstairs(stair: false)), Is.EqualTo(new[]
            {
                new MapProblem("GATE_UNREACHABLE", "base 1 point 1", (2, 8), new MapMm(2000, 8000)),
                new MapProblem("GATE_UNREACHABLE", "base 2 point 1", (22, 8), new MapMm(22000, 8000)),
            }));
            MapDesc ground = House(new[] { Point(1, 11500, 7500), Point(2, 19000, 13000, new MapMm(19000, 13000)) }, stair: false);
            Assert.That(Gate(ground), Is.Empty, "the same centre on the ground is reached on the ground");
        }

        [Test]
        public void TheGateNeedsAWalkableCentre()
        {
            Assert.That(Gate(Upstairs(14500, 6500)), Is.EqualTo(new[] { new MapProblem("GATE_POINT_BLOCKED", "point 1 layer 2", (14, 6), new MapMm(14500, 6500)) }));
        }
    }
}
