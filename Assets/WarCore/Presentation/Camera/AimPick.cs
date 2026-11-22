using System;

namespace WarCore.Presentation
{
    public enum AimKind { Plane = 0, Ground = 1, Unit = 2, Plate = 3 }

    // Where the mouse aims: metres for the picture, whole millimetres for the bridge. YMm is null when the point has no
    // height (Plane, or any pick on a map without a height field): Sim then uses the map's ground height, as before.
    public readonly struct AimTarget
    {
        public AimKind Kind { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public long XMm { get; }
        public long ZMm { get; }
        public long? YMm { get; }
        /// <summary>The snapped enemy's key for Unit; 0 otherwise.</summary>
        public ulong UnitKey { get; }
        /// <summary>ML-10: the plate's layer for Plate (1..N); 0 otherwise.</summary>
        public int Layer { get; }

        internal AimTarget(AimKind kind, float x, float y, float z, long? yMm, ulong unitKey, int layer = 0)
        {
            var pick = new GroundPick(x, z); // same millimetre rounding as the flat pick
            Kind = kind; X = x; Y = y; Z = z; XMm = pick.XMm; ZMm = pick.ZMm; YMm = yMm; UnitKey = unitKey; Layer = layer;
        }
    }

    // Mouse ray -> aim point. On a height field the ray is clipped to the field, cut where L9's own below-ground test first
    // turns true (integer bisection on the same test), and an enemy body on the ray in front of that point wins (snap).
    public static class AimPick
    {
        public const float SnapRadius = 0.5f; // PLACEHOLDER: the body's diameter (2r, r = BodySizes.Radius) as the mouse's reach; tune after play.
        public const float BodyHeight = 1f;   // The old drawn body height, for a world without body sizes (BodySizes.TopOf falls back to it).
        private const int Halvings = 24;
        private const long OneMetre = 65536;

        public static AimTarget Pick(IsoCamera camera, float screenX, float screenY, float screenWidth, float screenHeight,
            ViewGround ground, FrameView shown, ulong? self)
            => Pick(camera, screenX, screenY, screenWidth, screenHeight, ground, shown, self, null, null);

        // ML-10: as above on a map with floor plates. The ray also meets the top of every solid cell of a plate the cut leaves
        // as it is (cut null: every plate); the nearest of the ground and those tops wins, a plate top as Plate with its layer
        // and the plate's height at the millimetre point (a hit whose millimetre point falls off the plate's solid cells is
        // skipped, so the bridge never refuses the layer). Units the cut hides are not snapped to. Plates null: as above.
        // SCALE-2a: with sizes a body is as tall as its stance level now (BodySizes.Top); null keeps the old 1 m body.
        // AIM-2: with a reach (it needs sizes) the cylinder is the body widened by the reach, and of the enemies whose widened
        // cylinder the ray meets before the ground, the one whose axis (feet to Top, not widened) passes nearest the ray wins
        // (then the smaller t, then the smaller key). Reach null: exactly the pick without it.
        public static AimTarget Pick(IsoCamera camera, float screenX, float screenY, float screenWidth, float screenHeight,
            ViewGround ground, FrameView shown, ulong? self, ViewPlates plates, LayerCutResult cut, BodySizes sizes = null,
            PickReach reach = null)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var o = camera.RayOrigin(screenX, screenY, screenWidth, screenHeight);
            var f = camera.Forward;
            return PickRay(o.X, o.Y, o.Z, f.X, f.Y, f.Z, camera.GroundY, ground, shown, self, plates, cut, sizes, reach);
        }

        // The ray o + t f (f.Y < 0). Plane points use the same float formula as IsoCamera.ScreenToGround.
        internal static AimTarget PickRay(float ox, float oy, float oz, float fx, float fy, float fz, float groundY,
            ViewGround ground, FrameView shown, ulong? self, ViewPlates plates = null, LayerCutResult cut = null, BodySizes sizes = null,
            PickReach reach = null)
        {
            if (!(fy < 0f)) throw new ArgumentOutOfRangeException(nameof(fy), "The pick ray must point down.");
            if (reach != null && sizes == null) throw new ArgumentException("A reach widens the body: the pick needs body sizes (pick needs body sizes).", nameof(reach));
            AimTarget onGround = ground == null ? PlanePoint(ox, oy, oz, fx, fy, fz, groundY, out double tGround)
                : GroundPoint(ox, oy, oz, fx, fy, fz, groundY, ground, out tGround);
            if (plates != null)
                for (int k = 0; k < plates.Count; k++)
                {
                    if (cut != null && !cut.Pickable(k + 1)) continue;
                    if (TryPlate(plates, k + 1, ox, oy, oz, fx, fy, fz, tGround, out AimTarget top, out double t))
                    {
                        onGround = top;
                        tGround = t;
                    }
                }
            if (shown == null || !self.HasValue || !shown.TryGet(self.Value, out ViewEntity me)) return onGround;
            double best = double.PositiveInfinity;
            ViewEntity snapped = default;
            if (reach == null)
                foreach (ViewEntity e in shown.Entities)
                {
                    if (!Snappable(e, me, self.Value, cut)) continue;
                    double t = BodyHit(ox, oy, oz, fx, fy, fz, e, BodySizes.TopOf(sizes, e), SnapRadius);
                    if (t < best || (t == best && t < double.PositiveInfinity && e.Key < snapped.Key)) { best = t; snapped = e; }
                }
            else
            {
                double bestDistance = double.PositiveInfinity;
                foreach (ViewEntity e in shown.Entities)
                {
                    if (!Snappable(e, me, self.Value, cut)) continue; // the same filter as without a reach
                    float top = sizes.Top(e);
                    double t = BodyHit(ox, oy, oz, fx, fy, fz, e, reach.Height(top), reach.Radius(sizes.Radius));
                    if (!(t < tGround)) continue;
                    double distance = AxisDistanceSquared(ox, oy, oz, fx, fy, fz, e.X, e.Y, e.Y + top, e.Z);
                    if (distance < bestDistance || (distance == bestDistance && (t < best || (t == best && e.Key < snapped.Key))))
                    {
                        bestDistance = distance; best = t; snapped = e;
                    }
                }
            }
            if (!(best < tGround)) return onGround;
            // A unit on a plate always carries its height: without it Sim would aim at the ground under it.
            long? yMm = ground == null && snapped.Layer == 0 ? (long?)null
                : (long)Math.Round((double)snapped.Y * 1000.0, MidpointRounding.AwayFromZero);
            return new AimTarget(AimKind.Unit, snapped.X, snapped.Y, snapped.Z, yMm, snapped.Key);
        }

        // Who the pick can snap to: a living unit of another team than the player's (team 0 is nobody's), not the player,
        // and not hidden by the cut.
        private static bool Snappable(ViewEntity e, ViewEntity me, ulong self, LayerCutResult cut)
        {
            if (!e.Alive || e.Team == 0 || e.Team == me.Team || e.Key == self) return false;
            return cut == null || !cut.IsHidden(e);
        }

        // The first top of the plate of this layer in front of tLimit, at a millimetre point on a solid cell of that plate.
        private static bool TryPlate(ViewPlates plates, int layer, float ox, float oy, float oz, float fx, float fy, float fz,
            double tLimit, out AimTarget target, out double t)
        {
            target = default;
            double tMax = double.IsInfinity(tLimit) ? 1e9 : tLimit;
            if (!PlateRay.TryTop(plates.OfLayer(layer), ox, oy, oz, fx, fy, fz, 0, tMax, out t, out _, out _) || !(t < tLimit)) return false;
            float x = (float)(ox + t * fx), y = (float)(oy + t * fy), z = (float)(oz + t * fz);
            var pick = new GroundPick(x, z);
            var q = SimSession.TargetAt(pick.XMm, 0, pick.ZMm);
            if (!plates.TryTopRaw(layer, q.X.Raw, q.Z.Raw, out long h)) return false;
            long yMm = (long)Math.Round((double)h * 1000.0 / OneMetre, MidpointRounding.AwayFromZero);
            target = new AimTarget(AimKind.Plate, x, y, z, yMm, 0, layer);
            return true;
        }

        private static AimTarget PlanePoint(float ox, float oy, float oz, float fx, float fy, float fz, float groundY, out double t)
        {
            float tf = (groundY - oy) / fy;
            t = tf;
            return new AimTarget(AimKind.Plane, ox + fx * tf, groundY, oz + fz * tf, null, 0);
        }

        private static AimTarget GroundPoint(float oxf, float oyf, float ozf, float fxf, float fyf, float fzf, float groundY,
            ViewGround ground, out double tGround)
        {
            double ox = oxf, oy = oyf, oz = ozf, fx = fxf, fy = fyf, fz = fzf;
            double minX = (double)ground.MinXRaw / OneMetre, maxX = (double)ground.MaxXRaw / OneMetre;
            double minZ = (double)ground.MinZRaw / OneMetre, maxZ = (double)ground.MaxZRaw / OneMetre;
            long lowRaw = ground.MinHeightRaw - OneMetre, highRaw = ground.MaxHeightRaw + OneMetre;
            // (a) Clip: t >= 0, x and z in the closed rectangle, y in the band [lowest - 1 m, highest + 1 m].
            double ta = 0, tb = double.PositiveInfinity;
            if (!Slab(ox, fx, minX, maxX, ref ta, ref tb) || !Slab(oz, fz, minZ, maxZ, ref ta, ref tb)
                || !Slab(oy, fy, (double)lowRaw / OneMetre, (double)highRaw / OneMetre, ref ta, ref tb))
                return PlanePoint(oxf, oyf, ozf, fxf, fyf, fzf, groundY, out tGround);
            // (b) Quantize both ends and clamp them into the clipped box.
            long ax = Clamp(ToRaw(ox + ta * fx), ground.MinXRaw, ground.MaxXRaw), ay = Clamp(ToRaw(oy + ta * fy), lowRaw, highRaw),
                az = Clamp(ToRaw(oz + ta * fz), ground.MinZRaw, ground.MaxZRaw);
            long bx = Clamp(ToRaw(ox + tb * fx), ground.MinXRaw, ground.MaxXRaw), by = Clamp(ToRaw(oy + tb * fy), lowRaw, highRaw),
                bz = Clamp(ToRaw(oz + tb * fz), ground.MinZRaw, ground.MaxZRaw);
            long rx, ry, rz;
            if (ground.IsBelowRaw(ax, ay, az)) { rx = ax; ry = ay; rz = az; } // (c) entered through a side below the ground
            else if (!ground.TryFirstBelowRaw(ax, ay, az, bx, by, bz, out long cx, out long cy, out long cz))
                return PlanePoint(oxf, oyf, ozf, fxf, fyf, fzf, groundY, out tGround); // (d) crosses the field without touching it
            else
            {
                // (e) below(Q(lo)) is false (A), below(Q(hi)) is true (L9's first sample below): keep that on every halving.
                long lo = 0, hi = 1L << Halvings;
                for (int i = 0; i < Halvings; i++)
                {
                    long mid = (lo + hi) / 2;
                    if (ground.IsBelowRaw(Along(ax, cx, mid), Along(ay, cy, mid), Along(az, cz, mid))) hi = mid; else lo = mid;
                }
                rx = Along(ax, cx, hi); ry = Along(ay, cy, hi); rz = Along(az, cz, hi);
            }
            // (f) The ground point: x, z of R, the ground height under it.
            float x = (float)((double)rx / OneMetre), z = (float)((double)rz / OneMetre);
            float y = (float)((double)ground.ClosedHeightRaw(rx, rz) / OneMetre);
            tGround = ((double)rx / OneMetre - ox) * fx + ((double)ry / OneMetre - oy) * fy + ((double)rz / OneMetre - oz) * fz;
            // Millimetres: the height at the millimetre point Sim will use, clamped into the closed rectangle.
            var pick = new GroundPick(x, z);
            var q = SimSession.TargetAt(pick.XMm, 0, pick.ZMm);
            long h = ground.ClosedHeightRaw(Clamp(q.X.Raw, ground.MinXRaw, ground.MaxXRaw), Clamp(q.Z.Raw, ground.MinZRaw, ground.MaxZRaw));
            long yMm = (long)Math.Round((double)h * 1000.0 / OneMetre, MidpointRounding.AwayFromZero);
            return new AimTarget(AimKind.Ground, x, y, z, yMm, 0);
        }

        // Narrow [ta, tb] to the t where o + t d lies in [lo, hi]; false when that leaves nothing.
        private static bool Slab(double o, double d, double lo, double hi, ref double ta, ref double tb)
        {
            if (d == 0) return o >= lo && o <= hi && ta <= tb;
            double t0 = (lo - o) / d, t1 = (hi - o) / d;
            if (t0 > t1) { double s = t0; t0 = t1; t1 = s; }
            if (t0 > ta) ta = t0;
            if (t1 < tb) tb = t1;
            return ta <= tb;
        }

        // First t at which the ray meets the upright cylinder of the body (side, or the top cap); +inf when it misses. height is
        // added to the feet in float, then widened.
        private static double BodyHit(double ox, double oy, double oz, double fx, double fy, double fz, ViewEntity e, float height, float radius)
        {
            double r = radius, bottom = e.Y, top = e.Y + height;
            double px = ox - e.X, pz = oz - e.Z;
            double a = fx * fx + fz * fz, b = 2 * (px * fx + pz * fz), c = px * px + pz * pz - r * r;
            if (a == 0) // straight down: inside the circle it meets the top cap, else nothing
                return c <= 0 && oy >= top ? (top - oy) / fy : double.PositiveInfinity;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return double.PositiveInfinity;
            double root = Math.Sqrt(disc), t1 = (-b - root) / (2 * a), t2 = (-b + root) / (2 * a);
            double y1 = oy + t1 * fy;
            if (y1 >= bottom && y1 <= top) return t1 >= 0 ? t1 : double.PositiveInfinity;
            if (y1 > top)
            {
                double cap = (top - oy) / fy; // the ray goes down, so it reaches the top after t1
                if (cap <= t2 && cap >= 0) return cap;
            }
            return double.PositiveInfinity;
        }

        // AIM-2: the squared shortest distance from the half-line o + t f (t >= 0, f.Y < 0) to the upright segment at (x, z) from
        // bottom to top. Below the height where the line meets top and above where it meets bottom the vertical gap adds to the
        // horizontal one; each of the three stretches is a quadratic in t, minimised at its vertex clamped into the stretch.
        internal static double AxisDistanceSquared(double ox, double oy, double oz, double fx, double fy, double fz, double x, double bottom,
            double top, double z)
        {
            double px = ox - x, pz = oz - z;
            double a = fx * fx + fz * fz, b = 2 * (px * fx + pz * fz), c = px * px + pz * pz; // horizontal: a t^2 + b t + c
            double tTop = (top - oy) / fy, tBottom = (bottom - oy) / fy;                          // fy < 0: tTop <= tBottom
            double best = double.PositiveInfinity;
            // Above the top (t < tTop): vertical gap oy + t fy - top.
            Stretch(ref best, a, b, c, fy, oy - top, 0, tTop);
            // Beside the segment: no vertical gap.
            Stretch(ref best, a, b, c, 0, 0, tTop, tBottom);
            // Below the bottom (t > tBottom): vertical gap bottom - oy - t fy, squared the same as oy + t fy - bottom.
            Stretch(ref best, a, b, c, fy, oy - bottom, tBottom, double.PositiveInfinity);
            return best;
        }

        // The least of a t^2 + b t + c + (g0 + g t)^2 over t in [max(lo, 0), hi]; nothing when that stretch is empty.
        private static void Stretch(ref double best, double a, double b, double c, double g, double g0, double lo, double hi)
        {
            if (lo < 0) lo = 0;
            if (!(lo <= hi)) return;
            double qa = a + g * g, qb = b + 2 * g * g0, qc = c + g0 * g0;
            double t = qa > 0 ? -qb / (2 * qa) : lo;
            if (t < lo) t = lo;
            if (t > hi) t = hi;
            double v = (qa * t + qb) * t + qc;
            if (v < 0) v = 0;
            if (v < best) best = v;
        }

        private static long Along(long a, long b, long n) => a + FloorDiv((b - a) * n, 1L << Halvings);
        private static long FloorDiv(long a, long b) { long q = a / b; if (a % b != 0 && (a < 0) != (b < 0)) q--; return q; }
        private static long ToRaw(double metres) => (long)Math.Floor(metres * OneMetre);
        private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
