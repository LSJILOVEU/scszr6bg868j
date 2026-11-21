using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-10 acceptance 6: picking with plates in the demo house (stair layer 1 on x 10..13, z 9; first floor layer 2 on x
    // 9..14, z 6..9 at 3 m, cut over the stair). Each ray comes down the default camera direction onto a chosen point.
    public sealed class Ml10PickTests
    {
        private static readonly (float X, float Y, float Z) F = new IsoCamera(12f).Forward;
        private static readonly ViewPlates Plates = ViewPlates.From(WcMapReader.Read(PlateDemo.MapBytes()));

        // The ray that passes through (x, y, z), started 30 m back along the view direction.
        private static AimTarget Ray(float x, float y, float z, FrameView shown = null, ulong? self = null, LayerCutResult cut = null) =>
            AimPick.PickRay(x - F.X * 30, y - F.Y * 30, z - F.Z * 30, F.X, F.Y, F.Z, 0f, null, shown, self, Plates, cut);

        [Test]
        public void TheFloorIsPickedWithItsLayer()
        {
            AimTarget a = Ray(11.5f, 3f, 7.5f);
            Assert.That((a.Kind, a.Layer, a.XMm, a.ZMm, a.YMm), Is.EqualTo((AimKind.Plate, 2, 11500L, 7500L, (long?)3000)));
            Assert.That(a.Y, Is.EqualTo(3f).Within(1e-4f));
            // At x 13.5 the stair is 3.5 m of 4 up 3 m; the ray crossed the floor's height over the cutout.
            AimTarget stair = Ray(13.5f, 2.625f, 9.8f);
            Assert.That((stair.Kind, stair.Layer, stair.XMm, stair.ZMm, stair.YMm), Is.EqualTo((AimKind.Plate, 1, 13500L, 9800L, (long?)2625)));
        }

        // The cut hides the floor: the same ray goes on to the ground; on the cutout the ray passes the floor.
        [Test]
        public void HiddenPlatesAndHolesAreNotPicked()
        {
            LayerCutResult under = LayerCut.Run(Plates, ViewMap.Parse(PlateDemo.MapBytes()), new ViewEntity(1, 0, 11.5f, 0, 7.5f, team: 1), F);
            Assert.That(under.Plate(1), Is.EqualTo(CutState.Hidden));
            AimTarget a = Ray(11.5f, 3f, 7.5f, cut: under);
            Assert.That((a.Kind, a.Layer, a.YMm), Is.EqualTo((AimKind.Plane, 0, (long?)null)), "on to the flat ground");
            Assert.That(a.X, Is.EqualTo(11.5f + 3f * F.X / -F.Y).Within(1e-3f));
            AimTarget hole = Ray(11.5f, 3f, 9.5f);
            Assert.That((hole.Kind, hole.Layer), Is.EqualTo((AimKind.Plane, 0)), "through the cutout, over the stair, to the ground");
        }

        // A hit whose millimetre point leaves the plate's solid cells is not taken: x 14.9994 m rounds to 14999 mm (on the
        // floor), 14.9996 m to 15000 mm (the cell east of it).
        [Test]
        public void TheMillimetrePointStaysOnThePlate()
        {
            AimTarget inside = Ray(14.9994f, 3f, 7.5f);
            Assert.That((inside.Kind, inside.Layer, inside.XMm), Is.EqualTo((AimKind.Plate, 2, 14999L)));
            AimTarget edge = Ray(14.9996f, 3f, 7.5f);
            Assert.That((edge.Kind, edge.Layer), Is.EqualTo((AimKind.Plane, 0)));
        }

        // Snapping: an enemy on the floor is snapped to with its height; hidden by the cut, it is not.
        [Test]
        public void HiddenUnitsAreNotSnapped()
        {
            var me = new ViewEntity(1, 0, 11.5f, 0, 7.5f, team: 1);
            var enemy = new ViewEntity(9, 0, 12.5f, 3f, 7.5f, team: 2, layer: 2);
            var shown = new FrameView(5, new[] { me, enemy });
            AimTarget snapped = Ray(12.5f, 3.5f, 7.5f, shown, 1);
            Assert.That((snapped.Kind, snapped.UnitKey, snapped.YMm, snapped.Layer), Is.EqualTo((AimKind.Unit, 9UL, (long?)3000, 0)),
                "a unit on a plate carries its height on a map without a height field");
            LayerCutResult under = LayerCut.Run(Plates, ViewMap.Parse(PlateDemo.MapBytes()), me, F);
            AimTarget hidden = Ray(12.5f, 3.5f, 7.5f, shown, 1, under);
            Assert.That((hidden.Kind, hidden.UnitKey), Is.EqualTo((AimKind.Plane, 0UL)));
        }

        // The pick goes through the mapper into the session: the aim carries the layer and height, the session takes it, and a
        // flat pick afterwards clears the layer.
        [Test]
        public void ThePickReachesTheSession()
        {
            SimSession s = Ml10ViewTests.Demo();
            var camera = new IsoCamera(12f);
            var mapper = new PlayerInputMapper(PlayerKeys.Default);
            AimTarget floor = Ray(11.5f, 3f, 7.5f);
            PlayerInput input = mapper.Map(camera, 0, 0, 10, 10, 100, 100, true, false, null, floor);
            Assert.That((input.AimXMm, input.AimZMm, input.AimYMm, input.AimLayer), Is.EqualTo((11500L, 7500L, (long?)3000, 2)));
            Assert.DoesNotThrow(() => s.SetPlayerInput(s.Tick + 1, input));
            Assert.DoesNotThrow(() => s.StepOnce());
            PlayerInput kept = mapper.Map(camera, 0, 0, 10, 10, 100, 100, false, false, null, null);
            Assert.That(kept.AimLayer, Is.EqualTo(2), "outside the window the last pick is kept");
            PlayerInput flat = mapper.Map(camera, 0, 0, 10, 10, 100, 100, true, false, null, null);
            Assert.That((flat.AimYMm, flat.AimLayer), Is.EqualTo(((long?)null, 0)));
        }
    }
}
