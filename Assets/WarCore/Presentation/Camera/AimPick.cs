using System;

namespace WarCore.Presentation
{
    public enum AimKind { Plane = 0, Ground = 1, Unit = 2 }

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

        internal AimTarget(AimKind kind, float x, float y, float z, long? yMm, ulong unitKey)
        {
            var pick = new GroundPick(x, z); // same millimetre rounding as the flat pick
            Kind = kind; X = x; Y = y; Z = z; XMm = pick.XMm; ZMm = pick.ZMm; YMm = yMm; UnitKey = unitKey;
        }
    }

    // Mouse ray -> aim point. On a height field the ray is clipped to the field, cut where L9's own below-ground test first
    // turns true (integer bisection on the same test), and an enemy body on the ray in front of that point wins (snap).
    public static class AimPick
    {
        public const float SnapRadius = 0.5f; // PLACEHOLDER: the drawn body (0.5 x 1 x 0.5 m) widened for the mouse; tune after play.
        public const float BodyHeight = 1f;   // PLACEHOLDER: the drawn body height (EntityViews: bottom at Y, top at Y + 1).
        private const int Halvings = 24;
        private const long OneMetre = 65536;

        public static AimTarget Pick(IsoCamera camera, float screenX, float screenY, float screenWidth, float screenHeight,
            ViewGround ground, FrameView shown, ulong? self)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var o = camera.RayOrigin(screenX, screenY, screenWidth, screenHeight);
            var f = camera.Forward;
            return PickRay(o.X, o.Y, o.Z, f.X, f.Y, f.Z, camera.GroundY, ground, shown, self);
        }

        // The ray o + t f (f.Y < 0). Plane points use the same float formula as IsoCamera.ScreenToGround.
        internal static AimTarget PickRay(float ox, float oy, float oz, float fx, float fy, float fz, float groundY,
            ViewGround ground, FrameView shown, ulong? self)
        {
            if (!(fy < 0f)) throw new ArgumentOutOfRangeException(nameof(fy), "The pick ray must point down.");
            AimTarget onGround = ground == null ? PlanePoint(ox, oy, oz, fx, fy, fz, groundY, out double tGround)
                : GroundPoint(ox, oy, oz, fx, fy, fz, groundY, ground, out tGround);
            if (shown == null || !self.HasValue || !shown.TryGet(self.Value, out ViewEntity me)) return onGround;
            double best = double.PositiveInfinity;
            ViewEntity snapped = default;
            foreach (ViewEntity e in shown.Entities)
            {
                if (!e.Alive || e.Team == 0 || e.Team == me.Team || e.Key == self.Value) continue;
                double t = BodyHit(ox, oy, oz, fx, fy, fz, e);
                if (t < best || (t == best && t < double.PositiveInfinity && e.Key < snapped.Key)) { best = t; snapped = e; }
            }
            if (!(best < tGround)) return onGround;
            long? yMm = ground == null ? (long?)null : (long)Math.Round((double)snapped.Y * 1000.0, MidpointRounding.AwayFromZero);
            return new AimTarget(AimKind.Unit, snapped.X, snapped.Y, snapped.Z, yMm, snapped.Key);
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

        // First t at which the ray meets the upright cylinder of the body (side, or the top cap); +inf when it misses.
        private static double BodyHit(double ox, double oy, double oz, double fx, double fy, double fz, ViewEntity e)
        {
            double r = SnapRadius, bottom = e.Y, top = e.Y + BodyHeight;
            double px = ox - e.X, pz = oz - e.Z;
            double a = fx * fx + fz * fz, b = 2 * (px * fx + pz * fz), c = px * px + pz * pz - r * r;
            double disc = b * b - 4 * a * c;
            if (a == 0 || disc < 0) return double.PositiveInfinity;
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

        private static long Along(long a, long b, long n) => a + FloorDiv((b - a) * n, 1L << Halvings);
        private static long FloorDiv(long a, long b) { long q = a / b; if (a % b != 0 && (a < 0) != (b < 0)) q--; return q; }
        private static long ToRaw(double metres) => (long)Math.Floor(metres * OneMetre);
        private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
