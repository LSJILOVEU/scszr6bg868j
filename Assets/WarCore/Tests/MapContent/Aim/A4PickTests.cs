using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // A4 acceptance 5 (picking on the height field, appendix K1, L5, L6) and 6 (snapping to an enemy body).
    public sealed class A4PickTests
    {
        private const double Tol = 0.01; // 1 cm
        private static readonly double Cos20 = Math.Cos(20 * Math.PI / 180), Sin20 = Math.Sin(20 * Math.PI / 180);
        private const double Z = 4.5;

        private static AimTarget Ray(double px, double py, double fx, double fy, ViewGround ground, FrameView shown = null, ulong? self = null)
        {
            var o = A4.Through(px, py, Z, fx, fy, 0);
            return AimPick.PickRay(o.ox, o.oy, o.oz, (float)fx, (float)fy, 0f, 0f, ground, shown, self);
        }

        private static AimTarget East(double px, double py, ViewGround ground, FrameView shown = null, ulong? self = null) =>
            Ray(px, py, Cos20, -Sin20, ground, shown, self);

        // Oracle hit x along the same float ray.
        private static double OracleX(double px, double py, double fx, double fy)
        {
            var o = A4.Through(px, py, Z, fx, fy, 0);
            double t = A4.OracleT(o.ox, o.oy, o.oz, (float)fx, (float)fy, 0, A4.Ridge);
            return o.ox + t * (float)fx;
        }

        private static void Ground(AimTarget a, double x, long? yMm, string name)
        {
            Assert.That(a.Kind, Is.EqualTo(AimKind.Ground), name);
            Assert.That(a.X, Is.EqualTo(x).Within(Tol), name + " X");
            Assert.That(a.Z, Is.EqualTo(Z).Within(Tol), name + " Z");
            if (yMm.HasValue) Assert.That(a.YMm, Is.EqualTo(yMm), name + " YMm");
        }

        [Test]
        public void A4Pick_FixedRays_TableL6()
        {
            ViewGround g = A4.RidgeGround();
            Assert.That(g, Is.Not.Null);
            Ground(East(9, 1.5, g), 9.0, 1500, "V1");
            AimTarget o1 = East(15, 1.5, g);
            Ground(o1, 11.5277, null, "O1");
            Assert.That(o1.YMm.Value, Is.EqualTo((o1.XMm - 6000) / 2.0).Within(1), "O1 height on the front slope");
            Ground(East(12, 2.9999, g), 11.99988, 3000, "G1 grazing the top, 0.85 mm under it");
            Ground(East(12, 3.0001, g), 20.2427, 0, "M1 just over the top");
            AimTarget e1 = East(23.9996, 0, g);
            Ground(e1, 23.9996, 0, "E1"); Assert.That(e1.XMm, Is.EqualTo(24000));
            AimTarget e1r = Ray(23.9996, 0, -Cos20, -Sin20, g);
            Ground(e1r, 23.9996, 0, "E1' from the far side"); Assert.That(e1r.XMm, Is.EqualTo(24000));
            AimTarget s1 = Ray(0, -0.5, 0.8, -0.6, g);
            Ground(s1, 0, 0, "S1 enters through the side below the ground");
            var o = A4.Through(0, -2, Z, Cos20, -Sin20, 0);
            AimTarget p1 = East(0, -2, g);
            Assert.That(p1.Kind, Is.EqualTo(AimKind.Plane), "P1");
            Assert.That(p1.YMm, Is.Null);
            float t = (0f - o.oy) / (float)-Sin20;
            Assert.That(p1.X, Is.EqualTo(o.ox + (float)Cos20 * t));
            // The table's numbers agree with the independent oracle on the same rays.
            Assert.That(OracleX(15, 1.5, Cos20, -Sin20), Is.EqualTo(11.5277).Within(Tol));
            Assert.That(OracleX(12, 2.9999, Cos20, -Sin20), Is.EqualTo(11.99988).Within(Tol));
            Assert.That(OracleX(12, 3.0001, Cos20, -Sin20), Is.EqualTo(20.2427).Within(Tol));
        }

        [Test]
        public void A4Pick_ScreenSweep_MatchesTheOracle()
        {
            ViewGround g = A4.RidgeGround();
            int ground = 0, plane = 0;
            foreach (float yaw in new[] { 0f, 45f, 135f, 300f })
                foreach (float pitch in new[] { 20f, 45f, 80f })
                {
                    var camera = new IsoCamera(8, 0, yaw, pitch);
                    camera.Follow(12, 4);
                    foreach (float sx in new[] { 200f, 400f, 600f })
                        foreach (float sy in new[] { 150f, 300f, 450f })
                        {
                            AimTarget a = AimPick.Pick(camera, sx, sy, 800, 600, g, null, null);
                            var o = camera.RayOrigin(sx, sy, 800, 600); var f = camera.Forward;
                            double t = A4.OracleT(o.X, o.Y, o.Z, f.X, f.Y, f.Z, A4.Ridge);
                            string at = $"yaw {yaw} pitch {pitch} ({sx}, {sy})";
                            if (double.IsNaN(t))
                            {
                                Assert.That(a.Kind, Is.EqualTo(AimKind.Plane), at);
                                GroundPick flat = camera.ScreenToGround(sx, sy, 800, 600);
                                Assert.That(a.X, Is.EqualTo(flat.X), at); Assert.That(a.Z, Is.EqualTo(flat.Z), at);
                                Assert.That(a.YMm, Is.Null, at);
                                plane++;
                                continue;
                            }
                            Assert.That(a.Kind, Is.EqualTo(AimKind.Ground), at);
                            Assert.That(a.X, Is.EqualTo(o.X + t * f.X).Within(Tol), at);
                            Assert.That(a.Z, Is.EqualTo(o.Z + t * f.Z).Within(Tol), at);
                            ground++;
                        }
                }
            TestContext.Progress.WriteLine($"A4Pick sweep ground={ground} plane={plane}");
            Assert.That(ground, Is.GreaterThanOrEqualTo(12 * 5));
        }

        [Test]
        public void A4Pick_GridPointsAtPitch80_RoundTripOrHitTheFrontSlope()
        {
            ViewGround g = A4.RidgeGround();
            int visible = 0, hidden = 0, outside = 0;
            foreach (float yaw in new[] { 0f, 45f, 135f, 300f })
            {
                var camera = new IsoCamera(8, 0, yaw, 80);
                camera.Follow(12, 4);
                for (int j = 0; j <= 8; j++)
                    for (int i = 0; i <= 24; i++)
                    {
                        double h = A4.Ridge(i, j) / (double)A4.S;
                        var (sx, sy) = A4.Project(camera, i, h, j, 800, 600);
                        AimTarget a = AimPick.Pick(camera, sx, sy, 800, 600, g, null, null);
                        var o = camera.RayOrigin(sx, sy, 800, 600); var f = camera.Forward;
                        double t = A4.OracleT(o.X, o.Y, o.Z, f.X, f.Y, f.Z, A4.Ridge);
                        string at = $"grid ({i}, {j}) yaw {yaw}";
                        if (double.IsNaN(t))
                        {
                            // Float projection error moves an edge point's ray just past the field: no surface, so the plane pick.
                            Assert.That(i == 0 || i == 24 || j == 0 || j == 8, Is.True, at);
                            Assert.That(a.Kind, Is.EqualTo(AimKind.Plane), at);
                            outside++;
                            continue;
                        }
                        double ox = o.X + t * f.X, oz = o.Z + t * f.Z;
                        bool onEdge = ox <= Tol || ox >= 24 - Tol || oz <= Tol || oz >= 8 - Tol;
                        if (onEdge && a.Kind == AimKind.Plane)
                        {
                            // The surface hit is within Tol of the field's edge; the quantized ray may graze past it.
                            outside++;
                            continue;
                        }
                        Assert.That(a.Kind, Is.EqualTo(AimKind.Ground), at);
                        if (Math.Abs(ox - i) <= Tol && Math.Abs(oz - j) <= Tol)
                        {
                            Assert.That(a.X, Is.EqualTo((double)i).Within(Tol), at); Assert.That(a.Z, Is.EqualTo((double)j).Within(Tol), at);
                            visible++;
                        }
                        else
                        {
                            Assert.That(a.X, Is.EqualTo(ox).Within(Tol), at); Assert.That(a.Z, Is.EqualTo(oz).Within(Tol), at);
                            TestContext.Progress.WriteLine($"A4Pick hidden {at} -> ({ox:F4}, {oz:F4})");
                            hidden++;
                        }
                    }
            }
            TestContext.Progress.WriteLine($"A4Pick pitch80 visible={visible} hidden={hidden} outside={outside}");
            Assert.That(visible + hidden + outside, Is.EqualTo(4 * 25 * 9));
            Assert.That(outside, Is.LessThanOrEqualTo(4 * 2 * (25 + 9)));
            Assert.That(visible, Is.GreaterThan(4 * 25 * 9 / 2));
        }

        [Test]
        public void A4Pick_FlatMap_IsAlwaysThePlanePick()
        {
            ViewGround flat = ViewGround.For(ViewMap.Parse(DemoRecipes.M2bMapBytes()));
            Assert.That(flat, Is.Null);
            var camera = new IsoCamera(10, 0, 45, 45); camera.Follow(20, 10);
            foreach (float sx in new[] { 0f, 333f, 800f })
                foreach (float sy in new[] { 0f, 271f, 600f })
                {
                    AimTarget a = AimPick.Pick(camera, sx, sy, 800, 600, flat, null, null);
                    GroundPick p = camera.ScreenToGround(sx, sy, 800, 600);
                    Assert.That(a.Kind, Is.EqualTo(AimKind.Plane));
                    Assert.That(a.X, Is.EqualTo(p.X)); Assert.That(a.Z, Is.EqualTo(p.Z));
                    Assert.That(a.XMm, Is.EqualTo(p.XMm)); Assert.That(a.ZMm, Is.EqualTo(p.ZMm));
                    Assert.That(a.YMm, Is.Null);
                }
        }

        // ---- Snapping (acceptance 6) ----

        private static FrameView Frame(params ViewEntity[] e) => new FrameView(1, e);
        private static ViewEntity Me() => new ViewEntity(1, 0, 2f, 0f, 4.5f, team: 1);
        private static ViewEntity Enemy(ulong key, float x, float y, int team = 2, bool alive = true) => new ViewEntity(key, 0, x, y, 4.5f, team: team, alive: alive);
        // A ray through the body centre (x, y + 0.5).
        private static AimTarget AtBody(float x, float y, ViewGround g, FrameView shown, ulong? self = 1, double dz = 0)
        {
            var o = A4.Through(x, y + 0.5, Z + dz, Cos20, -Sin20, 0);
            return AimPick.PickRay(o.ox, o.oy, o.oz, (float)Cos20, (float)-Sin20, 0f, 0f, g, shown, self);
        }

        [Test]
        public void A4Pick_Snap_ToTheEnemyCentre()
        {
            ViewGround g = A4.RidgeGround();
            AimTarget a = AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9, 1.5f)));
            Assert.That(a.Kind, Is.EqualTo(AimKind.Unit));
            Assert.That(a.UnitKey, Is.EqualTo(2UL));
            Assert.That(a.XMm, Is.EqualTo(9000)); Assert.That(a.ZMm, Is.EqualTo(4500)); Assert.That(a.YMm, Is.EqualTo(1500));
        }

        [Test]
        public void A4Pick_Snap_OnlyLivingEnemiesInFrontOfTheGround()
        {
            ViewGround g = A4.RidgeGround();
            Assert.That(AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9, 1.5f)), dz: 0.6).Kind, Is.EqualTo(AimKind.Ground), "0.6 m to the side");
            Assert.That(AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9, 1.5f, team: 1))).Kind, Is.EqualTo(AimKind.Ground), "teammate");
            Assert.That(AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9, 1.5f, alive: false))).Kind, Is.EqualTo(AimKind.Ground), "dead");
            Assert.That(AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9, 1.5f, team: 0))).Kind, Is.EqualTo(AimKind.Ground), "team 0");
            Assert.That(AtBody(9, 1.5f, g, Frame(new ViewEntity(2, 0, 9f, 1.5f, 4.5f, team: 1))).Kind, Is.EqualTo(AimKind.Ground), "self is not a target");
            Assert.That(AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9, 1.5f)), self: 99).Kind, Is.EqualTo(AimKind.Ground), "self not in the frame");
            Assert.That(AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9, 1.5f)), self: null).Kind, Is.EqualTo(AimKind.Ground), "no self");
            // Behind the ridge: the ray through the body centre meets the front slope first.
            Assert.That(AtBody(16, 1f, g, Frame(Me(), Enemy(2, 16, 1f))).Kind, Is.EqualTo(AimKind.Ground), "behind the ridge");
        }

        [Test]
        public void A4Pick_Snap_TwoBodiesOnTheRay_TakesTheNearer()
        {
            ViewGround g = A4.RidgeGround();
            AimTarget a = AtBody(9, 1.5f, g, Frame(Me(), Enemy(2, 9.6f, 1.8f), Enemy(5, 9, 1.5f)));
            Assert.That(a.Kind, Is.EqualTo(AimKind.Unit));
            Assert.That(a.UnitKey, Is.EqualTo(5UL));
        }

        [Test]
        public void A4Pick_Snap_OnAFlatMap_HasNoHeight()
        {
            AimTarget a = AtBody(9, 0f, null, Frame(Me(), Enemy(2, 9, 0f)));
            Assert.That(a.Kind, Is.EqualTo(AimKind.Unit));
            Assert.That(a.UnitKey, Is.EqualTo(2UL));
            Assert.That(a.XMm, Is.EqualTo(9000));
            Assert.That(a.YMm, Is.Null);
        }
    }
}
